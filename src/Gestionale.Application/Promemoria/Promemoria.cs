using Gestionale.Application.Anagrafica;
using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Promemoria;

namespace Gestionale.Application.Promemoria;

/// <summary>Parametri di posta come si leggono: la password non esce mai, si sa solo se c'è.</summary>
public sealed class ParametriPosta
{
    public string Host { get; init; } = "";

    public int Porta { get; init; }

    public string Sicurezza { get; init; } = "";

    public string? Utente { get; init; }

    public bool ConPassword { get; init; }

    public string MittenteEmail { get; init; } = "";

    public string? MittenteNome { get; init; }

    public int Versione { get; init; }
}

/// <summary>Parametri completi per l'invio, con la password cifrata come sta nel database.</summary>
public sealed record ParametriInvio(string Host, int Porta, string Sicurezza, string? Utente, byte[]? PasswordCifrata, string MittenteEmail, string? MittenteNome);

/// <summary>Una scadenza scelta, con destinatario, consenso e dati dell'impianto (lavoro.dati_promemoria).</summary>
public sealed class DatiPromemoria
{
    public Guid ScadenzaId { get; init; }

    public string Tipo { get; init; } = "";

    public DateOnly DataScadenza { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string? Email { get; init; }

    public bool Consenso { get; init; }

    public string TipoImpianto { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string Comune { get; init; } = "";
}

/// <summary>Dati dell'impresa che firma il promemoria.</summary>
public sealed record ImpresaPromemoria(string RagioneSociale, string? Indirizzo, string? Cap, string? Comune, string? Provincia);

/// <summary>Un'email pronta: destinatario, oggetto e testo.</summary>
public sealed record MessaggioEmail(string Destinatario, string? NomeDestinatario, string Oggetto, string Testo);

/// <summary>Un promemoria dell'anteprima o dell'invio: Esclusione dice perché non parte, Errore perché non è partito.</summary>
public sealed record PromemoriaPreparato(
    Guid ScadenzaId, string Cliente, string? Email, EsclusionePromemoria? Esclusione, string? Oggetto, string? Testo, bool Inviato = false, string? Errore = null);

public abstract record EsitoPromemoria
{
    public sealed record Riuscito(IReadOnlyList<PromemoriaPreparato> Promemoria) : EsitoPromemoria;

    public sealed record NonValido(IReadOnlyList<ErrorePromemoria> Errori) : EsitoPromemoria;

    public sealed record Salvato(int Versione) : EsitoPromemoria;

    public sealed record VersioneSuperata : EsitoPromemoria;

    /// <summary>Il server di posta ha rifiutato la prova: il messaggio viene da lui.</summary>
    public sealed record InvioFallito(string Messaggio) : EsitoPromemoria;
}

public interface IRepositoryPromemoria
{
    Task<ParametriPosta?> ParametriAsync(Contesto contesto, CancellationToken ct);

    Task<ParametriInvio?> ParametriInvioAsync(Contesto contesto, CancellationToken ct);

    /// <summary>Crea o aggiorna; con passwordCifrata null resta quella salvata (e si toglie se non c'è più l'utente).</summary>
    Task<EsitoPromemoria> SalvaParametriAsync(Contesto contesto, int? versione, DatiPosta dati, byte[]? passwordCifrata, CancellationToken ct);

    Task<ImpresaPromemoria> ImpresaAsync(Contesto contesto, CancellationToken ct);

    Task<string> EmailUtenteAsync(Contesto contesto, CancellationToken ct);

    Task<IReadOnlyList<DatiPromemoria>> DatiAsync(Contesto contesto, IReadOnlyList<Guid> scadenze, CancellationToken ct);

    /// <summary>Registra il promemoria come richiamo per email della scadenza.</summary>
    Task RegistraInvioAsync(Contesto contesto, Guid scadenzaId, string oggetto, CancellationToken ct);
}

/// <summary>Spedisce email con un server SMTP; un invio apre una connessione sola per tutti i messaggi.</summary>
public interface IPostino
{
    /// <summary>Invia i messaggi e restituisce, per ognuno, null se è partito o il motivo del rifiuto.</summary>
    Task<IReadOnlyList<string?>> InviaAsync(ParametriInvio parametri, string? password, IReadOnlyList<MessaggioEmail> messaggi, CancellationToken ct);
}

/// <summary>Testo del promemoria; sta nell'infrastruttura con gli altri testi dei documenti.</summary>
public interface IComponitorePromemoria
{
    (string Oggetto, string Testo) Componi(DatiPromemoria dati, ImpresaPromemoria impresa, string? nota, DateOnly oggi);

    MessaggioEmail Prova(string destinatario, ImpresaPromemoria impresa);
}

/// <summary>
/// Promemoria email delle scadenze (ADR 0007): parametri del server, anteprima e
/// invio a mano. Riceve il promemoria solo chi ha un'email e il consenso
/// "promemoria_scadenze" in corso; ogni email partita diventa un richiamo.
/// </summary>
public sealed class ServizioPromemoria(
    IRepositoryPromemoria repository, IPostino postino, IComponitorePromemoria componitore, ICifratoreSegreti cifratore, TimeProvider orologio)
{
    private static readonly TimeZoneInfo s_fuso = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    public Task<ParametriPosta?> ParametriAsync(Contesto contesto, CancellationToken ct) => repository.ParametriAsync(contesto, ct);

    public async Task<EsitoPromemoria> SalvaParametriAsync(Contesto contesto, int? versione, DatiPosta dati, CancellationToken ct)
    {
        var (normalizzati, errori) = RegolePromemoria.Valida(dati);
        if (errori.Count > 0)
        {
            return new EsitoPromemoria.NonValido(errori);
        }

        var cifrata = normalizzati.Password is { } password ? cifratore.Cifra(System.Text.Encoding.UTF8.GetBytes(password)) : null;
        return await repository.SalvaParametriAsync(contesto, versione, normalizzati, cifrata, ct);
    }

    /// <summary>Un'email di prova a chi configura il server, per vedere che i parametri funzionano.</summary>
    public async Task<EsitoPromemoria> ProvaAsync(Contesto contesto, CancellationToken ct)
    {
        var destinatario = await repository.EmailUtenteAsync(contesto, ct);
        if (await repository.ParametriInvioAsync(contesto, ct) is not { } parametri)
        {
            return new EsitoPromemoria.NonValido([ErrorePromemoria.PostaNonConfigurata]);
        }

        var impresa = await repository.ImpresaAsync(contesto, ct);
        var esiti = await postino.InviaAsync(parametri, Password(parametri), [componitore.Prova(destinatario, impresa)], ct);
        return esiti[0] is { } errore ? new EsitoPromemoria.InvioFallito(errore) : new EsitoPromemoria.Riuscito([]);
    }

    public Task<EsitoPromemoria> AnteprimaAsync(Contesto contesto, IEnumerable<Guid>? scadenze, string? nota, CancellationToken ct) =>
        PreparaAsync(contesto, scadenze, nota, invia: false, ct);

    public Task<EsitoPromemoria> InviaAsync(Contesto contesto, IEnumerable<Guid>? scadenze, string? nota, CancellationToken ct) =>
        PreparaAsync(contesto, scadenze, nota, invia: true, ct);

    private async Task<EsitoPromemoria> PreparaAsync(Contesto contesto, IEnumerable<Guid>? scadenze, string? nota, bool invia, CancellationToken ct)
    {
        var (elenco, testoNota, errori) = RegolePromemoria.ValidaInvio(scadenze, nota);
        if (errori.Count > 0)
        {
            return new EsitoPromemoria.NonValido(errori);
        }

        var parametri = await repository.ParametriInvioAsync(contesto, ct);
        if (invia && parametri is null)
        {
            return new EsitoPromemoria.NonValido([ErrorePromemoria.PostaNonConfigurata]);
        }

        var impresa = await repository.ImpresaAsync(contesto, ct);
        var oggi = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(orologio.GetUtcNow(), s_fuso).DateTime);
        var preparati = (await repository.DatiAsync(contesto, elenco, ct)).Select(d =>
        {
            EsclusionePromemoria? esclusione = d.Email is null ? EsclusionePromemoria.SenzaEmail
                : !d.Consenso ? EsclusionePromemoria.SenzaConsenso
                : null;
            if (esclusione is not null)
            {
                return (Dati: d, Preparato: new PromemoriaPreparato(d.ScadenzaId, d.Cliente, d.Email, esclusione, null, null));
            }

            var (oggetto, testo) = componitore.Componi(d, impresa, testoNota, oggi);
            return (Dati: d, Preparato: new PromemoriaPreparato(d.ScadenzaId, d.Cliente, d.Email, null, oggetto, testo));
        }).ToList();

        if (!invia)
        {
            return new EsitoPromemoria.Riuscito([.. preparati.Select(p => p.Preparato)]);
        }

        var daInviare = preparati.Where(p => p.Preparato.Esclusione is null).ToList();
        var messaggi = daInviare.Select(p => new MessaggioEmail(p.Dati.Email!, p.Dati.Cliente, p.Preparato.Oggetto!, p.Preparato.Testo!)).ToList();
        IReadOnlyList<string?> esiti = messaggi.Count == 0 ? [] : await postino.InviaAsync(parametri!, Password(parametri!), messaggi, ct);

        var risultati = new List<PromemoriaPreparato>();
        var inviati = new Dictionary<Guid, string?>();
        for (var i = 0; i < daInviare.Count; i++)
        {
            inviati[daInviare[i].Dati.ScadenzaId] = esiti[i];
            if (esiti[i] is null)
            {
                await repository.RegistraInvioAsync(contesto, daInviare[i].Dati.ScadenzaId, daInviare[i].Preparato.Oggetto!, ct);
            }
        }

        foreach (var (_, preparato) in preparati)
        {
            risultati.Add(inviati.TryGetValue(preparato.ScadenzaId, out var errore)
                ? preparato with { Inviato = errore is null, Errore = errore }
                : preparato);
        }

        return new EsitoPromemoria.Riuscito(risultati);
    }

    private string? Password(ParametriInvio parametri) =>
        parametri.PasswordCifrata is { } cifrata ? System.Text.Encoding.UTF8.GetString(cifratore.Decifra(cifrata)) : null;
}

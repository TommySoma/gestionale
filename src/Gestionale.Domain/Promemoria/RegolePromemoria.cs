using System.Text.RegularExpressions;

namespace Gestionale.Domain.Promemoria;

/// <summary>Motivi per cui i parametri di posta o un invio non sono accettati; l'API li traduce in messaggi.</summary>
public enum ErrorePromemoria
{
    HostPostaNonValido,
    PortaPostaNonValida,
    SicurezzaPostaNonValida,
    MittentePostaNonValido,
    PasswordSenzaUtente,
    NotaTroppoLunga,
    NessunaScadenzaScelta,
    TroppeScadenzeScelte,
    PostaNonConfigurata,
}

/// <summary>Perché una scadenza scelta non riceve il promemoria.</summary>
public enum EsclusionePromemoria
{
    SenzaEmail,
    SenzaConsenso,
}

public static class SicurezzePosta
{
    public const string Nessuna = "nessuna";
    public const string StartTls = "starttls";
    public const string Ssl = "ssl";

    public static IReadOnlySet<string> Tutte { get; } = new HashSet<string>(StringComparer.Ordinal) { Nessuna, StartTls, Ssl };
}

/// <summary>Server SMTP dell'azienda. Password null significa: lascia quella salvata.</summary>
public sealed record DatiPosta(string? Host, int? Porta, string? Sicurezza, string? Utente, string? Password, string? MittenteEmail, string? MittenteNome);

/// <summary>Regole dei promemoria email (ADR 0007): parametri del server e limiti di un invio.</summary>
public static partial class RegolePromemoria
{
    public const int ScadenzeMassimePerInvio = 200;

    public const int LunghezzaMassimaNota = 1000;

    public static (DatiPosta Dati, IReadOnlyList<ErrorePromemoria> Errori) Valida(DatiPosta dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            Host = Pulisci(dati.Host)?.ToLowerInvariant(),
            Sicurezza = Pulisci(dati.Sicurezza)?.ToLowerInvariant(),
            Utente = Pulisci(dati.Utente),
            // La password non si tocca: può contenere spazi voluti. Vuota vuol dire nessun cambio.
            Password = string.IsNullOrEmpty(dati.Password) ? null : dati.Password,
            MittenteEmail = Pulisci(dati.MittenteEmail)?.ToLowerInvariant(),
            MittenteNome = Pulisci(dati.MittenteNome),
        };

        var errori = new List<ErrorePromemoria>();
        if (normalizzati.Host is null || normalizzati.Host.Any(char.IsWhiteSpace))
        {
            errori.Add(ErrorePromemoria.HostPostaNonValido);
        }

        if (normalizzati.Porta is not (>= 1 and <= 65535))
        {
            errori.Add(ErrorePromemoria.PortaPostaNonValida);
        }

        if (normalizzati.Sicurezza is null || !SicurezzePosta.Tutte.Contains(normalizzati.Sicurezza))
        {
            errori.Add(ErrorePromemoria.SicurezzaPostaNonValida);
        }

        if (normalizzati.MittenteEmail is null || !Email().IsMatch(normalizzati.MittenteEmail))
        {
            errori.Add(ErrorePromemoria.MittentePostaNonValido);
        }

        if (normalizzati.Password is not null && normalizzati.Utente is null)
        {
            errori.Add(ErrorePromemoria.PasswordSenzaUtente);
        }

        return (normalizzati, errori);
    }

    /// <summary>Le scadenze di un invio: almeno una, senza doppioni, entro il limite.</summary>
    public static (IReadOnlyList<Guid> Scadenze, string? Nota, IReadOnlyList<ErrorePromemoria> Errori) ValidaInvio(IEnumerable<Guid>? scadenze, string? nota)
    {
        var elenco = (scadenze ?? []).Where(s => s != Guid.Empty).Distinct().ToList();
        var testo = Pulisci(nota);
        var errori = new List<ErrorePromemoria>();
        if (elenco.Count == 0)
        {
            errori.Add(ErrorePromemoria.NessunaScadenzaScelta);
        }
        else if (elenco.Count > ScadenzeMassimePerInvio)
        {
            errori.Add(ErrorePromemoria.TroppeScadenzeScelte);
        }

        if (testo is { Length: > LunghezzaMassimaNota })
        {
            errori.Add(ErrorePromemoria.NotaTroppoLunga);
        }

        return (elenco, testo, errori);
    }

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex Email();
}

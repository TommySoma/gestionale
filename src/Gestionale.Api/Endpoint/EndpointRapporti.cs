using System.Security.Claims;
using System.Text.Json;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Rapporti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

public sealed record RichiestaNuovoRapporto(Guid? ImpiantoId, Guid? GeneratoreId, Guid? InterventoId, string? Modello, DateOnly? DataControllo);

/// <summary>Dati nella codifica del tracciato: chiavi di norma.tracciati_campi, valori testo o numero.</summary>
public sealed record RichiestaBozzaRapporto(DateOnly? DataControllo, JsonElement? Dati, int? Versione);

public sealed record RichiestaBollinoRapporto(Guid? TipoBollinoId, int? Versione);

public sealed record RichiestaAcquistoBollini(Guid? TipoBollinoId, DateOnly? AcquistatoIl, IReadOnlyList<string>? Codici, string? Note);

public sealed record RichiestaInvio(string? Modello);

public sealed record RichiestaEsitoRiga(Guid? RapportoControlloId, string? Esito, string? Causa);

public sealed record RichiestaEsitiInvio(IReadOnlyList<RichiestaEsitoRiga>? Esiti, int? Versione);

public sealed record RispostaScritturaRapporto(Guid Id, int Versione);

public sealed record RispostaInvioPreparato(Guid Id, int Versione, int Righe, IReadOnlyList<Guid> Esclusi);

public sealed record RispostaBolliniRegistrati(int Numero);

/// <summary>
/// Rapporti di controllo, bollini e invii al SIERT (ADR 0006). Ogni modifica
/// richiede la versione letta. Il Tecnico compila i rapporti suoi o dei suoi
/// interventi, vede i loro bollini e non vede gli invii; firma e annullamento
/// di un rapporto firmato richiedono il permesso di firma.
/// </summary>
internal static class EndpointRapporti
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var rapporti = api.MapGroup("/rapporti-controllo");
        rapporti.MapGet("/", CercaAsync).RichiedePermesso(CodiciPermesso.RapportiLettura);
        rapporti.MapGet("/tracciato/{modello}", TracciatoAsync).RichiedePermesso(CodiciPermesso.RapportiLettura);
        rapporti.MapGet("/{rapportoId:guid}", RapportoAsync).RichiedePermesso(CodiciPermesso.RapportiLettura);
        rapporti.MapPost("/", CreaAsync).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapPut("/{rapportoId:guid}", AggiornaAsync).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapPost("/{rapportoId:guid}/bollino", AssegnaBollinoAsync).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapDelete("/{rapportoId:guid}/bollino", LiberaBollinoAsync).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapPost("/{rapportoId:guid}/convalida", (Guid rapportoId, RichiestaVersione r, ClaimsPrincipal u, ServizioRapporti s, CancellationToken ct) =>
            PassaggioAsync(rapportoId, r.Versione, null, PassaggioRapporto.Convalida, u, s, ct)).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapPost("/{rapportoId:guid}/riapertura", (Guid rapportoId, RichiestaVersione r, ClaimsPrincipal u, ServizioRapporti s, CancellationToken ct) =>
            PassaggioAsync(rapportoId, r.Versione, null, PassaggioRapporto.Riapertura, u, s, ct)).RichiedePermesso(CodiciPermesso.RapportiScrittura);
        rapporti.MapPost("/{rapportoId:guid}/firma", (Guid rapportoId, RichiestaVersione r, ClaimsPrincipal u, ServizioRapporti s, CancellationToken ct) =>
            PassaggioAsync(rapportoId, r.Versione, null, PassaggioRapporto.Firma, u, s, ct)).RichiedePermesso(CodiciPermesso.RapportiFirma);
        rapporti.MapPost("/{rapportoId:guid}/annullamento", (Guid rapportoId, RichiestaAnnullamento r, ClaimsPrincipal u, ServizioRapporti s, CancellationToken ct) =>
            PassaggioAsync(rapportoId, r.Versione, r.Motivo, PassaggioRapporto.Annullamento, u, s, ct)).RichiedePermesso(CodiciPermesso.RapportiScrittura);

        var bollini = api.MapGroup("/bollini");
        bollini.MapGet("/tipi", TipiBollinoAsync).RichiedePermesso(CodiciPermesso.BolliniLettura);
        bollini.MapGet("/", CercaBolliniAsync).RichiedePermesso(CodiciPermesso.BolliniLettura);
        bollini.MapPost("/", RegistraBolliniAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);
        bollini.MapPost("/{bollinoId:guid}/annullamento", AnnullaBollinoAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);

        var invii = api.MapGroup("/invii-catasto");
        invii.MapGet("/", CercaInviiAsync).RichiedePermesso(CodiciPermesso.BolliniLettura);
        invii.MapGet("/{invioId:guid}", InvioAsync).RichiedePermesso(CodiciPermesso.BolliniLettura);
        invii.MapGet("/{invioId:guid}/file", FileAsync).RichiedePermesso(CodiciPermesso.BolliniLettura);
        invii.MapPost("/", PreparaAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);
        invii.MapPost("/{invioId:guid}/caricamento", SegnaCaricatoAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);
        invii.MapPost("/{invioId:guid}/annullamento", AnnullaInvioAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);
        invii.MapPost("/{invioId:guid}/esiti", RegistraEsitiAsync).RichiedePermesso(CodiciPermesso.BolliniScrittura);
    }

    // --- Rapporti -----------------------------------------------------------

    private static async Task<IResult> CercaAsync(
        ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct,
        Guid? impianto = null, Guid? intervento = null, string? stato = null, bool daTrasmettere = false, int pagina = 1, int dimensione = 50) =>
        Results.Ok(await servizio.CercaAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.RapportiLettura),
            new FiltroRapporti(impianto, intervento, stato, daTrasmettere), pagina, dimensione, ct));

    private static async Task<IResult> TracciatoAsync(string modello, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        await servizio.TracciatoAsync(Contesto(utente), modello, ct) is { Count: > 0 } campi
            ? Results.Ok(campi)
            : NonTrovato(Messaggi.ModelloTracciatoNonTrovato);

    private static async Task<IResult> RapportoAsync(Guid rapportoId, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        await servizio.RapportoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.RapportiLettura), rapportoId, ct) is { } rapporto
            ? Results.Ok(rapporto)
            : NonTrovato(Messaggi.RapportoNonTrovato);

    private static async Task<IResult> CreaAsync(RichiestaNuovoRapporto richiesta, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        Risposta(
            await servizio.CreaAsync(
                Contesto(utente), Ambito(utente, CodiciPermesso.RapportiScrittura),
                new DatiNuovoRapporto(richiesta.ImpiantoId, richiesta.GeneratoreId, richiesta.InterventoId, richiesta.Modello, richiesta.DataControllo), ct),
            Messaggi.RapportoNonTrovato, id => $"/api/rapporti-controllo/{id}");

    private static async Task<IResult> AggiornaAsync(
        Guid rapportoId, RichiestaBozzaRapporto richiesta, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.AggiornaAsync(
                    Contesto(utente), Ambito(utente, CodiciPermesso.RapportiScrittura), rapportoId, versione,
                    new DatiBozzaRapporto(richiesta.DataControllo, richiesta.Dati), ct),
                Messaggi.RapportoNonTrovato);

    private static async Task<IResult> AssegnaBollinoAsync(
        Guid rapportoId, RichiestaBollinoRapporto richiesta, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.AssegnaBollinoAsync(
                    Contesto(utente), Ambito(utente, CodiciPermesso.RapportiScrittura), rapportoId, versione, richiesta.TipoBollinoId, ct),
                Messaggi.RapportoNonTrovato);

    private static async Task<IResult> LiberaBollinoAsync(
        Guid rapportoId, int? versione, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.LiberaBollinoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.RapportiScrittura), rapportoId, v, ct),
                Messaggi.RapportoNonTrovato);

    private static async Task<IResult> PassaggioAsync(
        Guid rapportoId, int? versione, string? motivo, PassaggioRapporto passaggio, ClaimsPrincipal utente, ServizioRapporti servizio, CancellationToken ct)
    {
        if (versione is not { } v)
        {
            return Problemi.RichiestaNonValida(Messaggi.VersioneMancante);
        }

        // La firma richiede il suo permesso dalla policy; qui conta l'ambito dei
        // permessi di scrittura, oppure della firma per chi firma senza compilare.
        var codice = passaggio == PassaggioRapporto.Firma ? CodiciPermesso.RapportiFirma : CodiciPermesso.RapportiScrittura;
        var puoFirmare = utente.AmbitoPermesso(CodiciPermesso.RapportiFirma) is not null;
        return Risposta(
            await servizio.CambiaStatoAsync(Contesto(utente), Ambito(utente, codice), rapportoId, v, passaggio, motivo, puoFirmare, ct),
            Messaggi.RapportoNonTrovato);
    }

    // --- Bollini ------------------------------------------------------------

    private static async Task<IResult> TipiBollinoAsync(ClaimsPrincipal utente, ServizioBollini servizio, CancellationToken ct) =>
        Results.Ok(await servizio.TipiAsync(Contesto(utente), ct));

    private static async Task<IResult> CercaBolliniAsync(
        ClaimsPrincipal utente, ServizioBollini servizio, CancellationToken ct,
        string? stato = null, Guid? tipo = null, string? codice = null, int pagina = 1, int dimensione = 50) =>
        Results.Ok(await servizio.CercaAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.BolliniLettura), new FiltroBollini(stato, tipo, codice), pagina, dimensione, ct));

    private static async Task<IResult> RegistraBolliniAsync(
        RichiestaAcquistoBollini richiesta, ClaimsPrincipal utente, ServizioBollini servizio, CancellationToken ct) =>
        Risposta(
            await servizio.RegistraAsync(
                Contesto(utente), new DatiAcquistoBollini(richiesta.TipoBollinoId, richiesta.AcquistatoIl, richiesta.Codici, richiesta.Note), ct),
            Messaggi.BollinoNonTrovato);

    private static async Task<IResult> AnnullaBollinoAsync(
        Guid bollinoId, RichiestaAnnullamento richiesta, ClaimsPrincipal utente, ServizioBollini servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AnnullaAsync(Contesto(utente), bollinoId, versione, richiesta.Motivo, ct), Messaggi.BollinoNonTrovato);

    // --- Invii --------------------------------------------------------------

    private static async Task<IResult> CercaInviiAsync(
        ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct, int pagina = 1, int dimensione = 50) =>
        SoloAmbitoCompleto(utente) ?? Results.Ok(await servizio.CercaAsync(Contesto(utente), pagina, dimensione, ct));

    private static async Task<IResult> InvioAsync(Guid invioId, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        SoloAmbitoCompleto(utente)
        ?? (await servizio.InvioAsync(Contesto(utente), invioId, ct) is { } invio ? Results.Ok(invio) : NonTrovato(Messaggi.InvioNonTrovato));

    private static async Task<IResult> FileAsync(Guid invioId, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        SoloAmbitoCompleto(utente)
        ?? (await servizio.FileAsync(Contesto(utente), invioId, ct) is { } file
            ? Results.File(file.Contenuto, "text/plain; charset=utf-8", file.NomeFile)
            : NonTrovato(Messaggi.InvioNonTrovato));

    private static async Task<IResult> PreparaAsync(RichiestaInvio richiesta, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        Risposta(await servizio.PreparaAsync(Contesto(utente), richiesta.Modello, ct), Messaggi.InvioNonTrovato, id => $"/api/invii-catasto/{id}");

    private static async Task<IResult> SegnaCaricatoAsync(
        Guid invioId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.SegnaCaricatoAsync(Contesto(utente), invioId, versione, ct), Messaggi.InvioNonTrovato);

    private static async Task<IResult> AnnullaInvioAsync(
        Guid invioId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AnnullaAsync(Contesto(utente), invioId, versione, ct), Messaggi.InvioNonTrovato);

    private static async Task<IResult> RegistraEsitiAsync(
        Guid invioId, RichiestaEsitiInvio richiesta, ClaimsPrincipal utente, ServizioInvii servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.RegistraEsitiAsync(
                    Contesto(utente), invioId, versione,
                    [.. (richiesta.Esiti ?? []).Select(e => new EsitoRigaInvio(e.RapportoControlloId, e.Esito, e.Causa))], ct),
                Messaggi.InvioNonTrovato);

    // --- Dettagli -----------------------------------------------------------

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso Ambito(ClaimsPrincipal utente, string codice) =>
        utente.AmbitoPermesso(codice) ?? AmbitoPermesso.Propri;

    // Un invio contiene i rapporti di tutta l'azienda.
    private static IResult? SoloAmbitoCompleto(ClaimsPrincipal utente) =>
        Ambito(utente, CodiciPermesso.BolliniLettura) == AmbitoPermesso.Tutti ? null : Problemi.Vietato(Messaggi.InviiSoloAmbitoCompleto);

    private static IResult Risposta(EsitoRapporto esito, string nonTrovato, Func<Guid, string>? creato = null) => esito switch
    {
        EsitoRapporto.InvioPreparato p => Results.Created(creato?.Invoke(p.Id), new RispostaInvioPreparato(p.Id, p.Versione, p.Righe, p.Esclusi)),
        EsitoRapporto.BolliniRegistrati b => Results.Created((string?)null, new RispostaBolliniRegistrati(b.Numero)),
        EsitoRapporto.Riuscita r when creato is not null => Results.Created(creato(r.Id), new RispostaScritturaRapporto(r.Id, r.Versione)),
        EsitoRapporto.Riuscita r => Results.Ok(new RispostaScritturaRapporto(r.Id, r.Versione)),
        EsitoRapporto.NonValida n => Problemi.RichiestaNonValida(Problemi.Formatta(
            Messaggi.DatiRapportoNonValidi,
            string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + (n.Chiavi.Count > 0 ? $" ({string.Join(", ", n.Chiavi)})." : "."))),
        EsitoRapporto.Bloccata b => Problemi.Conflitto(Problemi.Formatta(
            Messaggi.RapportoConErroriBloccanti,
            string.Join("; ", b.Errori.Select(e => e.Campo is null ? e.Messaggio : $"{e.Campo}: {e.Messaggio}")))),
        EsitoRapporto.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoRapporto.StatoNonConsentito s => Problemi.Conflitto(
            Problemi.Formatta(Messaggi.OperazioneRapportoNonConsentita, Testo("StatoRapporto_" + s.Stato))),
        _ => NonTrovato(nonTrovato),
    };

    private static IResult NonTrovato(string messaggio) =>
        Results.Problem(detail: messaggio, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(string nome) => Messaggi.ResourceManager.GetString(nome, Messaggi.Culture) ?? nome;
}

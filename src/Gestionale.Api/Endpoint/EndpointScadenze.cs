using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Scadenze;
using Gestionale.Domain.Scadenze;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

public sealed record RichiestaDataScadenza(DateOnly? Data, int? Versione);

public sealed record RichiestaRichiamo(string? Canale, string? Esito, DateOnly? RichiamareIl, Guid? InterventoId, string? Note, int? Versione);

public sealed record RispostaScritturaScadenza(Guid Id, int Versione);

/// <summary>
/// Scadenze e richiami. Il database le calcola dalla periodicità dell'impianto e
/// dalle regole di cadenza; qui si leggono, si corregge la data (che poi il
/// ricalcolo non sposta più) e si registrano i tentativi di contatto. Ogni
/// modifica richiede la versione letta. Il Tecnico legge solo le scadenze degli
/// impianti dei propri interventi, senza telefono né richiami.
/// </summary>
internal static class EndpointScadenze
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var scadenze = api.MapGroup("/scadenze");

        scadenze.MapGet("/", InArrivoAsync).RichiedePermesso(CodiciPermesso.ScadenzeLettura);
        scadenze.MapGet("/{scadenzaId:guid}", ScadenzaAsync).RichiedePermesso(CodiciPermesso.ScadenzeLettura);
        scadenze.MapPut("/{scadenzaId:guid}/data", CorreggiDataAsync).RichiedePermesso(CodiciPermesso.ScadenzeScrittura);
        scadenze.MapPost("/{scadenzaId:guid}/richiami", RegistraRichiamoAsync).RichiedePermesso(CodiciPermesso.ScadenzeScrittura);

        api.MapGet("/impianti/{impiantoId:guid}/scadenze", DellImpiantoAsync).RichiedePermesso(CodiciPermesso.ScadenzeLettura);
    }

    // Senza giorni: le scadute e quelle dei prossimi 30 giorni.
    private static async Task<IResult> InArrivoAsync(
        ClaimsPrincipal utente, ServizioScadenze servizio, CancellationToken ct,
        int giorni = 30, string? tipo = null, string? richiamo = null, int pagina = 1, int dimensione = 50)
    {
        var (risultato, errori) = await servizio.InArrivoAsync(
            Contesto(utente), Ambito(utente), new FiltroScadenze(giorni, tipo, richiamo), pagina, dimensione, ct);
        return risultato is null ? NonValida(errori) : Results.Ok(risultato);
    }

    private static async Task<IResult> ScadenzaAsync(Guid scadenzaId, ClaimsPrincipal utente, ServizioScadenze servizio, CancellationToken ct) =>
        await servizio.ScadenzaAsync(Contesto(utente), Ambito(utente), scadenzaId, ct) is { } scadenza
            ? Results.Ok(scadenza)
            : NonTrovata();

    private static async Task<IResult> DellImpiantoAsync(Guid impiantoId, ClaimsPrincipal utente, ServizioScadenze servizio, CancellationToken ct) =>
        await servizio.DellImpiantoAsync(Contesto(utente), Ambito(utente), impiantoId, ct) is { } scadenze
            ? Results.Ok(scadenze)
            : NonTrovata();

    private static async Task<IResult> CorreggiDataAsync(
        Guid scadenzaId, RichiestaDataScadenza richiesta, ClaimsPrincipal utente, ServizioScadenze servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.CorreggiDataAsync(Contesto(utente), scadenzaId, versione, richiesta.Data, ct));

    private static async Task<IResult> RegistraRichiamoAsync(
        Guid scadenzaId, RichiestaRichiamo richiesta, ClaimsPrincipal utente, ServizioScadenze servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.RegistraRichiamoAsync(
                    Contesto(utente), scadenzaId, versione,
                    new DatiRichiamo(richiesta.Canale, richiesta.Esito, richiesta.RichiamareIl, richiesta.InterventoId, richiesta.Note), ct),
                creato: true);

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso Ambito(ClaimsPrincipal utente) =>
        utente.AmbitoPermesso(CodiciPermesso.ScadenzeLettura) ?? AmbitoPermesso.Propri;

    private static IResult Risposta(EsitoScadenza esito, bool creato = false) => esito switch
    {
        EsitoScadenza.Riuscita r when creato => Results.Created($"/api/scadenze/{r.Id}", new RispostaScritturaScadenza(r.Id, r.Versione)),
        EsitoScadenza.Riuscita r => Results.Ok(new RispostaScritturaScadenza(r.Id, r.Versione)),
        EsitoScadenza.NonValida n => NonValida(n.Errori),
        EsitoScadenza.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoScadenza.NonAperta a => Problemi.Conflitto(Problemi.Formatta(Messaggi.ScadenzaNonAperta, Testo("StatoScadenza_" + a.Stato))),
        _ => NonTrovata(),
    };

    private static IResult NonValida(IReadOnlyList<ErroreScadenza> errori) =>
        Problemi.RichiestaNonValida(
            Problemi.Formatta(Messaggi.DatiScadenzaNonValidi, string.Join("; ", errori.Select(e => Testo(e.ToString()))) + "."));

    private static IResult NonTrovata() =>
        Results.Problem(detail: Messaggi.ScadenzaNonTrovata, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(string nome) => Messaggi.ResourceManager.GetString(nome, Messaggi.Culture) ?? nome;
}

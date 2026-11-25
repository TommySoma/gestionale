using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Commerciale;
using Gestionale.Domain.Commerciale;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>PrezzoIniziale e ValidoDal valgono solo alla creazione: dopo, i prezzi si aggiungono con /prezzi.</summary>
public sealed record RichiestaVoce(
    string? Codice,
    string? Descrizione,
    string? Tipo,
    string? Unita,
    decimal? AliquotaIva,
    bool? Attiva,
    string? Note,
    decimal? PrezzoIniziale,
    DateOnly? ValidoDal,
    int? Versione);

public sealed record RichiestaPrezzo(decimal? Prezzo, DateOnly? ValidoDal);

public sealed record RichiestaRigaPreventivo(
    Guid? VoceId,
    string? Tipo,
    string? Codice,
    string? Descrizione,
    string? Unita,
    decimal? Quantita,
    decimal? PrezzoUnitario,
    decimal? ScontoPercentuale,
    decimal? AliquotaIva);

public sealed record RichiestaPreventivo(
    Guid? ClienteId,
    Guid? SedeId,
    Guid? ImpiantoId,
    string? Oggetto,
    string? Note,
    int? ValiditaGiorni,
    IReadOnlyList<RichiestaRigaPreventivo>? Righe,
    int? Versione);

public sealed record RichiestaConversione(int? Versione, string? Tipo);

public sealed record RispostaScritturaCommerciale(Guid Id, int Versione, int? Anno, int? Numero, Guid? InterventoId);

/// <summary>
/// Listino prezzi e preventivi. Ogni modifica richiede la versione letta. La bozza
/// si modifica e si elimina; all'emissione prende il numero e da lì cambia solo lo
/// stato. Generare l'intervento richiede anche il permesso di scrivere gli interventi.
/// </summary>
internal static class EndpointCommerciale
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var listino = api.MapGroup("/listino");
        listino.MapGet("/", CercaVociAsync).RichiedePermesso(CodiciPermesso.ListinoLettura);
        listino.MapGet("/{voceId:guid}", VoceAsync).RichiedePermesso(CodiciPermesso.ListinoLettura);
        listino.MapPost("/", CreaVoceAsync).RichiedePermesso(CodiciPermesso.ListinoScrittura);
        listino.MapPut("/{voceId:guid}", AggiornaVoceAsync).RichiedePermesso(CodiciPermesso.ListinoScrittura);
        listino.MapPost("/{voceId:guid}/prezzi", AggiungiPrezzoAsync).RichiedePermesso(CodiciPermesso.ListinoScrittura);

        var preventivi = api.MapGroup("/preventivi");
        preventivi.MapGet("/", CercaPreventiviAsync).RichiedePermesso(CodiciPermesso.PreventiviLettura);
        preventivi.MapGet("/{preventivoId:guid}", PreventivoAsync).RichiedePermesso(CodiciPermesso.PreventiviLettura);
        preventivi.MapGet("/{preventivoId:guid}/pdf", PdfAsync).RichiedePermesso(CodiciPermesso.PreventiviLettura);
        preventivi.MapPost("/", CreaPreventivoAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPut("/{preventivoId:guid}", AggiornaPreventivoAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapDelete("/{preventivoId:guid}", EliminaPreventivoAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPost("/{preventivoId:guid}/emissione", EmettiAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPost("/{preventivoId:guid}/accettazione", AccettaAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPost("/{preventivoId:guid}/rifiuto", RifiutaAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPost("/{preventivoId:guid}/annullamento", AnnullaAsync).RichiedePermesso(CodiciPermesso.PreventiviScrittura);
        preventivi.MapPost("/{preventivoId:guid}/intervento", ConvertiAsync)
            .RichiedePermesso(CodiciPermesso.PreventiviScrittura)
            .RichiedePermesso(CodiciPermesso.InterventiScrittura);
    }

    // --- Listino ------------------------------------------------------------

    private static async Task<IResult> CercaVociAsync(
        ClaimsPrincipal utente, ServizioListino servizio, CancellationToken ct,
        string? cerca = null, bool tutte = false, int pagina = 1, int dimensione = 50) =>
        Results.Ok(await servizio.CercaAsync(Contesto(utente), cerca, soloAttive: !tutte, pagina, dimensione, ct));

    private static async Task<IResult> VoceAsync(Guid voceId, ClaimsPrincipal utente, ServizioListino servizio, CancellationToken ct) =>
        await servizio.VoceAsync(Contesto(utente), voceId, ct) is { } voce ? Results.Ok(voce) : NonTrovata(Messaggi.VoceNonTrovata);

    private static async Task<IResult> CreaVoceAsync(RichiestaVoce richiesta, ClaimsPrincipal utente, ServizioListino servizio, CancellationToken ct)
    {
        // Un prezzo senza data vale da oggi (nel fuso italiano, lo decide il database).
        var prezzo = richiesta.PrezzoIniziale is null ? null : new DatiPrezzo(richiesta.PrezzoIniziale, richiesta.ValidoDal);
        return Risposta(await servizio.CreaAsync(Contesto(utente), Dati(richiesta), prezzo, ct), Messaggi.VoceNonTrovata, id => $"/api/listino/{id}");
    }

    private static async Task<IResult> AggiornaVoceAsync(
        Guid voceId, RichiestaVoce richiesta, ClaimsPrincipal utente, ServizioListino servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAsync(Contesto(utente), voceId, versione, Dati(richiesta), ct), Messaggi.VoceNonTrovata);

    private static async Task<IResult> AggiungiPrezzoAsync(
        Guid voceId, RichiestaPrezzo richiesta, ClaimsPrincipal utente, ServizioListino servizio, CancellationToken ct) =>
        Risposta(
            await servizio.AggiungiPrezzoAsync(Contesto(utente), voceId, new DatiPrezzo(richiesta.Prezzo, richiesta.ValidoDal), ct),
            Messaggi.VoceNonTrovata, id => $"/api/listino/{id}");

    // --- Preventivi ---------------------------------------------------------

    private static async Task<IResult> CercaPreventiviAsync(
        ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct,
        string? cerca = null, string? stato = null, Guid? cliente = null, Guid? impianto = null, int pagina = 1, int dimensione = 50) =>
        Results.Ok(await servizio.CercaAsync(Contesto(utente), new FiltroPreventivi(cerca, stato, cliente, impianto), pagina, dimensione, ct));

    private static async Task<IResult> PreventivoAsync(Guid preventivoId, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        await servizio.PreventivoAsync(Contesto(utente), preventivoId, ct) is { } preventivo
            ? Results.Ok(preventivo)
            : NonTrovata(Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> PdfAsync(Guid preventivoId, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        await servizio.PdfAsync(Contesto(utente), preventivoId, ct) is EsitoPdfPreventivo.Pronto p
            ? Results.File(p.Pdf, "application/pdf", p.NomeFile)
            : NonTrovata(Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> CreaPreventivoAsync(RichiestaPreventivo richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        Risposta(
            await servizio.CreaAsync(Contesto(utente), Dati(richiesta), Righe(richiesta), ct),
            Messaggi.PreventivoNonTrovato, id => $"/api/preventivi/{id}");

    private static async Task<IResult> AggiornaPreventivoAsync(
        Guid preventivoId, RichiestaPreventivo richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAsync(Contesto(utente), preventivoId, versione, Dati(richiesta), Righe(richiesta), ct), Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> EliminaPreventivoAsync(
        Guid preventivoId, int? versione, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaAsync(Contesto(utente), preventivoId, v, ct), Messaggi.PreventivoNonTrovato, eliminazione: true);

    private static async Task<IResult> EmettiAsync(
        Guid preventivoId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EmettiAsync(Contesto(utente), preventivoId, versione, ct), Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> AccettaAsync(
        Guid preventivoId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AccettaAsync(Contesto(utente), preventivoId, versione, ct), Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> RifiutaAsync(
        Guid preventivoId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.RifiutaAsync(Contesto(utente), preventivoId, versione, ct), Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> AnnullaAsync(
        Guid preventivoId, RichiestaAnnullamento richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AnnullaAsync(Contesto(utente), preventivoId, versione, richiesta.Motivo, ct), Messaggi.PreventivoNonTrovato);

    private static async Task<IResult> ConvertiAsync(
        Guid preventivoId, RichiestaConversione richiesta, ClaimsPrincipal utente, ServizioPreventivi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(
                await servizio.ConvertiAsync(Contesto(utente), preventivoId, versione, richiesta.Tipo, ct),
                Messaggi.PreventivoNonTrovato, conversione: true);

    // --- Dettagli -----------------------------------------------------------

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    private static DatiVoce Dati(RichiestaVoce r) => new(r.Codice, r.Descrizione, r.Tipo, r.Unita, r.AliquotaIva, r.Attiva, r.Note);

    private static DatiPreventivo Dati(RichiestaPreventivo r) => new(r.ClienteId, r.SedeId, r.ImpiantoId, r.Oggetto, r.Note, r.ValiditaGiorni);

    private static List<RigaPreventivo> Righe(RichiestaPreventivo r) =>
        [.. (r.Righe ?? []).Select(x => new RigaPreventivo(
            x.VoceId, new DatiRiga(x.Tipo, x.Codice, x.Descrizione, x.Unita, x.Quantita, x.PrezzoUnitario, x.ScontoPercentuale, x.AliquotaIva)))];

    private static IResult Risposta(
        EsitoCommerciale esito, string nonTrovata, Func<Guid, string>? creato = null, bool eliminazione = false, bool conversione = false) => esito switch
        {
            EsitoCommerciale.Riuscita _ when eliminazione => Results.NoContent(),
            EsitoCommerciale.Riuscita r when conversione && r.InterventoId is { } intervento =>
                Results.Created($"/api/interventi/{intervento}", Scrittura(r)),
            EsitoCommerciale.Riuscita r when creato is not null => Results.Created(creato(r.Id), Scrittura(r)),
            EsitoCommerciale.Riuscita r => Results.Ok(Scrittura(r)),
            EsitoCommerciale.NonValida n => Problemi.RichiestaNonValida(
                Problemi.Formatta(Messaggi.DatiCommercialiNonValidi, string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + ".")),
            EsitoCommerciale.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
            EsitoCommerciale.StatoNonConsentito s => Problemi.Conflitto(
                Problemi.Formatta(Messaggi.StatoPreventivoNonConsentito, Testo("StatoPreventivo_" + s.Stato))),
            EsitoCommerciale.Duplicata d => Problemi.Conflitto(Testo(d.Motivo.ToString())),
            _ => NonTrovata(nonTrovata),
        };

    private static RispostaScritturaCommerciale Scrittura(EsitoCommerciale.Riuscita r) => new(r.Id, r.Versione, r.Anno, r.Numero, r.InterventoId);

    private static IResult NonTrovata(string messaggio) =>
        Results.Problem(detail: messaggio, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(string nome) => Messaggi.ResourceManager.GetString(nome, Messaggi.Culture) ?? nome;
}

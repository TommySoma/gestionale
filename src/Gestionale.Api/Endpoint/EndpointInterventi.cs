using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>Inizio e fine con fuso orario (ISO 8601); senza, l'intervento resta da pianificare.</summary>
public sealed record RichiestaIntervento(
    Guid? ImpiantoId,
    Guid? GeneratoreId,
    string? Tipo,
    string? Descrizione,
    DateTimeOffset? Inizio,
    DateTimeOffset? Fine,
    Guid? TecnicoUtenteId,
    int? Versione);

public sealed record RichiestaVersione(int? Versione);

public sealed record RichiestaRiga(
    string? Tipo,
    string? Codice,
    string? Descrizione,
    string? Unita,
    decimal? Quantita,
    decimal? PrezzoUnitario,
    decimal? ScontoPercentuale,
    decimal? AliquotaIva);

public sealed record RichiestaChiusura(int? Versione, string? Esito, string? Note, IReadOnlyList<RichiestaRiga>? Righe);

public sealed record RichiestaAnnullamento(int? Versione, string? Motivo);

public sealed record RispostaScritturaIntervento(Guid Id, int Versione, int Anno, int Numero);

/// <summary>
/// Interventi e calendario. Ogni modifica richiede la versione letta. Gli
/// interventi non si cancellano: si annullano con un motivo. Il Tecnico vede,
/// avvia e chiude solo i propri.
/// </summary>
internal static class EndpointInterventi
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var interventi = api.MapGroup("/interventi");

        interventi.MapGet("/", CercaAsync).RichiedePermesso(CodiciPermesso.InterventiLettura);
        interventi.MapGet("/tecnici", TecniciAsync).RichiedePermesso(CodiciPermesso.InterventiLettura);
        interventi.MapGet("/{interventoId:guid}", InterventoAsync).RichiedePermesso(CodiciPermesso.InterventiLettura);
        interventi.MapGet("/{interventoId:guid}/rapportino", RapportinoAsync).RichiedePermesso(CodiciPermesso.InterventiLettura);
        interventi.MapPost("/", CreaAsync).RichiedePermesso(CodiciPermesso.InterventiScrittura);
        interventi.MapPut("/{interventoId:guid}", AggiornaAsync).RichiedePermesso(CodiciPermesso.InterventiScrittura);
        interventi.MapPost("/{interventoId:guid}/avvio", AvviaAsync).RichiedePermesso(CodiciPermesso.InterventiScrittura);
        interventi.MapPost("/{interventoId:guid}/chiusura", ChiudiAsync).RichiedePermesso(CodiciPermesso.InterventiScrittura);
        interventi.MapPost("/{interventoId:guid}/annullamento", AnnullaAsync).RichiedePermesso(CodiciPermesso.InterventiScrittura);
    }

    private static async Task<IResult> CercaAsync(
        ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct,
        DateTimeOffset? da = null, DateTimeOffset? a = null, Guid? tecnico = null, Guid? impianto = null, string? stato = null,
        int pagina = 1, int dimensione = 50) =>
        Results.Ok(await servizio.CercaAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.InterventiLettura),
            new FiltroInterventi(da, a, tecnico, impianto, stato), pagina, dimensione, ct));

    private static async Task<IResult> TecniciAsync(ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        Results.Ok(await servizio.TecniciAsync(Contesto(utente), ct));

    private static async Task<IResult> InterventoAsync(Guid interventoId, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        await servizio.InterventoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.InterventiLettura), interventoId, ct) is { } intervento
            ? Results.Ok(intervento)
            : NonTrovato();

    // PDF del rapportino (ADR 0005): solo per un intervento completato.
    private static async Task<IResult> RapportinoAsync(Guid interventoId, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        await servizio.RapportinoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.InterventiLettura), interventoId, ct) switch
        {
            EsitoRapportino.Pronto p => Results.File(p.Pdf, "application/pdf", p.NomeFile),
            EsitoRapportino.NonChiuso n => Problemi.Conflitto(Problemi.Formatta(Messaggi.RapportinoSoloCompletati, Testo("StatoIntervento_" + n.Stato))),
            _ => NonTrovato(),
        };

    private static async Task<IResult> CreaAsync(RichiestaIntervento richiesta, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        Risposta(await servizio.CreaAsync(Contesto(utente), AmbitoScrittura(utente), Dati(richiesta), ct), creato: true);

    private static async Task<IResult> AggiornaAsync(
        Guid interventoId, RichiestaIntervento richiesta, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAsync(Contesto(utente), AmbitoScrittura(utente), interventoId, versione, Dati(richiesta), ct));

    private static async Task<IResult> AvviaAsync(
        Guid interventoId, RichiestaVersione richiesta, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AvviaAsync(Contesto(utente), AmbitoScrittura(utente), interventoId, versione, ct));

    private static async Task<IResult> ChiudiAsync(
        Guid interventoId, RichiestaChiusura richiesta, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.ChiudiAsync(Contesto(utente), AmbitoScrittura(utente), interventoId, versione, Dati(richiesta), ct));

    private static async Task<IResult> AnnullaAsync(
        Guid interventoId, RichiestaAnnullamento richiesta, ClaimsPrincipal utente, ServizioInterventi servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AnnullaAsync(Contesto(utente), AmbitoScrittura(utente), interventoId, versione, richiesta.Motivo, ct));

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso Ambito(ClaimsPrincipal utente, string codice) =>
        utente.AmbitoPermesso(codice) ?? AmbitoPermesso.Propri;

    // Chi scrive deve anche poter leggere: vale l'ambito più stretto dei due.
    private static AmbitoPermesso AmbitoScrittura(ClaimsPrincipal utente) =>
        Ambito(utente, CodiciPermesso.InterventiScrittura) == AmbitoPermesso.Tutti && Ambito(utente, CodiciPermesso.InterventiLettura) == AmbitoPermesso.Tutti
            ? AmbitoPermesso.Tutti
            : AmbitoPermesso.Propri;

    private static DatiIntervento Dati(RichiestaIntervento r) =>
        new(r.ImpiantoId, r.GeneratoreId, r.Tipo, r.Descrizione, r.Inizio, r.Fine, r.TecnicoUtenteId);

    private static DatiChiusura Dati(RichiestaChiusura r) =>
        new(r.Esito, r.Note, r.Righe?.Select(x => new DatiRiga(
            x.Tipo, x.Codice, x.Descrizione, x.Unita, x.Quantita, x.PrezzoUnitario, x.ScontoPercentuale, x.AliquotaIva)).ToList());

    private static IResult Risposta(EsitoIntervento esito, bool creato = false) => esito switch
    {
        EsitoIntervento.Riuscita r when creato => Results.Created($"/api/interventi/{r.Id}", Scrittura(r)),
        EsitoIntervento.Riuscita r => Results.Ok(Scrittura(r)),
        EsitoIntervento.NonValida n => Problemi.RichiestaNonValida(
            Problemi.Formatta(Messaggi.DatiInterventoNonValidi, string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + ".")),
        EsitoIntervento.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoIntervento.StatoNonConsentito s => Problemi.Conflitto(
            Problemi.Formatta(Messaggi.StatoInterventoNonConsentito, Testo("StatoIntervento_" + s.Stato))),
        EsitoIntervento.TecnicoOccupato => Problemi.Conflitto(Messaggi.TecnicoOccupato),
        EsitoIntervento.Vietata => Problemi.Vietato(Messaggi.InterventoVietato),
        _ => NonTrovato(),
    };

    private static RispostaScritturaIntervento Scrittura(EsitoIntervento.Riuscita r) => new(r.Id, r.Versione, r.Anno, r.Numero);

    private static IResult NonTrovato() =>
        Results.Problem(detail: Messaggi.InterventoNonTrovato, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(string nome) => Messaggi.ResourceManager.GetString(nome, Messaggi.Culture) ?? nome;
}

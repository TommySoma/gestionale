using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Fgas;
using Gestionale.Domain.Fgas;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

public sealed record RichiestaApparecchiatura(
    Guid? GasId,
    decimal? CaricaKg,
    decimal? CaricaInstallazioneKg,
    bool ErmeticaEtichettata,
    bool EdificioResidenziale,
    bool SistemaRilevamento,
    string? CodiceUnivoco,
    string? FatturaNumero,
    DateOnly? FatturaData,
    int? Versione);

public sealed record RichiestaOperazioneFgas(
    Guid? ApparecchiaturaId,
    Guid? InterventoId,
    string? Tipo,
    DateOnly? EseguitoIl,
    bool? PerditaRilevata,
    decimal? GasAggiuntoKg,
    decimal? GasRecuperatoKg,
    bool GasRiciclato,
    string? ImpiantoRiciclaggio,
    string? CertificatoRiciclaggio,
    string? MisureSmaltimento,
    Guid? TecnicoUtenteId,
    string? Note);

public sealed record RichiestaComunicazioneFgas(int? Versione, DateOnly? ComunicatoIl);

public sealed record RichiestaCertificazione(Guid? UtenteId, string? Numero, string? Ente, DateOnly? RilasciataIl, DateOnly? ScadeIl, int? Versione);

public sealed record RispostaScritturaFgas(Guid Id, int Versione);

/// <summary>
/// Registro F-Gas (ADR 0008): gas, apparecchiature dei generatori, operazioni del
/// registro, comunicazioni assistite alla Banca dati FGAS e certificati. Le
/// comunicazioni riguardano tutta l'azienda: il Tecnico non le vede.
/// </summary>
internal static class EndpointFgas
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var fgas = api.MapGroup("/fgas");
        fgas.MapGet("/gas", GasAsync).RichiedePermesso(CodiciPermesso.FgasLettura);
        fgas.MapPost("/operazioni", RegistraAsync).RichiedePermesso(CodiciPermesso.FgasScrittura);
        fgas.MapPost("/operazioni/{operazioneId:guid}/annullamento", AnnullaAsync).RichiedePermesso(CodiciPermesso.FgasScrittura);
        fgas.MapPost("/operazioni/{operazioneId:guid}/comunicazione", ComunicataAsync).RichiedePermesso(CodiciPermesso.FgasScrittura);
        fgas.MapGet("/comunicazioni", ComunicazioniAsync).RichiedePermesso(CodiciPermesso.FgasLettura);

        fgas.MapGet("/certificazioni", CertificazioniAsync).RichiedePermesso(CodiciPermesso.FgasLettura);
        fgas.MapPost("/certificazioni", (RichiestaCertificazione r, ClaimsPrincipal u, ServizioFgas s, CancellationToken ct) => SalvaCertificazioneAsync(null, r, u, s, ct))
            .RichiedePermesso(CodiciPermesso.AziendaScrittura);
        fgas.MapPut("/certificazioni/{certificazioneId:guid}", (Guid certificazioneId, RichiestaCertificazione r, ClaimsPrincipal u, ServizioFgas s, CancellationToken ct) =>
                SalvaCertificazioneAsync(certificazioneId, r, u, s, ct))
            .RichiedePermesso(CodiciPermesso.AziendaScrittura);
        fgas.MapDelete("/certificazioni/{certificazioneId:guid}", EliminaCertificazioneAsync).RichiedePermesso(CodiciPermesso.AziendaScrittura);

        var impianto = api.MapGroup("/impianti/{impiantoId:guid}");
        impianto.MapGet("/fgas", ApparecchiatureAsync).RichiedePermesso(CodiciPermesso.FgasLettura);
        impianto.MapPut("/generatori/{generatoreId:guid}/fgas", SalvaApparecchiaturaAsync).RichiedePermesso(CodiciPermesso.FgasScrittura);
        impianto.MapDelete("/generatori/{generatoreId:guid}/fgas", EliminaApparecchiaturaAsync).RichiedePermesso(CodiciPermesso.FgasScrittura);
    }

    private static async Task<IResult> GasAsync(ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Results.Ok(await servizio.GasAsync(Contesto(utente), ct));

    private static async Task<IResult> ApparecchiatureAsync(Guid impiantoId, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        await servizio.ApparecchiatureAsync(Contesto(utente), Ambito(utente, CodiciPermesso.FgasLettura), impiantoId, ct) is { } elenco
            ? Results.Ok(elenco)
            : NonTrovato();

    private static async Task<IResult> SalvaApparecchiaturaAsync(
        Guid impiantoId, Guid generatoreId, RichiestaApparecchiatura r, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Risposta(await servizio.SalvaApparecchiaturaAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.FgasScrittura), impiantoId, generatoreId, r.Versione,
            new DatiApparecchiatura(r.GasId, r.CaricaKg, r.CaricaInstallazioneKg, r.ErmeticaEtichettata, r.EdificioResidenziale,
                r.SistemaRilevamento, r.CodiceUnivoco, r.FatturaNumero, r.FatturaData), ct));

    // Togliere un'apparecchiatura spetta all'ufficio; con operazioni nel registro non si toglie.
    private static async Task<IResult> EliminaApparecchiaturaAsync(
        Guid impiantoId, Guid generatoreId, int? versione, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Ambito(utente, CodiciPermesso.FgasScrittura) != AmbitoPermesso.Tutti
            ? Problemi.Vietato(Messaggi.FgasSoloUfficio)
            : versione is not { } v
                ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
                : Risposta(await servizio.EliminaApparecchiaturaAsync(Contesto(utente), impiantoId, generatoreId, v, ct), eliminazione: true);

    private static async Task<IResult> RegistraAsync(RichiestaOperazioneFgas r, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Risposta(await servizio.RegistraOperazioneAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.FgasScrittura),
            new DatiOperazione(r.ApparecchiaturaId, r.InterventoId, r.Tipo, r.EseguitoIl, r.PerditaRilevata, r.GasAggiuntoKg, r.GasRecuperatoKg,
                r.GasRiciclato, r.ImpiantoRiciclaggio, r.CertificatoRiciclaggio, r.MisureSmaltimento, r.TecnicoUtenteId, r.Note), ct), creato: true);

    private static async Task<IResult> AnnullaAsync(
        Guid operazioneId, RichiestaAnnullamento r, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Ambito(utente, CodiciPermesso.FgasScrittura) != AmbitoPermesso.Tutti
            ? Problemi.Vietato(Messaggi.FgasSoloUfficio)
            : r.Versione is not { } versione
                ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
                : Risposta(await servizio.AnnullaOperazioneAsync(Contesto(utente), operazioneId, versione, r.Motivo, ct));

    private static async Task<IResult> ComunicataAsync(
        Guid operazioneId, RichiestaComunicazioneFgas r, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Ambito(utente, CodiciPermesso.FgasScrittura) != AmbitoPermesso.Tutti
            ? Problemi.Vietato(Messaggi.FgasSoloUfficio)
            : r.Versione is not { } versione
                ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
                : Risposta(await servizio.SegnaComunicataAsync(Contesto(utente), operazioneId, versione, r.ComunicatoIl, ct));

    private static async Task<IResult> ComunicazioniAsync(ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct, bool daComunicare = true) =>
        Ambito(utente, CodiciPermesso.FgasLettura) != AmbitoPermesso.Tutti
            ? Problemi.Vietato(Messaggi.FgasSoloUfficio)
            : Results.Ok(await servizio.ComunicazioniAsync(Contesto(utente), daComunicare, ct));

    private static async Task<IResult> CertificazioniAsync(ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        Results.Ok(await servizio.CertificazioniAsync(Contesto(utente), ct));

    private static async Task<IResult> SalvaCertificazioneAsync(
        Guid? certificazioneId, RichiestaCertificazione r, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        certificazioneId is not null && r.Versione is null
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.SalvaCertificazioneAsync(
                Contesto(utente), certificazioneId, r.Versione, new DatiCertificato(r.UtenteId, r.Numero, r.Ente, r.RilasciataIl, r.ScadeIl), ct),
                creato: certificazioneId is null);

    private static async Task<IResult> EliminaCertificazioneAsync(
        Guid certificazioneId, int? versione, ClaimsPrincipal utente, ServizioFgas servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaCertificazioneAsync(Contesto(utente), certificazioneId, v, ct), eliminazione: true);

    private static IResult Risposta(EsitoFgas esito, bool creato = false, bool eliminazione = false) => esito switch
    {
        EsitoFgas.Riuscita _ when eliminazione => Results.NoContent(),
        EsitoFgas.Riuscita r when creato => Results.Created((string?)null, new RispostaScritturaFgas(r.Id, r.Versione)),
        EsitoFgas.Riuscita r => Results.Ok(new RispostaScritturaFgas(r.Id, r.Versione)),
        EsitoFgas.NonValida n => Problemi.RichiestaNonValida(
            Problemi.Formatta(Messaggi.DatiFgasNonValidi, string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + ".")),
        EsitoFgas.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoFgas.StatoNonConsentito => Problemi.Conflitto(Messaggi.OperazioneFgasChiusa),
        _ => NonTrovato(),
    };

    private static IResult NonTrovato() =>
        Results.Problem(detail: Messaggi.FgasNonTrovato, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso Ambito(ClaimsPrincipal utente, string codice) => utente.AmbitoPermesso(codice) ?? AmbitoPermesso.Propri;

    private static string Testo(string codice) => Messaggi.ResourceManager.GetString(codice, Messaggi.Culture) ?? codice;
}

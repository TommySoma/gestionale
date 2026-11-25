using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Documenti;
using Gestionale.Domain.Documenti;
using Gestionale.Domain.Sicurezza;
using Microsoft.AspNetCore.Mvc;

namespace Gestionale.Api.Endpoint;

/// <summary>
/// Foto e documenti di un impianto. Il caricamento è multipart (campo <c>file</c>,
/// facoltativo <c>descrizione</c>); tipo e dimensione li controlla il server sul
/// contenuto. Lo scaricamento manda il file con il suo nome originale e senza
/// permettere al browser di indovinarne il tipo.
/// </summary>
internal static class EndpointDocumenti
{
    // Il limite del file più un margine per le intestazioni multipart.
    private const long LimiteRichiesta = RegoleDocumenti.DimensioneMassima + (256 * 1024);

    public static void Mappa(RouteGroupBuilder api)
    {
        var documenti = api.MapGroup("/impianti/{impiantoId:guid}/documenti");

        documenti.MapGet("/", ElencoAsync).RichiedePermesso(CodiciPermesso.DocumentiLettura);
        documenti.MapGet("/{documentoId:guid}/contenuto", ContenutoAsync).RichiedePermesso(CodiciPermesso.DocumentiLettura);
        documenti.MapPost("/", CaricaAsync)
            .RichiedePermesso(CodiciPermesso.DocumentiScrittura)
            // L'API usa token Bearer, non cookie: il controllo antiforgery dei form non serve.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(LimiteRichiesta), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = LimiteRichiesta });
        documenti.MapDelete("/{documentoId:guid}", EliminaAsync).RichiedePermesso(CodiciPermesso.DocumentiScrittura);
    }

    private static async Task<IResult> ElencoAsync(Guid impiantoId, ClaimsPrincipal utente, ServizioDocumenti servizio, CancellationToken ct) =>
        await servizio.ElencoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.DocumentiLettura), impiantoId, ct) is { } elenco
            ? Results.Ok(elenco)
            : NonTrovato();

    private static async Task<IResult> ContenutoAsync(
        Guid impiantoId, Guid documentoId, ClaimsPrincipal utente, ServizioDocumenti servizio, HttpContext http, CancellationToken ct)
    {
        if (await servizio.ContenutoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.DocumentiLettura), impiantoId, documentoId, ct) is not { } contenuto)
        {
            return NonTrovato();
        }

        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(contenuto.Flusso, contenuto.TipoMime, contenuto.NomeOriginale);
    }

    private static async Task<IResult> CaricaAsync(
        Guid impiantoId, IFormFile? file, [FromForm] string? descrizione, ClaimsPrincipal utente, ServizioDocumenti servizio, CancellationToken ct)
    {
        if (file is null)
        {
            return Problemi.RichiestaNonValida(Messaggi.FileMancante);
        }

        await using var flusso = file.OpenReadStream();
        return await servizio.CaricaAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, file.FileName, descrizione, flusso, ct) switch
        {
            EsitoDocumento.Caricato c => Results.Created($"/api/impianti/{impiantoId}/documenti/{c.Documento.DocumentoId}/contenuto", c.Documento),
            EsitoDocumento.NonValido n => Problemi.RichiestaNonValida(Testo(n.Errore.ToString())),
            _ => NonTrovato(),
        };
    }

    private static async Task<IResult> EliminaAsync(
        Guid impiantoId, Guid documentoId, int? versione, ClaimsPrincipal utente, ServizioDocumenti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : await servizio.EliminaAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, documentoId, v, ct) switch
            {
                EsitoDocumento.Eliminato => Results.NoContent(),
                EsitoDocumento.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
                EsitoDocumento.Vietato => Problemi.Vietato(Messaggi.EliminazioneDocumentoVietata),
                _ => NonTrovato(),
            };

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    private static AmbitoPermesso Ambito(ClaimsPrincipal utente, string codice) =>
        utente.AmbitoPermesso(codice) ?? AmbitoPermesso.Propri;

    // Chi scrive deve anche poter leggere: vale l'ambito più stretto dei due.
    private static AmbitoPermesso AmbitoScrittura(ClaimsPrincipal utente) =>
        Ambito(utente, CodiciPermesso.DocumentiScrittura) == AmbitoPermesso.Tutti && Ambito(utente, CodiciPermesso.DocumentiLettura) == AmbitoPermesso.Tutti
            ? AmbitoPermesso.Tutti
            : AmbitoPermesso.Propri;

    private static IResult NonTrovato() =>
        Results.Problem(detail: Messaggi.DocumentoNonTrovato, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    private static string Testo(string nome) => Messaggi.ResourceManager.GetString("Documento" + nome, Messaggi.Culture) ?? nome;
}

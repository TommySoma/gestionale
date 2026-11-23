using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

public sealed record RichiestaImpianto(
    Guid? SedeId,
    Guid? TipoImpiantoId,
    string? Stato,
    int? AnnoInstallazione,
    decimal? PotenzaUtileKw,
    Guid? CombustibileId,
    string? Pdr,
    Guid? ProprietarioClienteId,
    Guid? ResponsabileClienteId,
    string? Note,
    DateOnly? DismessoIl,
    int? Versione);

public sealed record RichiestaGeneratore(
    Guid? TipoGeneratoreId,
    string? Marca,
    string? Modello,
    string? Matricola,
    decimal? PotenzaKw,
    Guid? CombustibileId,
    DateOnly? InstallatoIl,
    DateOnly? DismessoIl,
    string? Note,
    int? Versione);

public sealed record RichiestaRiferimentoCatasto(string? Regione, string? Codice, DateOnly? RegistratoIl, int? Versione);

/// <summary>Esito di una scrittura con gli avvisi che non la bloccano (doppioni sospetti).</summary>
public sealed record RispostaScritturaImpianto(Guid Id, int Versione, IReadOnlyList<string> Avvisi);

/// <summary>
/// Impianti con generatori e riferimenti ai catasti regionali. Come per le
/// anagrafiche, ogni modifica richiede la versione letta. I generatori non si
/// cancellano: si dismettono con la data.
/// </summary>
internal static class EndpointImpianti
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var impianti = api.MapGroup("/impianti");

        impianti.MapGet("/", CercaAsync).RichiedePermesso(CodiciPermesso.ImpiantiLettura);
        impianti.MapGet("/voci", VociAsync).RichiedePermesso(CodiciPermesso.ImpiantiLettura);
        impianti.MapGet("/{impiantoId:guid}", ImpiantoAsync).RichiedePermesso(CodiciPermesso.ImpiantiLettura);
        impianti.MapPost("/", CreaAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
        impianti.MapPut("/{impiantoId:guid}", AggiornaAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
        impianti.MapDelete("/{impiantoId:guid}", EliminaAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);

        impianti.MapPost("/{impiantoId:guid}/generatori", CreaGeneratoreAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
        impianti.MapPut("/{impiantoId:guid}/generatori/{generatoreId:guid}", AggiornaGeneratoreAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);

        impianti.MapPost("/{impiantoId:guid}/riferimenti-catasto", CreaRiferimentoAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
        impianti.MapPut("/{impiantoId:guid}/riferimenti-catasto/{riferimentoId:guid}", AggiornaRiferimentoAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
        impianti.MapDelete("/{impiantoId:guid}/riferimenti-catasto/{riferimentoId:guid}", EliminaRiferimentoAsync).RichiedePermesso(CodiciPermesso.ImpiantiScrittura);
    }

    private static async Task<IResult> CercaAsync(
        ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct,
        string? cerca = null, Guid? cliente = null, string? stato = null, int pagina = 1, int dimensione = 25) =>
        Results.Ok(await servizio.CercaAsync(
            Contesto(utente), Ambito(utente, CodiciPermesso.ImpiantiLettura), new FiltroImpianti(cerca, cliente, stato), pagina, dimensione, ct));

    private static async Task<IResult> VociAsync(ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        Results.Ok(await servizio.VociAsync(Contesto(utente), ct));

    private static async Task<IResult> ImpiantoAsync(Guid impiantoId, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        await servizio.ImpiantoAsync(Contesto(utente), Ambito(utente, CodiciPermesso.ImpiantiLettura), impiantoId, ct) is { } impianto
            ? Results.Ok(impianto)
            : NonTrovato();

    private static async Task<IResult> CreaAsync(RichiestaImpianto richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        Risposta(await servizio.CreaAsync(Contesto(utente), AmbitoScrittura(utente), Dati(richiesta), ct), creato: id => $"/api/impianti/{id}");

    private static async Task<IResult> AggiornaAsync(
        Guid impiantoId, RichiestaImpianto richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, versione, Dati(richiesta), ct));

    private static async Task<IResult> EliminaAsync(Guid impiantoId, int? versione, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, v, ct), eliminazione: true);

    private static async Task<IResult> CreaGeneratoreAsync(
        Guid impiantoId, RichiestaGeneratore richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        Risposta(
            await servizio.CreaGeneratoreAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, Dati(richiesta), ct),
            creato: id => $"/api/impianti/{impiantoId}/generatori/{id}");

    private static async Task<IResult> AggiornaGeneratoreAsync(
        Guid impiantoId, Guid generatoreId, RichiestaGeneratore richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaGeneratoreAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, generatoreId, versione, Dati(richiesta), ct));

    private static async Task<IResult> CreaRiferimentoAsync(
        Guid impiantoId, RichiestaRiferimentoCatasto richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        Risposta(
            await servizio.CreaRiferimentoAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, Dati(richiesta), ct),
            creato: id => $"/api/impianti/{impiantoId}/riferimenti-catasto/{id}");

    private static async Task<IResult> AggiornaRiferimentoAsync(
        Guid impiantoId, Guid riferimentoId, RichiestaRiferimentoCatasto richiesta, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaRiferimentoAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, riferimentoId, versione, Dati(richiesta), ct));

    private static async Task<IResult> EliminaRiferimentoAsync(
        Guid impiantoId, Guid riferimentoId, int? versione, ClaimsPrincipal utente, ServizioImpianti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaRiferimentoAsync(Contesto(utente), AmbitoScrittura(utente), impiantoId, riferimentoId, v, ct), eliminazione: true);

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso Ambito(ClaimsPrincipal utente, string codice) =>
        utente.AmbitoPermesso(codice) ?? AmbitoPermesso.Propri;

    // Chi scrive deve anche poter leggere: vale l'ambito più stretto dei due.
    private static AmbitoPermesso AmbitoScrittura(ClaimsPrincipal utente) =>
        Ambito(utente, CodiciPermesso.ImpiantiScrittura) == AmbitoPermesso.Tutti && Ambito(utente, CodiciPermesso.ImpiantiLettura) == AmbitoPermesso.Tutti
            ? AmbitoPermesso.Tutti
            : AmbitoPermesso.Propri;

    private static DatiImpianto Dati(RichiestaImpianto r) =>
        new(r.SedeId, r.TipoImpiantoId, r.Stato, r.AnnoInstallazione, r.PotenzaUtileKw, r.CombustibileId, r.Pdr,
            r.ProprietarioClienteId, r.ResponsabileClienteId, r.Note, r.DismessoIl);

    private static DatiGeneratore Dati(RichiestaGeneratore r) =>
        new(r.TipoGeneratoreId, r.Marca, r.Modello, r.Matricola, r.PotenzaKw, r.CombustibileId, r.InstallatoIl, r.DismessoIl, r.Note);

    private static DatiRiferimentoCatasto Dati(RichiestaRiferimentoCatasto r) => new(r.Regione, null, r.Codice, r.RegistratoIl);

    private static IResult Risposta(EsitoImpianto esito, Func<Guid, string>? creato = null, bool eliminazione = false) => esito switch
    {
        EsitoImpianto.Riuscita _ when eliminazione => Results.NoContent(),
        EsitoImpianto.Riuscita r when creato is not null => Results.Created(creato(r.Id), Scrittura(r)),
        EsitoImpianto.Riuscita r => Results.Ok(Scrittura(r)),
        EsitoImpianto.NonValida n => Problemi.RichiestaNonValida(
            Problemi.Formatta(Messaggi.DatiAnagraficiNonValidi, string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + ".")),
        EsitoImpianto.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoImpianto.Duplicata d => Problemi.Conflitto(Testo(d.Motivo.ToString())),
        EsitoImpianto.Vietata => Problemi.Vietato(Messaggi.CreazioneImpiantoVietata),
        _ => NonTrovato(),
    };

    private static RispostaScritturaImpianto Scrittura(EsitoImpianto.Riuscita r) =>
        new(r.Id, r.Versione, [.. r.Avvisi.Select(a => Testo("Avviso" + a))]);

    private static IResult NonTrovato() =>
        Results.Problem(detail: Messaggi.ImpiantoNonTrovato, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore e avviso ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(string nome) => Messaggi.ResourceManager.GetString(nome, Messaggi.Culture) ?? nome;
}

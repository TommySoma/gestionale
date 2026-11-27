using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Anagrafica;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

public sealed record RichiestaCliente(
    string Tipo,
    string? Denominazione,
    string? Nome,
    string? Cognome,
    string? CodiceFiscale,
    string? PartitaIva,
    string? Pec,
    string? Note,
    int? Versione);

public sealed record RichiestaSede(
    string? Descrizione,
    string Indirizzo,
    string? Civico,
    string Cap,
    string Comune,
    string Provincia,
    string? NoteAccesso,
    decimal? Latitudine,
    decimal? Longitudine,
    int? Versione,
    string? CodiceIstatComune = null);

public sealed record RichiestaAzienda(
    string RagioneSociale,
    string? PartitaIva,
    string? CodiceFiscale,
    string? Indirizzo,
    string? Cap,
    string? Comune,
    string? Provincia,
    int? CodiceDittaSiert,
    int? Versione);

public sealed record RichiestaContatto(string Tipo, string Valore, string? Etichetta, bool Principale, int? Versione);

public sealed record RichiestaConsenso(string Finalita, bool Concesso, string Fonte, string? Note);

public sealed record RispostaScrittura(Guid Id, int Versione);

/// <summary>
/// Anagrafiche: clienti con sedi, contatti e consensi privacy. Ogni modifica
/// richiede la versione letta, per non sovrascrivere il lavoro di un collega.
/// </summary>
internal static class EndpointClienti
{
    public static void Mappa(RouteGroupBuilder api)
    {
        var clienti = api.MapGroup("/clienti");

        clienti.MapGet("/", CercaAsync).RichiedePermesso(CodiciPermesso.ClientiLettura);
        clienti.MapGet("/{clienteId:guid}", ClienteAsync).RichiedePermesso(CodiciPermesso.ClientiLettura);
        clienti.MapPost("/", CreaAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapPut("/{clienteId:guid}", AggiornaAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapDelete("/{clienteId:guid}", EliminaAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapPost("/{clienteId:guid}/anonimizzazione", AnonimizzaAsync).RichiedePermesso(CodiciPermesso.ClientiAnonimizzazione);

        clienti.MapPost("/{clienteId:guid}/sedi", CreaSedeAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapPut("/{clienteId:guid}/sedi/{sedeId:guid}", AggiornaSedeAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapDelete("/{clienteId:guid}/sedi/{sedeId:guid}", EliminaSedeAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);

        clienti.MapPost("/{clienteId:guid}/contatti", CreaContattoAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapPut("/{clienteId:guid}/contatti/{contattoId:guid}", AggiornaContattoAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);
        clienti.MapDelete("/{clienteId:guid}/contatti/{contattoId:guid}", EliminaContattoAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);

        clienti.MapGet("/{clienteId:guid}/consensi", StoricoConsensiAsync).RichiedePermesso(CodiciPermesso.ClientiLettura);
        clienti.MapPost("/{clienteId:guid}/consensi", RegistraConsensoAsync).RichiedePermesso(CodiciPermesso.ClientiScrittura);

        api.MapGet("/finalita-privacy", FinalitaAsync).RichiedePermesso(CodiciPermesso.ClientiLettura);

        // Dati dell'impresa corrente: il codice ditta serve al file di import SIERT.
        api.MapGet("/azienda", AziendaAsync).RichiedePermesso(CodiciPermesso.AziendaLettura);
        api.MapPut("/azienda", AggiornaAziendaAsync).RichiedePermesso(CodiciPermesso.AziendaScrittura);
    }

    private static async Task<IResult> CercaAsync(
        ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct,
        string? cerca = null, string? tipo = null, int pagina = 1, int dimensione = 25) =>
        Results.Ok(await servizio.CercaAsync(Contesto(utente), AmbitoLettura(utente), cerca, tipo, pagina, dimensione, ct));

    private static async Task<IResult> ClienteAsync(Guid clienteId, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        await servizio.ClienteAsync(Contesto(utente), AmbitoLettura(utente), clienteId, ct) is { } cliente
            ? Results.Ok(cliente)
            : NonTrovato();

    private static async Task<IResult> CreaAsync(RichiestaCliente richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Risposta(await servizio.CreaAsync(Contesto(utente), Dati(richiesta), ct), creato: id => $"/api/clienti/{id}");

    private static async Task<IResult> AggiornaAsync(
        Guid clienteId, RichiestaCliente richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAsync(Contesto(utente), clienteId, versione, Dati(richiesta), ct));

    private static async Task<IResult> EliminaAsync(Guid clienteId, int? versione, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaAsync(Contesto(utente), clienteId, v, ct), eliminazione: true);

    private static async Task<IResult> AnonimizzaAsync(Guid clienteId, int? versione, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AnonimizzaAsync(Contesto(utente), clienteId, v, ct), eliminazione: true);

    private static async Task<IResult> CreaSedeAsync(
        Guid clienteId, RichiestaSede richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Risposta(await servizio.CreaSedeAsync(Contesto(utente), clienteId, Dati(richiesta), ct), creato: id => $"/api/clienti/{clienteId}/sedi/{id}");

    private static async Task<IResult> AggiornaSedeAsync(
        Guid clienteId, Guid sedeId, RichiestaSede richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaSedeAsync(Contesto(utente), clienteId, sedeId, versione, Dati(richiesta), ct));

    private static async Task<IResult> EliminaSedeAsync(
        Guid clienteId, Guid sedeId, int? versione, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaSedeAsync(Contesto(utente), clienteId, sedeId, v, ct), eliminazione: true);

    private static async Task<IResult> CreaContattoAsync(
        Guid clienteId, RichiestaContatto richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Risposta(await servizio.CreaContattoAsync(Contesto(utente), clienteId, Dati(richiesta), ct), creato: id => $"/api/clienti/{clienteId}/contatti/{id}");

    private static async Task<IResult> AggiornaContattoAsync(
        Guid clienteId, Guid contattoId, RichiestaContatto richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaContattoAsync(Contesto(utente), clienteId, contattoId, versione, Dati(richiesta), ct));

    private static async Task<IResult> EliminaContattoAsync(
        Guid clienteId, Guid contattoId, int? versione, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        versione is not { } v
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.EliminaContattoAsync(Contesto(utente), clienteId, contattoId, v, ct), eliminazione: true);

    private static async Task<IResult> StoricoConsensiAsync(Guid clienteId, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        await servizio.StoricoConsensiAsync(Contesto(utente), AmbitoLettura(utente), clienteId, ct) is { } storico
            ? Results.Ok(storico)
            : NonTrovato();

    private static async Task<IResult> RegistraConsensoAsync(
        Guid clienteId, RichiestaConsenso richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Risposta(
            await servizio.RegistraConsensoAsync(Contesto(utente), clienteId, new DatiConsenso(richiesta.Finalita, richiesta.Concesso, richiesta.Fonte, richiesta.Note), ct),
            creato: _ => $"/api/clienti/{clienteId}/consensi");

    private static async Task<IResult> FinalitaAsync(ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Results.Ok(await servizio.FinalitaAsync(Contesto(utente), ct));

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    // La policy garantisce che il permesso ci sia: qui serve solo sapere se vale su tutto o sui propri.
    private static AmbitoPermesso AmbitoLettura(ClaimsPrincipal utente) =>
        utente.AmbitoPermesso(CodiciPermesso.ClientiLettura) ?? AmbitoPermesso.Propri;

    private static DatiCliente Dati(RichiestaCliente r) =>
        new(r.Tipo, r.Denominazione, r.Nome, r.Cognome, r.CodiceFiscale, r.PartitaIva, r.Pec, r.Note);

    private static DatiSede Dati(RichiestaSede r) =>
        new(r.Descrizione, r.Indirizzo, r.Civico, r.Cap, r.Comune, r.Provincia, r.NoteAccesso, r.Latitudine, r.Longitudine, r.CodiceIstatComune);

    private static async Task<IResult> AziendaAsync(ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        Results.Ok(await servizio.AziendaAsync(Contesto(utente), ct));

    private static async Task<IResult> AggiornaAziendaAsync(
        RichiestaAzienda richiesta, ClaimsPrincipal utente, ServizioClienti servizio, CancellationToken ct) =>
        richiesta.Versione is not { } versione
            ? Problemi.RichiestaNonValida(Messaggi.VersioneMancante)
            : Risposta(await servizio.AggiornaAziendaAsync(Contesto(utente), versione,
                new DatiAzienda(richiesta.RagioneSociale, richiesta.PartitaIva, richiesta.CodiceFiscale, richiesta.Indirizzo,
                    richiesta.Cap, richiesta.Comune, richiesta.Provincia, richiesta.CodiceDittaSiert), ct));

    private static DatiContatto Dati(RichiestaContatto r) => new(r.Tipo, r.Valore, r.Etichetta, r.Principale);

    private static IResult Risposta(EsitoScrittura esito, Func<Guid, string>? creato = null, bool eliminazione = false) => esito switch
    {
        EsitoScrittura.Riuscita _ when eliminazione => Results.NoContent(),
        EsitoScrittura.Riuscita r when creato is not null => Results.Created(creato(r.Id), new RispostaScrittura(r.Id, r.Versione)),
        EsitoScrittura.Riuscita r => Results.Ok(new RispostaScrittura(r.Id, r.Versione)),
        EsitoScrittura.NonValida n => Problemi.RichiestaNonValida(
            Problemi.Formatta(Messaggi.DatiAnagraficiNonValidi, string.Join("; ", n.Errori.Select(Testo)) + ".")),
        EsitoScrittura.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoScrittura.Duplicata d => Problemi.Conflitto(Testo(d.Motivo)),
        _ => NonTrovato(),
    };

    private static IResult NonTrovato() =>
        Results.Problem(detail: Messaggi.ClienteNonTrovato, statusCode: StatusCodes.Status404NotFound, title: Messaggi.TitoloNonTrovato);

    // Ogni errore ha il suo testo nel file di traduzione, con lo stesso nome.
    private static string Testo(ErroreAnagrafica errore) =>
        Messaggi.ResourceManager.GetString(errore.ToString(), Messaggi.Culture) ?? errore.ToString();
}

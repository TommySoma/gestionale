using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Promemoria;
using Gestionale.Domain.Promemoria;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>Password vuota o assente: resta quella salvata.</summary>
public sealed record RichiestaPosta(string? Host, int? Porta, string? Sicurezza, string? Utente, string? Password, string? MittenteEmail, string? MittenteNome, int? Versione);

public sealed record RichiestaPromemoria(IReadOnlyList<Guid>? Scadenze, string? Nota);

public sealed record RispostaPromemoria(
    Guid ScadenzaId, string Cliente, string? Email, string? Esclusione, string? Motivo, string? Oggetto, string? Testo, bool Inviato, string? Errore);

/// <summary>
/// Promemoria email delle scadenze (ADR 0007). Il server di posta lo configura il
/// Titolare nei dati aziendali; l'invio parte a mano da chi gestisce le scadenze,
/// dopo l'anteprima, e solo verso i clienti con email e consenso ai promemoria.
/// </summary>
internal static class EndpointPromemoria
{
    public static void Mappa(RouteGroupBuilder api)
    {
        api.MapGet("/azienda/posta", ParametriAsync).RichiedePermesso(CodiciPermesso.AziendaLettura);
        api.MapPut("/azienda/posta", SalvaAsync).RichiedePermesso(CodiciPermesso.AziendaScrittura);
        api.MapPost("/azienda/posta/prova", ProvaAsync).RichiedePermesso(CodiciPermesso.AziendaScrittura);

        api.MapPost("/promemoria/anteprima", (RichiestaPromemoria r, ClaimsPrincipal u, ServizioPromemoria s, CancellationToken ct) => PromemoriaAsync(r, u, s, false, ct))
            .RichiedePermesso(CodiciPermesso.ScadenzeScrittura);
        api.MapPost("/promemoria", (RichiestaPromemoria r, ClaimsPrincipal u, ServizioPromemoria s, CancellationToken ct) => PromemoriaAsync(r, u, s, true, ct))
            .RichiedePermesso(CodiciPermesso.ScadenzeScrittura);
    }

    private static async Task<IResult> ParametriAsync(ClaimsPrincipal utente, ServizioPromemoria servizio, CancellationToken ct) =>
        await servizio.ParametriAsync(Contesto(utente), ct) is { } parametri ? Results.Ok(parametri) : Results.NoContent();

    private static async Task<IResult> SalvaAsync(RichiestaPosta r, ClaimsPrincipal utente, ServizioPromemoria servizio, CancellationToken ct) =>
        Risposta(await servizio.SalvaParametriAsync(
            Contesto(utente), r.Versione, new DatiPosta(r.Host, r.Porta, r.Sicurezza, r.Utente, r.Password, r.MittenteEmail, r.MittenteNome), ct));

    // La prova arriva a chi configura il server: così si vede subito se la posta parte.
    private static async Task<IResult> ProvaAsync(ClaimsPrincipal utente, ServizioPromemoria servizio, CancellationToken ct) =>
        Risposta(await servizio.ProvaAsync(Contesto(utente), ct));

    private static async Task<IResult> PromemoriaAsync(RichiestaPromemoria r, ClaimsPrincipal utente, ServizioPromemoria servizio, bool invia, CancellationToken ct)
    {
        // Il promemoria va al cliente: decide chi gestisce le scadenze di tutta l'azienda.
        if (utente.AmbitoPermesso(CodiciPermesso.ScadenzeScrittura) != AmbitoPermesso.Tutti)
        {
            return Problemi.Vietato(Messaggi.PromemoriaVietati);
        }

        var esito = invia
            ? await servizio.InviaAsync(Contesto(utente), r.Scadenze, r.Nota, ct)
            : await servizio.AnteprimaAsync(Contesto(utente), r.Scadenze, r.Nota, ct);
        return Risposta(esito);
    }

    private static IResult Risposta(EsitoPromemoria esito) => esito switch
    {
        EsitoPromemoria.Riuscito r => Results.Ok(r.Promemoria.Select(p => new RispostaPromemoria(
            p.ScadenzaId, p.Cliente, p.Email, p.Esclusione?.ToString(), p.Esclusione is { } e ? Testo(e.ToString()) : null,
            p.Oggetto, p.Testo, p.Inviato, p.Errore)).ToList()),
        EsitoPromemoria.Salvato s => Results.Ok(new { versione = s.Versione }),
        EsitoPromemoria.NonValido n => Problemi.RichiestaNonValida(string.Join("; ", n.Errori.Select(e => Testo(e.ToString()))) + "."),
        EsitoPromemoria.VersioneSuperata => Problemi.Conflitto(Messaggi.VersioneSuperata),
        EsitoPromemoria.InvioFallito f => Problemi.Conflitto(Problemi.Formatta(Messaggi.ProvaPostaFallita, f.Messaggio)),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static Contesto Contesto(ClaimsPrincipal utente) => new(utente.AziendaId(), utente.UtenteId());

    private static string Testo(string codice) => Messaggi.ResourceManager.GetString(codice, Messaggi.Culture) ?? codice;
}

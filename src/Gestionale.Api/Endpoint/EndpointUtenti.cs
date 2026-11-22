using System.Security.Claims;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>Gestione degli utenti dell'azienda: elenco e creazione.</summary>
internal static class EndpointUtenti
{
    public static void Mappa(RouteGroupBuilder api)
    {
        api.MapGet("/utenti", ElencoAsync).RichiedePermesso(CodiciPermesso.UtentiLettura);
        api.MapPost("/utenti", CreaAsync).RichiedePermesso(CodiciPermesso.UtentiScrittura);
    }

    private static async Task<IResult> ElencoAsync(ClaimsPrincipal utente, ServizioAutenticazione servizio, CancellationToken ct)
    {
        var chiamante = await servizio.ProfiloAsync(utente.AziendaId(), utente.UtenteId(), ct);
        var utenti = await servizio.ElencoUtentiAsync(chiamante, ct);

        return Results.Ok(utenti.Select(u => new RispostaUtente(u.UtenteId, u.Email, u.Nome, u.Cognome, u.Stato, u.MfaAttivo, u.Ruoli)));
    }

    private static async Task<IResult> CreaAsync(RichiestaNuovoUtente richiesta, ClaimsPrincipal utente, ServizioAutenticazione servizio, CancellationToken ct)
    {
        var mancanti = Problemi.CampiMancanti(
            ("email", richiesta.Email),
            ("nome", richiesta.Nome),
            ("cognome", richiesta.Cognome),
            ("password", richiesta.Password));
        if (mancanti is not null)
        {
            return mancanti;
        }

        if (Problemi.EmailNonValida(richiesta.Email) is { } emailNonValida)
        {
            return emailNonValida;
        }

        // I ruoli si leggono dal database, non dal token: potrebbero essere cambiati.
        var chiamante = await servizio.ProfiloAsync(utente.AziendaId(), utente.UtenteId(), ct);
        var nuovo = new NuovoUtente(richiesta.Email.Trim(), richiesta.Nome.Trim(), richiesta.Cognome.Trim(), [.. (richiesta.Ruoli ?? []).Distinct()]);

        return await servizio.CreaUtenteAsync(chiamante, nuovo, richiesta.Password, ct) switch
        {
            EsitoCreazioneUtente.Creato c => Results.Created($"/api/utenti/{c.UtenteId}", new { utenteId = c.UtenteId }),
            EsitoCreazioneUtente.PasswordNonValida p => Problemi.PasswordNonValida(nuovo.Email, p.Errori),
            EsitoCreazioneUtente.RuoloSconosciuto r => Problemi.RichiestaNonValida(Problemi.Formatta(Messaggi.RuoloSconosciuto, r.Ruolo)),
            EsitoCreazioneUtente.RuoloNonAssegnabile r => Problemi.Vietato(Problemi.Formatta(Messaggi.RuoloNonAssegnabile, r.Ruolo)),
            EsitoCreazioneUtente.SenzaRuoli => Problemi.RichiestaNonValida(Messaggi.SenzaRuoli),
            _ => Problemi.Conflitto(Messaggi.EmailInUso),
        };
    }
}

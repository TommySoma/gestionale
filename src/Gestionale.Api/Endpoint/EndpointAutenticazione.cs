using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Anagrafica;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>
/// Primo avvio, accesso con secondo fattore, rinnovo e uscita. Il flusso:
/// <list type="number">
/// <item>POST /api/accesso con email e password;</item>
/// <item>se l'esito è <c>mfa_richiesto</c>, POST /api/accesso/mfa con il token di passaggio e il codice;</item>
/// <item>se è <c>mfa_da_configurare</c>, POST /api/mfa/configurazione e poi /api/mfa/attivazione;</item>
/// <item>prima che scada il token di accesso, POST /api/sessione/rinnovo.</item>
/// </list>
/// </summary>
internal static class EndpointAutenticazione
{
    private const int LunghezzaMassimaDispositivo = 200;
    private static readonly string[] s_regioni = ["toscana", "lombardia"];

    public static void Mappa(RouteGroupBuilder api)
    {
        api.MapGet("/installazione", StatoInstallazioneAsync).AllowAnonymous();
        api.MapPost("/installazione", InstallaAsync).AllowAnonymous().RequireRateLimiting(LimitiRichieste.Accesso);

        api.MapPost("/accesso", AccediAsync).AllowAnonymous().RequireRateLimiting(LimitiRichieste.Accesso);
        api.MapPost("/accesso/mfa", VerificaMfaAsync)
            .RequireAuthorization(PolicyAutenticazione.VerificaMfa)
            .RequireRateLimiting(LimitiRichieste.Accesso);

        api.MapPost("/mfa/configurazione", ConfiguraMfaAsync).RequireAuthorization(PolicyAutenticazione.ConfigurazioneMfa);
        api.MapPost("/mfa/attivazione", AttivaMfaAsync)
            .RequireAuthorization(PolicyAutenticazione.ConfigurazioneMfa)
            .RequireRateLimiting(LimitiRichieste.Accesso);

        api.MapPost("/sessione/rinnovo", RinnovaAsync).AllowAnonymous().RequireRateLimiting(LimitiRichieste.Accesso);
        api.MapPost("/sessione/uscita", EsciAsync);

        api.MapGet("/io", ProfiloAsync);
    }

    private static async Task<IResult> StatoInstallazioneAsync(ServizioAutenticazione servizio, CancellationToken ct) =>
        Results.Ok(new RispostaStatoInstallazione(await servizio.InstallazioneDaInizializzareAsync(ct)));

    private static async Task<IResult> InstallaAsync(
        RichiestaInstallazione richiesta,
        HttpRequest http,
        ServizioAutenticazione servizio,
        OpzioniAutenticazione opzioni,
        CancellationToken ct)
    {
        if (!TokenInstallazioneValido(http.Headers["X-Token-Installazione"], opzioni.TokenInstallazione))
        {
            return Problemi.Vietato(Messaggi.TokenInstallazioneNonValido);
        }

        var titolare = richiesta.Titolare;
        var mancanti = Problemi.CampiMancanti(
            ("ragioneSociale", richiesta.RagioneSociale),
            ("regione", richiesta.Regione),
            ("titolare.email", titolare?.Email),
            ("titolare.nome", titolare?.Nome),
            ("titolare.cognome", titolare?.Cognome),
            ("titolare.password", titolare?.Password));
        if (mancanti is not null)
        {
            return mancanti;
        }

        var amministratore = richiesta.Amministratore;
        if (amministratore is not null)
        {
            mancanti = Problemi.CampiMancanti(
                ("amministratore.email", amministratore.Email),
                ("amministratore.nome", amministratore.Nome),
                ("amministratore.cognome", amministratore.Cognome),
                ("amministratore.password", amministratore.Password));
            if (mancanti is not null)
            {
                return mancanti;
            }
        }

        var emailNonValida = Problemi.EmailNonValida(titolare!.Email)
            ?? (amministratore is null ? null : Problemi.EmailNonValida(amministratore.Email));
        if (emailNonValida is not null)
        {
            return emailNonValida;
        }

        var regione = richiesta.Regione.Trim().ToLowerInvariant();
        if (!s_regioni.Contains(regione))
        {
            return Problemi.RichiestaNonValida(Problemi.Formatta(Messaggi.RegioneNonGestita, richiesta.Regione));
        }

        var partitaIva = string.IsNullOrWhiteSpace(richiesta.PartitaIva) ? null : richiesta.PartitaIva.Trim();
        if (partitaIva is not null && !PartitaIva.Valida(partitaIva))
        {
            return Problemi.RichiestaNonValida(Problemi.Formatta(Messaggi.DatiAnagraficiNonValidi, Messaggi.PartitaIvaNonValida + "."));
        }

        var dati = new DatiInstallazione(
            richiesta.RagioneSociale.Trim(), partitaIva, regione,
            titolare.Email.Trim(), titolare.Nome.Trim(), titolare.Cognome.Trim(), "",
            amministratore?.Email.Trim(), amministratore?.Nome.Trim(), amministratore?.Cognome.Trim(), null);

        return await servizio.InstallaAsync(dati, titolare.Password, amministratore?.Password, ct) switch
        {
            EsitoInstallazione.Completata c => Results.Created("/api/io", new RispostaInstallazione(c.AziendaId)),
            EsitoInstallazione.PasswordNonValida p => Problemi.PasswordNonValida(p.Email, p.Errori),
            _ => Problemi.Conflitto(Messaggi.InstallazioneGiaInizializzata),
        };
    }

    private static async Task<IResult> AccediAsync(RichiestaAccesso richiesta, HttpContext http, ServizioAutenticazione servizio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(richiesta.Email) || string.IsNullOrEmpty(richiesta.Password))
        {
            return Problemi.AccessoNegato(Messaggi.CredenzialiNonValide);
        }

        var esito = await servizio.AccediAsync(
            richiesta.Email, richiesta.Password, Dispositivo(richiesta.Dispositivo), http.Connection.RemoteIpAddress, ct);

        return esito switch
        {
            EsitoAccesso.Completato c => Results.Ok(new RispostaAccesso("completato", RispostaToken.Da(c.Token), null)),
            EsitoAccesso.MfaRichiesto m => Results.Ok(new RispostaAccesso("mfa_richiesto", null, m.TokenPassaggio)),
            EsitoAccesso.MfaDaConfigurare m => Results.Ok(new RispostaAccesso("mfa_da_configurare", null, m.TokenPassaggio)),
            _ => Problemi.AccessoNegato(Messaggi.CredenzialiNonValide),
        };
    }

    private static async Task<IResult> VerificaMfaAsync(
        RichiestaCodice richiesta, ClaimsPrincipal utente, HttpContext http, ServizioAutenticazione servizio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(richiesta.Codice))
        {
            return Problemi.AccessoNegato(Messaggi.CodiceMfaNonValido);
        }

        var esito = await servizio.VerificaMfaAsync(
            utente.AziendaId(), utente.UtenteId(), richiesta.Codice, Dispositivo(richiesta.Dispositivo), http.Connection.RemoteIpAddress, ct);

        return esito is EsitoAccesso.Completato c
            ? Results.Ok(new RispostaAccesso("completato", RispostaToken.Da(c.Token), null))
            : Problemi.AccessoNegato(Messaggi.CodiceMfaNonValido);
    }

    private static async Task<IResult> ConfiguraMfaAsync(ClaimsPrincipal utente, ServizioAutenticazione servizio, CancellationToken ct)
    {
        var configurazione = await servizio.ConfiguraMfaAsync(utente.AziendaId(), utente.UtenteId(), ct);

        return configurazione is null
            ? Problemi.Conflitto(Messaggi.MfaGiaAttivo)
            : Results.Ok(new RispostaConfigurazioneMfa(configurazione.SegretoBase32, configurazione.UriConfigurazione));
    }

    private static async Task<IResult> AttivaMfaAsync(
        RichiestaCodice richiesta, ClaimsPrincipal utente, HttpContext http, ServizioAutenticazione servizio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(richiesta.Codice))
        {
            return Problemi.RichiestaNonValida(Messaggi.CodiceAttivazioneNonValido);
        }

        // Con il token di passaggio l'attivazione chiude anche il login.
        var daPassaggio = utente.Scopo() == ScopiPassaggio.Configurazione;
        var attivazione = await servizio.AttivaMfaAsync(
            utente.AziendaId(), utente.UtenteId(), richiesta.Codice, daPassaggio,
            Dispositivo(richiesta.Dispositivo), http.Connection.RemoteIpAddress, ct);

        return attivazione is null
            ? Problemi.RichiestaNonValida(Messaggi.CodiceAttivazioneNonValido)
            : Results.Ok(new RispostaAttivazioneMfa(
                attivazione.CodiciRecupero, attivazione.Token is null ? null : RispostaToken.Da(attivazione.Token)));
    }

    private static async Task<IResult> RinnovaAsync(RichiestaRinnovo richiesta, HttpContext http, ServizioAutenticazione servizio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(richiesta.TokenRinnovo))
        {
            return Problemi.AccessoNegato(Messaggi.SessioneNonValida);
        }

        var token = await servizio.RinnovaAsync(
            richiesta.TokenRinnovo, Dispositivo(richiesta.Dispositivo), http.Connection.RemoteIpAddress, ct);

        return token is null
            ? Problemi.AccessoNegato(Messaggi.SessioneNonValida)
            : Results.Ok(RispostaToken.Da(token));
    }

    private static async Task<IResult> EsciAsync(
        RichiestaRinnovo richiesta, ClaimsPrincipal utente, HttpContext http, ServizioAutenticazione servizio, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(richiesta.TokenRinnovo))
        {
            await servizio.EsciAsync(utente.AziendaId(), utente.UtenteId(), richiesta.TokenRinnovo, http.Connection.RemoteIpAddress, ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ProfiloAsync(ClaimsPrincipal utente, ServizioAutenticazione servizio, CancellationToken ct)
    {
        var profilo = await servizio.ProfiloAsync(utente.AziendaId(), utente.UtenteId(), ct);

        return Results.Ok(new RispostaProfilo(
            profilo.UtenteId,
            profilo.AziendaId,
            profilo.Email,
            profilo.Nome,
            profilo.Cognome,
            profilo.Ruoli,
            [.. profilo.Permessi.Select(p => new RispostaPermesso(p.Codice, p.Ambito == AmbitoPermesso.Propri ? "propri" : "tutti"))]));
    }

    private static bool TokenInstallazioneValido(string? ricevuto, string atteso)
    {
        if (string.IsNullOrEmpty(atteso) || string.IsNullOrEmpty(ricevuto))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(ricevuto), Encoding.UTF8.GetBytes(atteso));
    }

    private static string? Dispositivo(string? dispositivo) =>
        string.IsNullOrWhiteSpace(dispositivo) ? null
        : dispositivo.Length <= LunghezzaMassimaDispositivo ? dispositivo.Trim()
        : dispositivo[..LunghezzaMassimaDispositivo].Trim();
}

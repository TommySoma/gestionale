using System.Threading.RateLimiting;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Autenticazione;
using Gestionale.Infrastructure.Accessi;
using Gestionale.Infrastructure.Anagrafica;
using Gestionale.Infrastructure.Database;
using Gestionale.Infrastructure.Sicurezza;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Gestionale.Api.Autenticazione;

internal static class ConfigurazioneServizi
{
    public const string NomeStringaConnessione = "Database";

    /// <summary>Database, sicurezza, autenticazione, autorizzazione e limiti di richieste.</summary>
    public static void AggiungiGestionale(this WebApplicationBuilder builder)
    {
        var opzioni = builder.Configuration.GetSection(OpzioniAutenticazione.Sezione).Get<OpzioniAutenticazione>() ?? new OpzioniAutenticazione();
        var chiavi = LeggiChiavi(opzioni);
        var stringaConnessione = builder.Configuration.GetConnectionString(NomeStringaConnessione);
        if (string.IsNullOrWhiteSpace(stringaConnessione))
        {
            throw Mancante($"ConnectionStrings:{NomeStringaConnessione} (utente app_api).");
        }

        var parametriArgon2 = builder.Configuration.GetSection("Argon2").Get<ParametriArgon2>() ?? new ParametriArgon2();

        var servizi = builder.Services;
        servizi.AddSingleton(TimeProvider.System);
        servizi.AddSingleton(opzioni);
        servizi.AddSingleton(chiavi);
        servizi.AddSingleton(_ => NpgsqlDataSource.Create(stringaConnessione));
        servizi.AddSingleton<Database>();
        servizi.AddSingleton<IRepositoryAccessi, RepositoryAccessi>();
        servizi.AddSingleton<IHasherPassword>(new HasherArgon2id(parametriArgon2));
        servizi.AddSingleton<IElencoPasswordNote, ElencoPasswordNote>();
        servizi.AddSingleton<IServizioTotp, ServizioTotp>();
        servizi.AddSingleton<ICifratoreSegreti>(new CifratoreAesGcm(chiavi.CifraturaMfa));
        servizi.AddSingleton<IEmettitoreToken, EmettitoreJwt>();
        servizi.AddSingleton<ServizioAutenticazione>();
        servizi.AddSingleton<IRepositoryClienti, RepositoryClienti>();
        servizi.AddSingleton<ServizioClienti>();

        servizi.AddProblemDetails();

        servizi.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = EmettitoreJwt.Parametri(opzioni, chiavi, Destinatari.Api);
            })
            .AddJwtBearer(SchemiAutenticazione.PassaggioMfa, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = EmettitoreJwt.Parametri(opzioni, chiavi, Destinatari.PassaggioMfa);
            });

        servizi.AddSingleton<IAuthorizationPolicyProvider, FornitorePolicyPermessi>();
        servizi.AddSingleton<IAuthorizationHandler, GestoreRequisitoPermesso>();
        servizi.AddAuthorizationBuilder()
            // Sicuro per impostazione predefinita: ogni endpoint richiede un token di accesso,
            // salvo quelli marcati esplicitamente AllowAnonymous.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(PolicyAutenticazione.VerificaMfa, p => p
                .AddAuthenticationSchemes(SchemiAutenticazione.PassaggioMfa)
                .RequireClaim(ClaimGestionale.Scopo, ScopiPassaggio.Verifica))
            .AddPolicy(PolicyAutenticazione.ConfigurazioneMfa, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, SchemiAutenticazione.PassaggioMfa)
                .RequireAuthenticatedUser()
                // Token di accesso (nessuno scopo) oppure token di passaggio per la configurazione.
                .RequireAssertion(c =>
                    !c.User.HasClaim(x => x.Type == ClaimGestionale.Scopo)
                    || c.User.HasClaim(ClaimGestionale.Scopo, ScopiPassaggio.Configurazione)));

        servizi.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LimitiRichieste.Accesso, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "sconosciuto",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opzioni.LimiteRichiesteAccessoAlMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            o.OnRejected = (contesto, ct) => new ValueTask(contesto.HttpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = Messaggi.TitoloTroppeRichieste,
                    Detail = Messaggi.TroppeRichieste,
                },
                ct));
        });
    }

    private static ChiaviAutenticazione LeggiChiavi(OpzioniAutenticazione opzioni)
    {
        var firma = DaBase64(opzioni.ChiaveFirma, $"{OpzioniAutenticazione.Sezione}:ChiaveFirma");
        if (firma.Length < 32)
        {
            throw Mancante($"{OpzioniAutenticazione.Sezione}:ChiaveFirma deve essere di almeno 32 byte in Base64.");
        }

        var cifratura = DaBase64(opzioni.ChiaveCifraturaMfa, $"{OpzioniAutenticazione.Sezione}:ChiaveCifraturaMfa");
        if (cifratura.Length != 32)
        {
            throw Mancante($"{OpzioniAutenticazione.Sezione}:ChiaveCifraturaMfa deve essere di 32 byte in Base64.");
        }

        return new ChiaviAutenticazione(new SymmetricSecurityKey(firma), cifratura);
    }

    private static byte[] DaBase64(string valore, string nome)
    {
        if (string.IsNullOrWhiteSpace(valore))
        {
            throw Mancante($"{nome} non impostata.");
        }

        try
        {
            return Convert.FromBase64String(valore);
        }
        catch (FormatException)
        {
            throw Mancante($"{nome} non è in Base64.");
        }
    }

    private static InvalidOperationException Mancante(string dettaglio) =>
        new(string.Format(System.Globalization.CultureInfo.CurrentCulture, Messaggi.ConfigurazioneMancante, dettaglio));
}

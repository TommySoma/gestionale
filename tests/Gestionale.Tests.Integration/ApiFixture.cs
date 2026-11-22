using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gestionale.Api.Endpoint;
using Microsoft.AspNetCore.Mvc.Testing;
using OtpNet;

namespace Gestionale.Tests.Integration;

/// <summary>
/// API vera contro un PostgreSQL vero, già inizializzata con il primo avvio.
/// Utenti, email e password sono fittizi e validi solo in questo container.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string TokenInstallazione = "token-installazione-di-test";
    public const string EmailTitolare = "mario.rossi@alfa.example";
    public const string EmailAmministratore = "it@alfa.example";
    public const string PasswordTitolare = "caldaia-verde-sul-tetto";
    public const string PasswordAmministratore = "server-nello-sgabuzzino";

    private readonly DatabaseFixture _database = new();
    private readonly Dictionary<string, string> _token = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _accessi = new(1, 1);
    private WebApplicationFactory<Program>? _fabbrica;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        _fabbrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Database", _database.StringaConnessioneApi);
            b.UseSetting("Autenticazione:ChiaveFirma", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            b.UseSetting("Autenticazione:ChiaveCifraturaMfa", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            b.UseSetting("Autenticazione:TokenInstallazione", TokenInstallazione);
            b.UseSetting("Autenticazione:LimiteRichiesteAccessoAlMinuto", "1000");
            // Parametri leggeri per non rallentare i test; in produzione valgono i minimi OWASP.
            b.UseSetting("Argon2:MemoriaKiB", "8192");
            b.UseSetting("Argon2:Iterazioni", "1");
        });

        using var client = Client();
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, "/api/installazione")
        {
            Content = JsonContent.Create(new
            {
                ragioneSociale = "Termoidraulica Alfa Srl (fittizia)",
                partitaIva = "01234567897",
                regione = "toscana",
                titolare = new { email = EmailTitolare, nome = "Mario", cognome = "Rossi", password = PasswordTitolare },
                amministratore = new { email = EmailAmministratore, nome = "Ivo", cognome = "Server", password = PasswordAmministratore },
            }),
        };
        richiesta.Headers.Add("X-Token-Installazione", TokenInstallazione);

        var risposta = await client.SendAsync(richiesta);
        risposta.EnsureSuccessStatusCode();
    }

    /// <summary>Per i controlli diretti sul database, senza Row-Level Security.</summary>
    public string StringaConnessioneOwner => _database.StringaConnessioneOwner;

    public HttpClient Client(string? tokenAccesso = null)
    {
        var client = (_fabbrica ?? throw new InvalidOperationException("API non avviata.")).CreateClient();
        if (tokenAccesso is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAccesso);
        }

        return client;
    }

    /// <summary>
    /// Token di accesso di un utente, ottenuto una volta e riusato (vale 15 minuti).
    /// Se il ruolo richiede il secondo fattore, lo configura al primo accesso.
    /// </summary>
    public async Task<string> TokenAsync(string email, string password)
    {
        await _accessi.WaitAsync();
        try
        {
            if (_token.TryGetValue(email, out var salvato))
            {
                return salvato;
            }

            using var client = Client();
            var risposta = await client.PostAsJsonAsync("/api/accesso", new { email, password });
            risposta.EnsureSuccessStatusCode();
            var accesso = (await risposta.Content.ReadFromJsonAsync<RispostaAccesso>())!;

            var token = accesso.Esito switch
            {
                "completato" => accesso.Token!.TokenAccesso,
                "mfa_da_configurare" => await ConfiguraSecondoFattoreAsync(accesso.TokenPassaggio!),
                _ => throw new InvalidOperationException($"Esito inatteso per {email}: {accesso.Esito}. Usa un utente senza secondo fattore già attivo."),
            };

            _token[email] = token;
            return token;
        }
        finally
        {
            _accessi.Release();
        }
    }

    public Task<string> TokenTitolareAsync() => TokenAsync(EmailTitolare, PasswordTitolare);

    /// <summary>Crea un utente come Titolare; restituisce l'email.</summary>
    public async Task<string> CreaUtenteAsync(string email, string password, params string[] ruoli)
    {
        using var titolare = Client(await TokenTitolareAsync());
        var risposta = await titolare.PostAsJsonAsync("/api/utenti", new { email, nome = "Prova", cognome = "Fittizia", password, ruoli });
        risposta.EnsureSuccessStatusCode();
        return email;
    }

    private async Task<string> ConfiguraSecondoFattoreAsync(string tokenPassaggio)
    {
        using var passaggio = Client(tokenPassaggio);
        var configurazione = (await (await passaggio.PostAsync("/api/mfa/configurazione", null)).Content.ReadFromJsonAsync<RispostaConfigurazioneMfa>())!;
        var codice = new Totp(Base32Encoding.ToBytes(configurazione.Segreto)).ComputeTotp(DateTime.UtcNow);
        var attivazione = await passaggio.PostAsJsonAsync("/api/mfa/attivazione", new { codice });
        attivazione.EnsureSuccessStatusCode();
        return (await attivazione.Content.ReadFromJsonAsync<RispostaAttivazioneMfa>())!.Token!.TokenAccesso;
    }

    public async ValueTask DisposeAsync()
    {
        if (_fabbrica is not null)
        {
            await _fabbrica.DisposeAsync();
        }

        await _database.DisposeAsync();
        _accessi.Dispose();
    }
}

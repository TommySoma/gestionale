using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

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
                partitaIva = "00000000001",
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

    public async ValueTask DisposeAsync()
    {
        if (_fabbrica is not null)
        {
            await _fabbrica.DisposeAsync();
        }

        await _database.DisposeAsync();
    }
}

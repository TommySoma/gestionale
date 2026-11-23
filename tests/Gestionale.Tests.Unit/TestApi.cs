using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gestionale.Tests.Unit;

/// <summary>API configurata con chiavi casuali e un database che non viene mai contattato.</summary>
public sealed class FabbricaApiSenzaDatabase : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Database", "Host=database-inesistente;Database=gestionale;Username=app_api;Password=nessuna");
        builder.UseSetting("Autenticazione:ChiaveFirma", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Autenticazione:ChiaveCifraturaMfa", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }
}

public sealed class TestApi(FabbricaApiSenzaDatabase fabbrica) : IClassFixture<FabbricaApiSenzaDatabase>
{
    [Fact]
    public async Task Il_controllo_di_salute_risponde()
    {
        using var client = fabbrica.CreateClient();

        var risposta = await client.GetAsync(new Uri("/salute", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("Healthy", await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/api/io")]
    [InlineData("/api/utenti")]
    public async Task Senza_token_gli_endpoint_protetti_rispondono_401(string percorso)
    {
        using var client = fabbrica.CreateClient();

        var risposta = await client.GetAsync(new Uri(percorso, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, risposta.StatusCode);
    }

    [Fact]
    public async Task Un_token_firmato_con_un_altra_chiave_è_rifiutato()
    {
        using var client = fabbrica.CreateClient();
        // Token corretto nella forma, con emittente e destinatario giusti, ma firmato
        // con una chiave casuale diversa da quella dell'API. Generato qui, così nel
        // repository non resta nessuna stringa che somigli a un token.
        var tokenEstraneo = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "gestionale",
            Audience = "gestionale-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["azienda"] = Guid.NewGuid().ToString() },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenEstraneo);

        var risposta = await client.GetAsync(new Uri("/api/io", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, risposta.StatusCode);
    }

    [Fact]
    public void Senza_chiavi_l_API_non_parte()
    {
        // Le chiavi si svuotano in modo esplicito: sul computer di chi sviluppa
        // scripts/sviluppo le salva negli user-secrets, che in Development si leggono.
        using var senzaChiavi = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Database", "Host=database-inesistente")
            .UseSetting("Autenticazione:ChiaveFirma", "")
            .UseSetting("Autenticazione:ChiaveCifraturaMfa", ""));

        var errore = Assert.ThrowsAny<Exception>(() => senzaChiavi.CreateClient());

        Assert.Contains("Autenticazione:ChiaveFirma", errore.ToString(), StringComparison.Ordinal);
    }
}

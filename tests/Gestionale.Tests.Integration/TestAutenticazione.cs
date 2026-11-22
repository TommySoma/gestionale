using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Npgsql;
using OtpNet;

namespace Gestionale.Tests.Integration;

/// <summary>
/// Criterio di uscita di M1: accesso con secondo fattore e ruoli funzionanti,
/// provati dall'API fino al database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestAutenticazione(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string EmailTecnico = "luca.verdi@alfa.example";
    private const string PasswordTecnico = "chiave-inglese-da-quattordici";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dopo_il_primo_avvio_l_installazione_non_è_più_da_inizializzare()
    {
        using var client = api.Client();

        var stato = await client.GetFromJsonAsync<RispostaStatoInstallazione>("/api/installazione", Ct);

        Assert.False(stato!.DaInizializzare);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Forbidden)]
    [InlineData("token-sbagliato", HttpStatusCode.Forbidden)]
    [InlineData(ApiFixture.TokenInstallazione, HttpStatusCode.Conflict)]
    public async Task Il_primo_avvio_non_si_ripete(string? token, HttpStatusCode atteso)
    {
        using var client = api.Client();
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, "/api/installazione")
        {
            Content = JsonContent.Create(new
            {
                ragioneSociale = "Intrusa Srl (fittizia)",
                regione = "toscana",
                titolare = new { email = "intruso@intrusa.example", nome = "Ugo", cognome = "Neri", password = "una-password-qualsiasi" },
            }),
        };
        if (token is not null)
        {
            richiesta.Headers.Add("X-Token-Installazione", token);
        }

        var risposta = await client.SendAsync(richiesta, Ct);

        Assert.Equal(atteso, risposta.StatusCode);
    }

    [Fact]
    public async Task Flusso_completo_di_accesso_secondo_fattore_ruoli_e_sessioni()
    {
        // --- Titolare: il ruolo obbliga al secondo fattore, che va configurato al primo accesso.
        var primo = await AccediAsync(ApiFixture.EmailTitolare, ApiFixture.PasswordTitolare);
        Assert.Equal("mfa_da_configurare", primo.Esito);
        Assert.Null(primo.Token);

        using (var passaggio = api.Client(primo.TokenPassaggio))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await passaggio.GetAsync("/api/io", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await passaggio.PostAsJsonAsync("/api/accesso/mfa", new { codice = "123456" }, Ct)).StatusCode);

            var configurazione = await LeggiAsync<RispostaConfigurazioneMfa>(await passaggio.PostAsync("/api/mfa/configurazione", null, Ct));
            Assert.StartsWith("otpauth://totp/", configurazione.UriConfigurazione, StringComparison.Ordinal);
            var segretoTitolare = Base32Encoding.ToBytes(configurazione.Segreto);

            var sbagliato = await passaggio.PostAsJsonAsync("/api/mfa/attivazione", new { codice = CodiceSbagliato(segretoTitolare) }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, sbagliato.StatusCode);

            var codiceAttivazione = new Totp(segretoTitolare).ComputeTotp(DateTime.UtcNow);
            var attivazione = await LeggiAsync<RispostaAttivazioneMfa>(
                await passaggio.PostAsJsonAsync("/api/mfa/attivazione", new { codice = codiceAttivazione }, Ct));
            Assert.Equal(10, attivazione.CodiciRecupero.Count);
            Assert.NotNull(attivazione.Token);

            using var titolare = api.Client(attivazione.Token.TokenAccesso);
            var profilo = await titolare.GetFromJsonAsync<RispostaProfilo>("/api/io", Ct);
            Assert.Equal(["titolare"], profilo!.Ruoli);
            Assert.Contains(new RispostaPermesso("utenti.scrittura", "tutti"), profilo.Permessi);
            Assert.DoesNotContain(profilo.Permessi, p => p.Codice.StartsWith("sistema.", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.Conflict, (await titolare.PostAsync("/api/mfa/configurazione", null, Ct)).StatusCode);

            // --- Accessi successivi: serve il codice, che non si può riusare.
            var secondo = await AccediAsync(ApiFixture.EmailTitolare, ApiFixture.PasswordTitolare);
            Assert.Equal("mfa_richiesto", secondo.Esito);
            using var verifica = api.Client(secondo.TokenPassaggio);
            Assert.Equal(HttpStatusCode.Unauthorized, (await verifica.PostAsJsonAsync("/api/accesso/mfa", new { codice = codiceAttivazione }, Ct)).StatusCode);

            var codiceSuccessivo = new Totp(segretoTitolare).ComputeTotp(DateTime.UtcNow.AddSeconds(30));
            var conCodice = await LeggiAsync<RispostaAccesso>(await verifica.PostAsJsonAsync("/api/accesso/mfa", new { codice = codiceSuccessivo }, Ct));
            Assert.Equal("completato", conCodice.Esito);

            // --- Codice di recupero: vale una volta sola.
            var codiceRecupero = attivazione.CodiciRecupero[0].ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
            using (var recupero = api.Client((await AccediAsync(ApiFixture.EmailTitolare, ApiFixture.PasswordTitolare)).TokenPassaggio))
            {
                Assert.Equal(HttpStatusCode.OK, (await recupero.PostAsJsonAsync("/api/accesso/mfa", new { codice = codiceRecupero }, Ct)).StatusCode);
            }

            using (var recupero = api.Client((await AccediAsync(ApiFixture.EmailTitolare, ApiFixture.PasswordTitolare)).TokenPassaggio))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await recupero.PostAsJsonAsync("/api/accesso/mfa", new { codice = codiceRecupero }, Ct)).StatusCode);
            }

            // --- Il titolare crea un tecnico, con le regole sulle password e sui ruoli.
            Assert.Equal(HttpStatusCode.Created, (await titolare.PostAsJsonAsync("/api/utenti", NuovoUtente(EmailTecnico, PasswordTecnico, "tecnico"), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await titolare.PostAsJsonAsync("/api/utenti", NuovoUtente(EmailTecnico.ToUpperInvariant(), PasswordTecnico, "tecnico"), Ct)).StatusCode);

            var passwordDebole = await titolare.PostAsJsonAsync("/api/utenti", NuovoUtente("debole@alfa.example", "Password1234", "tecnico"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, passwordDebole.StatusCode);
            Assert.Contains("troppo comune", await passwordDebole.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

            Assert.Equal(HttpStatusCode.BadRequest, (await titolare.PostAsJsonAsync("/api/utenti", NuovoUtente("x@alfa.example", PasswordTecnico, "capocantiere"), Ct)).StatusCode);
        }

        // --- Amministratore di sistema: gestisce gli utenti ma non può darsi ruoli di business.
        var amministratore = await AccediConNuovoSecondoFattoreAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore);
        using (var admin = api.Client(amministratore.TokenAccesso))
        {
            var utenti = await admin.GetFromJsonAsync<List<RispostaUtente>>("/api/utenti", Ct);
            Assert.Equal(3, utenti!.Count);
            Assert.Contains(utenti, u => u.Email == EmailTecnico && u.Ruoli.SequenceEqual(["tecnico"]));

            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/utenti", NuovoUtente("spia@alfa.example", PasswordTecnico, "sola_lettura"), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/utenti", NuovoUtente("it2@alfa.example", PasswordTecnico, "amministratore_sistema"), Ct)).StatusCode);
        }

        // --- Tecnico: nessun secondo fattore obbligatorio, permessi solo sui propri dati.
        var tecnico = await AccediAsync(EmailTecnico, PasswordTecnico);
        Assert.Equal("completato", tecnico.Esito);
        using (var client = api.Client(tecnico.Token!.TokenAccesso))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/utenti", Ct)).StatusCode);
            var profilo = await client.GetFromJsonAsync<RispostaProfilo>("/api/io", Ct);
            Assert.Contains(new RispostaPermesso("clienti.lettura", "propri"), profilo!.Permessi);
            Assert.Contains(new RispostaPermesso("listino.lettura", "tutti"), profilo.Permessi);
        }

        // --- Rinnovo a rotazione: il token vecchio, se riusato, fa revocare tutta la catena.
        var rinnovato = await RinnovaAsync(tecnico.Token.TokenRinnovo);
        Assert.Equal(HttpStatusCode.OK, rinnovato.StatusCode);
        var nuovaCoppia = await LeggiAsync<RispostaToken>(rinnovato);
        Assert.NotEqual(tecnico.Token.TokenRinnovo, nuovaCoppia.TokenRinnovo);
        Assert.Equal(tecnico.Token.RinnovoScadeIl.ToUnixTimeSeconds(), nuovaCoppia.RinnovoScadeIl.ToUnixTimeSeconds());

        Assert.Equal(HttpStatusCode.Unauthorized, (await RinnovaAsync(tecnico.Token.TokenRinnovo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RinnovaAsync(nuovaCoppia.TokenRinnovo)).StatusCode);
        Assert.Equal(1, await ContaAuditAsync("sessione.riuso_rilevato"));

        // --- Uscita: il token di rinnovo non vale più.
        var perUscita = await AccediAsync(EmailTecnico, PasswordTecnico);
        using (var client = api.Client(perUscita.Token!.TokenAccesso))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/sessione/uscita", new { tokenRinnovo = perUscita.Token.TokenRinnovo }, Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RinnovaAsync(perUscita.Token.TokenRinnovo)).StatusCode);

        // --- Blocco progressivo: dopo 5 errori anche la password giusta è rifiutata.
        for (var tentativo = 1; tentativo <= 5; tentativo++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await TentaAccessoAsync(EmailTecnico, "password-sbagliata-" + tentativo)).StatusCode);
        }

        var bloccato = await TentaAccessoAsync(EmailTecnico, PasswordTecnico);
        Assert.Equal(HttpStatusCode.Unauthorized, bloccato.StatusCode);
        Assert.Equal(1, await ContaAuditAsync("accesso.bloccato"));

        // Email inesistente: stessa risposta di una password sbagliata.
        var sconosciuto = await TentaAccessoAsync("nessuno@alfa.example", "password-qualsiasi-lunga");
        Assert.Equal(HttpStatusCode.Unauthorized, sconosciuto.StatusCode);
        Assert.Equal(await DettaglioAsync(bloccato), await DettaglioAsync(sconosciuto));
    }

    private static object NuovoUtente(string email, string password, string ruolo) =>
        new { email, nome = "Prova", cognome = "Fittizia", password, ruoli = new[] { ruolo } };

    private static string CodiceSbagliato(byte[] segreto)
    {
        var totp = new Totp(segreto);
        var validi = new[] { -30, 0, 30 }.Select(s => totp.ComputeTotp(DateTime.UtcNow.AddSeconds(s))).ToHashSet();
        return Enumerable.Range(0, 10).Select(i => new string((char)('0' + i), 6)).First(c => !validi.Contains(c));
    }

    private static async Task<string?> DettaglioAsync(HttpResponseMessage risposta) =>
        (await risposta.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Ct))?.Detail;

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> TentaAccessoAsync(string email, string password)
    {
        using var client = api.Client();
        return await client.PostAsJsonAsync("/api/accesso", new { email, password, dispositivo = "Test di integrazione" }, Ct);
    }

    private async Task<RispostaAccesso> AccediAsync(string email, string password) =>
        await LeggiAsync<RispostaAccesso>(await TentaAccessoAsync(email, password));

    private async Task<HttpResponseMessage> RinnovaAsync(string tokenRinnovo)
    {
        using var client = api.Client();
        return await client.PostAsJsonAsync("/api/sessione/rinnovo", new { tokenRinnovo }, Ct);
    }

    private async Task<RispostaToken> AccediConNuovoSecondoFattoreAsync(string email, string password)
    {
        var accesso = await AccediAsync(email, password);
        Assert.Equal("mfa_da_configurare", accesso.Esito);

        using var passaggio = api.Client(accesso.TokenPassaggio);
        var configurazione = await LeggiAsync<RispostaConfigurazioneMfa>(await passaggio.PostAsync("/api/mfa/configurazione", null, Ct));
        var codice = new Totp(Base32Encoding.ToBytes(configurazione.Segreto)).ComputeTotp(DateTime.UtcNow);
        var attivazione = await LeggiAsync<RispostaAttivazioneMfa>(await passaggio.PostAsJsonAsync("/api/mfa/attivazione", new { codice }, Ct));

        return attivazione.Token!;
    }

    private async Task<long> ContaAuditAsync(string azione)
    {
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM sec.audit_log WHERE azione = @azione", connessione);
        comando.Parameters.AddWithValue("azione", azione);
        return (long)(await comando.ExecuteScalarAsync(Ct))!;
    }
}

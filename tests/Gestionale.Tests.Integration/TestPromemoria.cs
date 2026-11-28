using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Promemoria;
using Gestionale.Application.Scadenze;

namespace Gestionale.Tests.Integration;

/// <summary>
/// Promemoria email dall'API a un server di posta finto (Mailpit, in un container):
/// parametri del server, prova, anteprima con esclusioni, invio e richiamo registrato.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestPromemoria(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    private const string PasswordProva = "promemoria-di-prova";

    private readonly IContainer _posta = new ContainerBuilder("axllent/mailpit:v1.33.1")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
        .Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _posta.StartAsync(Ct);

    public async ValueTask DisposeAsync() => await _posta.DisposeAsync();

    [Fact]
    public async Task Dalla_configurazione_all_invio()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());

        // Senza server di posta l'invio non parte; l'anteprima sì.
        Assert.Equal(HttpStatusCode.NoContent, (await titolare.GetAsync("/api/azienda/posta", Ct)).StatusCode);

        var posta = new
        {
            host = _posta.Hostname,
            porta = _posta.GetMappedPublicPort(1025),
            sicurezza = "nessuna",
            mittenteEmail = "promemoria@alfa.example",
            mittenteNome = "Termoidraulica Alfa (prova)",
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await titolare.PutAsJsonAsync("/api/azienda/posta", new { host = "smtp example", porta = 0, sicurezza = "tls", mittenteEmail = "no" }, Ct)).StatusCode);
        var salvata = await LeggiAsync<JsonElement>(await titolare.PutAsJsonAsync("/api/azienda/posta", posta, Ct));
        var versione = salvata.GetProperty("versione").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await titolare.PutAsJsonAsync("/api/azienda/posta", posta, Ct)).StatusCode);

        var letti = (await titolare.GetFromJsonAsync<ParametriPosta>("/api/azienda/posta", Ct))!;
        Assert.Equal(("nessuna", false, versione), (letti.Sicurezza, letti.ConPassword, letti.Versione));

        // La prova arriva a chi configura.
        Assert.Equal(HttpStatusCode.OK, (await titolare.PostAsync("/api/azienda/posta/prova", null, Ct)).StatusCode);
        Assert.Contains(await MessaggiAsync(), m => m.Destinatario == ApiFixture.EmailTitolare);

        // Due clienti: uno con email e consenso, uno con email ma senza consenso.
        var conConsenso = await ScadenzaAsync(titolare, "Giulia", "Promemoria", "giulia.promemoria@example.com", consenso: true, "1");
        var senzaConsenso = await ScadenzaAsync(titolare, "Paolo", "Promemoria", "paolo.promemoria@example.com", consenso: false, "2");

        var anteprima = await LeggiAsync<List<RispostaPromemoria>>(await titolare.PostAsJsonAsync("/api/promemoria/anteprima",
            new { scadenze = new[] { conConsenso, senzaConsenso } }, Ct));
        Assert.Equal(2, anteprima.Count);
        Assert.Contains(anteprima, p => p.ScadenzaId == senzaConsenso && p.Esclusione == "SenzaConsenso" && p.Oggetto is null);
        var pronto = anteprima.Single(p => p.ScadenzaId == conConsenso);
        Assert.Null(pronto.Esclusione);
        Assert.Contains("Gentile Giulia Promemoria", pronto.Testo, StringComparison.Ordinal);
        Assert.Single(await MessaggiAsync());

        var inviati = await LeggiAsync<List<RispostaPromemoria>>(await titolare.PostAsJsonAsync("/api/promemoria",
            new { scadenze = new[] { conConsenso, senzaConsenso }, nota = "Siamo disponibili dal lunedì al venerdì (prova)." }, Ct));
        Assert.True(inviati.Single(p => p.ScadenzaId == conConsenso).Inviato);
        Assert.False(inviati.Single(p => p.ScadenzaId == senzaConsenso).Inviato);

        var messaggio = Assert.Single(await MessaggiAsync(), m => m.Destinatario == "giulia.promemoria@example.com");
        Assert.Contains("manutenzione", messaggio.Oggetto, StringComparison.OrdinalIgnoreCase);

        // L'invio è un richiamo: la scadenza ora è in attesa di risposta.
        var scadenza = (await titolare.GetFromJsonAsync<Scadenza>($"/api/scadenze/{conConsenso}", Ct))!;
        Assert.Equal("in_attesa", scadenza.StatoRichiamo);
    }

    [Fact]
    public async Task Server_di_posta_e_promemoria_li_gestisce_chi_ne_ha_il_permesso()
    {
        var sola = await api.CreaUtenteAsync("lettura.promemoria@alfa.example", PasswordProva, "sola_lettura");
        using var lettura = api.Client(await api.TokenAsync(sola, PasswordProva));
        Assert.Equal(HttpStatusCode.Forbidden, (await lettura.PostAsJsonAsync("/api/promemoria/anteprima", new { scadenze = new[] { Guid.NewGuid() } }, Ct)).StatusCode);

        var amministrativo = await api.CreaUtenteAsync("amm.promemoria@alfa.example", PasswordProva, "amministrativo");
        using var ufficio = api.Client(await api.TokenAsync(amministrativo, PasswordProva));
        Assert.Equal(HttpStatusCode.Forbidden, (await ufficio.PutAsJsonAsync("/api/azienda/posta",
            new { host = "smtp.example.com", porta = 587, sicurezza = "starttls", mittenteEmail = "info@alfa.example" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ufficio.PostAsJsonAsync("/api/promemoria/anteprima", new { scadenze = Array.Empty<Guid>() }, Ct)).StatusCode);
    }

    // Un cliente con sede, email e, se richiesto, il consenso ai promemoria; il suo impianto con la manutenzione annuale.
    private static async Task<Guid> ScadenzaAsync(HttpClient client, string nome, string cognome, string email, bool consenso, string civico)
    {
        var voci = (await client.GetFromJsonAsync<Gestionale.Application.Impianti.VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti", new { tipo = "privato", nome, cognome }, Ct));
        await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/contatti",
            new { tipo = "email", valore = email, principale = true }, Ct));
        if (consenso)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/consensi",
                new { finalita = "promemoria_scadenze", concesso = true, fonte = "modulo_firmato" }, Ct)).StatusCode);
        }

        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo = "Via dei Promemoria", civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto[0].Id, manutenzioneMesi = 12 }, Ct));
        var scadenze = (await client.GetFromJsonAsync<List<Scadenza>>($"/api/impianti/{impianto.Id}/scadenze", Ct))!;
        return scadenze.Single(s => s.Tipo == "manutenzione").ScadenzaId;
    }

    // Le email raccolte da Mailpit, dalla sua API.
    private async Task<List<(string Destinatario, string Oggetto)>> MessaggiAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{_posta.Hostname}:{_posta.GetMappedPublicPort(8025)}") };
        var risposta = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages", Ct);
        return [.. risposta.GetProperty("messages").EnumerateArray().Select(m => (
            m.GetProperty("To")[0].GetProperty("Address").GetString()!,
            m.GetProperty("Subject").GetString()!))];
    }

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;

namespace Gestionale.Tests.Integration;

/// <summary>Impianti dall'API al database: scheda, generatori, catasto, avvisi, concorrenza e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestImpianti(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "chiave-inglese-da-quattordici";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_voci_delle_lookup_sono_segnate_da_verificare()
    {
        using var client = api.Client(await api.TokenTitolareAsync());

        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;

        Assert.NotEmpty(voci.TipiImpianto);
        Assert.NotEmpty(voci.TipiGeneratore);
        Assert.NotEmpty(voci.Combustibili);
        Assert.All(voci.TipiImpianto.Concat(voci.TipiGeneratore).Concat(voci.Combustibili), v => Assert.True(v.DaVerificare));
    }

    [Fact]
    public async Task Un_impianto_si_crea_si_legge_si_modifica_e_si_elimina()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var voci = await VociAsync(client);
        var (clienteId, sedeId) = await ClienteConSedeAsync(client, "Via delle Caldaie", "1");

        var creato = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti", new
        {
            sedeId,
            tipoImpiantoId = voci.TipiImpianto[0].Id,
            annoInstallazione = 2015,
            potenzaUtileKw = 24.5m,
            combustibileId = voci.Combustibili[0].Id,
            pdr = " 0001 2345 6789 01 ",
            proprietarioClienteId = clienteId,
            note = "Caldaia in cucina",
        }, Ct));
        Assert.Equal(1, creato.Versione);
        Assert.Empty(creato.Avvisi);

        var letto = (await client.GetFromJsonAsync<Impianto>($"/api/impianti/{creato.Id}", Ct))!;
        Assert.Equal("attivo", letto.Stato);
        Assert.Equal("00012345678901", letto.Pdr);
        Assert.Equal(24.5m, letto.PotenzaUtileKw);
        Assert.Equal(clienteId, letto.ClienteId);
        Assert.Equal("Fittizio Impianti", letto.Proprietario);
        Assert.Equal("Via delle Caldaie", letto.Indirizzo);
        Assert.True(letto.TipoDaVerificare);

        // Dismissione: serve la data.
        var senzaData = await client.PutAsJsonAsync($"/api/impianti/{creato.Id}",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id, stato = "dismesso", versione = 1 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, senzaData.StatusCode);
        Assert.Contains("data di dismissione", await DettaglioAsync(senzaData), StringComparison.Ordinal);

        var dismesso = await LeggiAsync<RispostaScritturaImpianto>(await client.PutAsJsonAsync($"/api/impianti/{creato.Id}",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id, stato = "dismesso", dismessoIl = "2026-10-31", pdr = letto.Pdr, versione = 1 }, Ct));
        Assert.Equal(2, dismesso.Versione);
        letto = (await client.GetFromJsonAsync<Impianto>($"/api/impianti/{creato.Id}", Ct))!;
        Assert.Equal(new DateOnly(2026, 10, 31), letto.DismessoIl);
        Assert.Null(letto.Combustibile);

        // Un collega con la scheda aperta da prima non sovrascrive la modifica.
        var superata = await client.PutAsJsonAsync($"/api/impianti/{creato.Id}",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id, versione = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, superata.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/impianti/{creato.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/impianti/{creato.Id}?versione=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/impianti/{creato.Id}?versione=2", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{creato.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task I_dati_sbagliati_vengono_spiegati_tutti_insieme()
    {
        using var client = api.Client(await api.TokenTitolareAsync());

        var risposta = await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = Guid.NewGuid(), tipoImpiantoId = Guid.NewGuid(), annoInstallazione = 1850, potenzaUtileKw = -3 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        var testo = await DettaglioAsync(risposta);
        Assert.Contains("anno di installazione", testo, StringComparison.Ordinal);
        Assert.Contains("potenza", testo, StringComparison.Ordinal);

        // Le regole di forma passano, ma sede e tipo non esistono.
        risposta = await client.PostAsJsonAsync("/api/impianti", new { sedeId = Guid.NewGuid(), tipoImpiantoId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        testo = await DettaglioAsync(risposta);
        Assert.Contains("sede indicata non esiste", testo, StringComparison.Ordinal);
        Assert.Contains("tipo di impianto", testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Il_pdr_è_unico_tra_gli_impianti_attivi_e_l_indirizzo_doppio_è_un_avviso()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var voci = await VociAsync(client);
        var (_, sedeId) = await ClienteConSedeAsync(client, "Via dei Doppioni", "7");
        var (_, altraSedeStessoIndirizzo) = await ClienteConSedeAsync(client, "via dei doppioni ", "7");
        var tipo = voci.TipiImpianto[0].Id;

        var primo = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = tipo, pdr = "PDR-DOPPIO-1" }, Ct));
        Assert.Empty(primo.Avvisi);

        var stessoPdr = await client.PostAsJsonAsync("/api/impianti", new { sedeId, tipoImpiantoId = tipo, pdr = "pdr-doppio-1" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stessoPdr.StatusCode);
        Assert.Contains("PDR è già su un altro impianto", await DettaglioAsync(stessoPdr), StringComparison.Ordinal);

        // Stesso indirizzo scritto in modo diverso, stesso tipo: si salva, con un avviso.
        var secondo = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = altraSedeStessoIndirizzo, tipoImpiantoId = tipo }, Ct));
        Assert.Contains("stesso tipo a questo indirizzo", Assert.Single(secondo.Avvisi), StringComparison.Ordinal);
        Assert.True((await client.GetFromJsonAsync<Impianto>($"/api/impianti/{primo.Id}", Ct))!.IndirizzoDuplicato);

        // Un tipo diverso allo stesso indirizzo (caldaia e climatizzatore) è normale.
        var terzo = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[1].Id }, Ct));
        Assert.Empty(terzo.Avvisi);
    }

    [Fact]
    public async Task Generatori_e_riferimenti_al_catasto_si_gestiscono_dalla_scheda()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var voci = await VociAsync(client);
        var (_, sedeId) = await ClienteConSedeAsync(client, "Via dei Generatori", "3");
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id }, Ct));
        var percorso = $"/api/impianti/{impianto.Id}";

        var generatore = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"{percorso}/generatori", new
        {
            tipoGeneratoreId = voci.TipiGeneratore[0].Id,
            marca = "Marca Fittizia",
            modello = "Modello 24",
            matricola = "MAT-GEN-0001",
            potenzaKw = 24,
            installatoIl = "2015-03-10",
        }, Ct));
        Assert.Empty(generatore.Avvisi);

        var dateInvertite = await client.PostAsJsonAsync($"{percorso}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore[0].Id, installatoIl = "2020-01-01", dismessoIl = "2019-01-01" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, dateInvertite.StatusCode);

        // Stessa matricola, scritta in minuscolo: avviso, non errore.
        var doppio = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"{percorso}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore[1].Id, matricola = "mat-gen-0001" }, Ct));
        Assert.Contains("stessa matricola", Assert.Single(doppio.Avvisi), StringComparison.Ordinal);

        // Il primo generatore si dismette: resta nella scheda con la data.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{percorso}/generatori/{generatore.Id}", new
        {
            tipoGeneratoreId = voci.TipiGeneratore[0].Id,
            marca = "Marca Fittizia",
            matricola = "MAT-GEN-0001",
            installatoIl = "2015-03-10",
            dismessoIl = "2026-11-01",
            versione = 1,
        }, Ct)).StatusCode);

        var siert = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"{percorso}/riferimenti-catasto",
            new { regione = "Toscana", codice = "CAT-PROVA-0001", registratoIl = "2016-01-20" }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{percorso}/riferimenti-catasto",
            new { regione = "toscana", codice = "CAT-PROVA-0002" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{percorso}/riferimenti-catasto",
            new { regione = "veneto", codice = "CAT 3" }, Ct)).StatusCode);

        var scheda = (await client.GetFromJsonAsync<Impianto>(percorso, Ct))!;
        Assert.Equal(2, scheda.Generatori.Count);
        Assert.All(scheda.Generatori, g => Assert.True(g.MatricolaDuplicata));
        Assert.Equal(new DateOnly(2026, 11, 1), scheda.Generatori.Single(g => g.GeneratoreId == generatore.Id).DismessoIl);
        var riferimento = Assert.Single(scheda.RiferimentiCatasto);
        Assert.Equal(("toscana", "siert"), (riferimento.Regione, riferimento.Sistema));

        // Lo stesso codice del catasto non può stare su due impianti della stessa azienda.
        var altro = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[1].Id }, Ct));
        var codiceInUso = await client.PostAsJsonAsync($"/api/impianti/{altro.Id}/riferimenti-catasto",
            new { regione = "toscana", codice = "CAT-PROVA-0001" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, codiceInUso.StatusCode);
        Assert.Contains("codice del catasto è già", await DettaglioAsync(codiceInUso), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{percorso}/riferimenti-catasto/{siert.Id}",
            new { regione = "toscana", codice = "CAT-PROVA-0009", versione = 1 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{percorso}/riferimenti-catasto/{siert.Id}?versione=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{percorso}/riferimenti-catasto/{siert.Id}?versione=2", Ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<Impianto>(percorso, Ct))!.RiferimentiCatasto);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/impianti/{Guid.NewGuid()}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore[0].Id }, Ct)).StatusCode);
    }

    [Fact]
    public async Task La_ricerca_trova_per_cliente_indirizzo_pdr_matricola_e_codice()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var voci = await VociAsync(client);
        var (clienteId, sedeId) = await ClienteConSedeAsync(client, "Piazza della Ricerca", "5");
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id, pdr = "PDRRICERCA42" }, Ct));
        await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"/api/impianti/{impianto.Id}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore[0].Id, matricola = "MATRICERCA42" }, Ct));
        await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"/api/impianti/{impianto.Id}/riferimenti-catasto",
            new { regione = "lombardia", codice = "TARGARICERCA42" }, Ct));

        foreach (var testo in new[] { "piazza della ricerca", "PDRRICERCA42", "matricerca42", "TARGARICERCA42" })
        {
            var pagina = await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>($"/api/impianti?cerca={Uri.EscapeDataString(testo)}", Ct);
            var trovato = Assert.Single(pagina!.Elementi);
            Assert.Equal(impianto.Id, trovato.ImpiantoId);
            Assert.Equal("TARGARICERCA42", trovato.CodiceCatasto);
            Assert.Equal(1, trovato.NumeroGeneratori);
        }

        var delCliente = await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>($"/api/impianti?cliente={clienteId}", Ct);
        var riga = Assert.Single(delCliente!.Elementi);
        Assert.Equal((impianto.Id, sedeId), (riga.ImpiantoId, riga.SedeId));
        Assert.Equal(0, (await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>($"/api/impianti?cliente={clienteId}&stato=dismesso", Ct))!.Totale);

        // Se il cliente viene eliminato, i suoi impianti spariscono con lui.
        var cliente = (await client.GetFromJsonAsync<Cliente>($"/api/clienti/{clienteId}", Ct))!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/clienti/{clienteId}?versione={cliente.Versione}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{impianto.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Ogni_ruolo_vede_e_modifica_solo_quello_che_gli_spetta()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var voci = await VociAsync(titolare);
        var (_, sedeId) = await ClienteConSedeAsync(titolare, "Via dei Permessi", "9");
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await titolare.PostAsJsonAsync("/api/impianti",
            new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id }, Ct));
        var nuovo = new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id };

        // Responsabile tecnico: vede e modifica tutto.
        var responsabile = await api.CreaUtenteAsync("responsabile.impianti@alfa.example", PasswordProva, "responsabile_tecnico");
        using (var client = api.Client(await api.TokenAsync(responsabile, PasswordProva)))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/impianti/{impianto.Id}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/impianti", nuovo, Ct)).StatusCode);
        }

        // Tecnico: solo gli impianti dei propri interventi, che arrivano nella prossima parte di M2.
        var tecnico = await api.CreaUtenteAsync("tecnico.impianti@alfa.example", PasswordProva, "tecnico");
        using (var client = api.Client(await api.TokenAsync(tecnico, PasswordProva)))
        {
            Assert.Equal(0, (await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>("/api/impianti", Ct))!.Totale);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/impianti/voci", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{impianto.Id}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/impianti/{impianto.Id}", new { sedeId, tipoImpiantoId = voci.TipiImpianto[0].Id, versione = 1 }, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/impianti", nuovo, Ct)).StatusCode);
        }

        // Sola lettura: vede tutto, non modifica nulla.
        var consulente = await api.CreaUtenteAsync("consulente.impianti@alfa.example", PasswordProva, "sola_lettura");
        using (var client = api.Client(await api.TokenAsync(consulente, PasswordProva)))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/impianti/{impianto.Id}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/impianti", nuovo, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/impianti/{impianto.Id}?versione=1", Ct)).StatusCode);
        }

        // Amministratore di sistema: nessun accesso ai dati di business.
        using (var client = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/impianti", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/impianti/{impianto.Id}", Ct)).StatusCode);
        }
    }

    private static async Task<VociImpianti> VociAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;

    private static async Task<(Guid ClienteId, Guid SedeId)> ClienteConSedeAsync(HttpClient client, string indirizzo, string civico)
    {
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Impianti", cognome = "Fittizio" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        return (cliente.Id, sede.Id);
    }

    /// <summary>Il messaggio per l'utente: nel JSON grezzo le lettere accentate sono codificate.</summary>
    private static async Task<string> DettaglioAsync(HttpResponseMessage risposta) =>
        (await risposta.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Ct))?.Detail ?? "";

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

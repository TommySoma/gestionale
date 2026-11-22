using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;

namespace Gestionale.Tests.Integration;

/// <summary>Anagrafiche dall'API al database: CRUD, validazione, concorrenza, consensi e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestClienti(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "chiave-inglese-da-quattordici";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_cliente_si_crea_si_legge_si_modifica_e_si_elimina()
    {
        using var client = api.Client(await api.TokenTitolareAsync());

        var creato = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "Privato", nome = " Mario ", cognome = "Rossi", codiceFiscale = "rssmra 80a01 h501u" }, Ct));
        Assert.Equal(1, creato.Versione);

        var letto = await client.GetFromJsonAsync<Cliente>($"/api/clienti/{creato.Id}", Ct);
        Assert.Equal("privato", letto!.Tipo);
        Assert.Equal("Mario", letto.Nome);
        Assert.Equal("RSSMRA80A01H501U", letto.CodiceFiscale);

        var aggiornato = await LeggiAsync<RispostaScrittura>(await client.PutAsJsonAsync($"/api/clienti/{creato.Id}",
            new { tipo = "privato", nome = "Mario", cognome = "Rossi", codiceFiscale = "RSSMRA80A01H501U", pec = "mario.rossi@pec.example", versione = 1 }, Ct));
        Assert.Equal(2, aggiornato.Versione);

        // Un collega con la scheda aperta da prima non sovrascrive la modifica.
        var superata = await client.PutAsJsonAsync($"/api/clienti/{creato.Id}",
            new { tipo = "privato", nome = "Marco", cognome = "Rossi", versione = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, superata.StatusCode);
        Assert.Contains("modificato da qualcun altro", await DettaglioAsync(superata), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/clienti/{creato.Id}", new { tipo = "privato", nome = "Mario", cognome = "Rossi" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/clienti/{creato.Id}?versione=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/clienti/{creato.Id}?versione=2", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/clienti/{creato.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/clienti/{creato.Id}", new { tipo = "privato", nome = "Mario", cognome = "Rossi", versione = 3 }, Ct)).StatusCode);
    }

    [Fact]
    public async Task I_dati_sbagliati_vengono_spiegati_tutti_insieme()
    {
        using var client = api.Client(await api.TokenTitolareAsync());

        var risposta = await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "azienda", codiceFiscale = "RSSMRA80A01H501A", partitaIva = "01234567890" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        var testo = await DettaglioAsync(risposta);
        Assert.Contains("serve la denominazione", testo, StringComparison.Ordinal);
        Assert.Contains("codice fiscale non è valido", testo, StringComparison.Ordinal);
        Assert.Contains("partita IVA non è valida", testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_stessa_partita_iva_non_può_avere_due_clienti()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var azienda = new { tipo = "azienda", denominazione = "Officina Unica Srl (fittizia)", partitaIva = "01234567897" };

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/clienti", azienda, Ct)).StatusCode);

        var doppione = await client.PostAsJsonAsync("/api/clienti", azienda, Ct);
        Assert.Equal(HttpStatusCode.Conflict, doppione.StatusCode);
        Assert.Contains("Esiste già un cliente con questa partita IVA", await DettaglioAsync(doppione), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sedi_e_contatti_si_gestiscono_dalla_scheda_del_cliente()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "condominio", denominazione = "Condominio Le Querce (fittizio)", codiceFiscale = "01234567897" }, Ct));

        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo = "Via dei Platani", civico = "12", cap = "50100", comune = "Firenze", provincia = "fi", noteAccesso = "Chiavi in portineria" }, Ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo = "Via Prova", cap = "501", comune = "Firenze", provincia = "FI" }, Ct)).StatusCode);

        var cellulare = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/contatti",
            new { tipo = "cellulare", valore = "+39 333 000 0000", etichetta = "Amministratore", principale = true }, Ct));
        var secondoPrincipale = await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/contatti",
            new { tipo = "cellulare", valore = "+39 333 111 1111", principale = true }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, secondoPrincipale.StatusCode);
        Assert.Contains("già un contatto principale", await DettaglioAsync(secondoPrincipale), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/clienti/{cliente.Id}/contatti/{cellulare.Id}",
            new { tipo = "cellulare", valore = "+39 333 222 2222", etichetta = "Amministratore", principale = true, versione = cellulare.Versione }, Ct)).StatusCode);

        var scheda = await client.GetFromJsonAsync<Cliente>($"/api/clienti/{cliente.Id}", Ct);
        Assert.Equal("FI", Assert.Single(scheda!.Sedi).Provincia);
        Assert.Equal("+39 333 222 2222", Assert.Single(scheda.Contatti).Valore);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/clienti/{cliente.Id}/contatti/{cellulare.Id}?versione=2", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/clienti/{cliente.Id}/sedi/{sede.Id}?versione=1", Ct)).StatusCode);

        scheda = await client.GetFromJsonAsync<Cliente>($"/api/clienti/{cliente.Id}", Ct);
        Assert.Empty(scheda!.Sedi);
        Assert.Empty(scheda.Contatti);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/clienti/{Guid.NewGuid()}/sedi",
            new { indirizzo = "Via Prova", cap = "50100", comune = "Firenze", provincia = "FI" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task I_consensi_formano_uno_storico_e_vale_l_ultimo()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Giulia", cognome = "Neri" }, Ct));
        var percorso = $"/api/clienti/{cliente.Id}/consensi";

        var finalita = await client.GetFromJsonAsync<List<FinalitaPrivacy>>("/api/finalita-privacy", Ct);
        Assert.Equal(["comunicazioni_commerciali", "promemoria_scadenze"], finalita!.Select(f => f.Codice).Order());

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(percorso, new { finalita = "comunicazioni_commerciali", concesso = true, fonte = "modulo_firmato" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(percorso, new { finalita = "comunicazioni_commerciali", concesso = false, fonte = "email", note = "Revoca via email" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(percorso, new { finalita = "promemoria_scadenze", concesso = true, fonte = "di_persona" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(percorso, new { finalita = "profilazione", concesso = true, fonte = "email" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(percorso, new { finalita = "promemoria_scadenze", concesso = true, fonte = "piccione" }, Ct)).StatusCode);

        var scheda = await client.GetFromJsonAsync<Cliente>($"/api/clienti/{cliente.Id}", Ct);
        Assert.Equal(
            [("comunicazioni_commerciali", false), ("promemoria_scadenze", true)],
            scheda!.Consensi.Select(c => (c.Finalita, c.Concesso)));

        var storico = await client.GetFromJsonAsync<List<VoceStoricoConsenso>>(percorso, Ct);
        Assert.Equal(3, storico!.Count);
        Assert.Equal("Revoca via email", storico.Single(v => v.Finalita == "comunicazioni_commerciali" && !v.Concesso).Note);
    }

    [Fact]
    public async Task La_ricerca_trova_per_nome_codice_e_tipo_e_divide_in_pagine()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        await client.PostAsJsonAsync("/api/clienti", new { tipo = "privato", nome = "Anna", cognome = "Bianchi Ricerca" }, Ct);
        await client.PostAsJsonAsync("/api/clienti", new { tipo = "condominio", denominazione = "Condominio Aurora Ricerca (fittizio)" }, Ct);
        await client.PostAsJsonAsync("/api/clienti", new { tipo = "azienda", denominazione = "Ricerca Impianti Srl (fittizia)", partitaIva = "00000000000" }, Ct);

        var perNome = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=aurora", Ct);
        Assert.Equal("Condominio Aurora Ricerca (fittizio)", Assert.Single(perNome!.Elementi).NomeVisualizzato);

        var perPartitaIva = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=00000000000", Ct);
        Assert.Equal("azienda", Assert.Single(perPartitaIva!.Elementi).Tipo);

        var perTipo = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=ricerca&tipo=privato", Ct);
        Assert.Equal("Bianchi Ricerca Anna", Assert.Single(perTipo!.Elementi).NomeVisualizzato);

        var primaPagina = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=ricerca&dimensione=2", Ct);
        Assert.Equal(3, primaPagina!.Totale);
        Assert.Equal(2, primaPagina.Elementi.Count);
        var secondaPagina = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=ricerca&dimensione=2&pagina=2", Ct);
        Assert.Single(secondaPagina!.Elementi);

        // I caratteri jolly di LIKE si cercano come testo, non selezionano tutto.
        var jolly = await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti?cerca=%25", Ct);
        Assert.Equal(0, jolly!.Totale);
    }

    [Fact]
    public async Task Ogni_ruolo_vede_e_modifica_solo_quello_che_gli_spetta()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var cliente = await LeggiAsync<RispostaScrittura>(await titolare.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Paolo", cognome = "Permessi" }, Ct));
        var nuovo = new { tipo = "privato", nome = "Nuovo", cognome = "Cliente" };

        // Tecnico: legge solo i propri clienti, che arriveranno con gli interventi (M2).
        var tecnico = await api.CreaUtenteAsync("tecnico.clienti@alfa.example", PasswordProva, "tecnico");
        using (var client = api.Client(await api.TokenAsync(tecnico, PasswordProva)))
        {
            Assert.Equal(0, (await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>("/api/clienti", Ct))!.Totale);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/clienti/{cliente.Id}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/clienti", nuovo, Ct)).StatusCode);
        }

        // Sola lettura: vede tutto, non modifica nulla.
        var consulente = await api.CreaUtenteAsync("consulente@alfa.example", PasswordProva, "sola_lettura");
        using (var client = api.Client(await api.TokenAsync(consulente, PasswordProva)))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/clienti/{cliente.Id}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/clienti", nuovo, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/clienti/{cliente.Id}?versione=1", Ct)).StatusCode);
        }

        // Amministratore di sistema: nessun accesso ai dati di business.
        using (var client = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/clienti", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/clienti/{cliente.Id}", Ct)).StatusCode);
        }
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

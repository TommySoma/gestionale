using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Application.Interventi;
using Npgsql;

namespace Gestionale.Tests.Integration;

/// <summary>Interventi dall'API al database: pianificazione, calendario, avvio, chiusura, annullamento e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestInterventi(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "chiave-inglese-da-quattordici";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_intervento_si_pianifica_si_avvia_e_si_chiude_con_le_righe()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var impiantoId = await ImpiantoAsync(client, "Via del Ciclo", "1");
        var tecnicoId = await UtenteAsync(await api.CreaUtenteAsync("tecnico.ciclo@alfa.example", PasswordProva, "tecnico"));

        var creato = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi",
            new { impiantoId, tipo = "Manutenzione", descrizione = "  Manutenzione annuale  " }, Ct));
        Assert.True(creato.Numero >= 1);
        var letto = (await client.GetFromJsonAsync<Intervento>($"/api/interventi/{creato.Id}", Ct))!;
        Assert.Equal(("da_pianificare", "manutenzione", "Manutenzione annuale"), (letto.Stato, letto.Tipo, letto.Descrizione));
        Assert.Null(letto.Inizio);

        // In calendario: finestra e tecnico.
        var pianificato = await LeggiAsync<RispostaScritturaIntervento>(await client.PutAsJsonAsync($"/api/interventi/{creato.Id}", new
        {
            impiantoId,
            tipo = "manutenzione",
            inizio = "2027-01-11T09:00:00+01:00",
            fine = "2027-01-11T11:00:00+01:00",
            tecnicoUtenteId = tecnicoId,
            versione = creato.Versione,
        }, Ct));
        Assert.Equal((creato.Anno, creato.Numero), (pianificato.Anno, pianificato.Numero));
        letto = (await client.GetFromJsonAsync<Intervento>($"/api/interventi/{creato.Id}", Ct))!;
        Assert.Equal("pianificato", letto.Stato);
        Assert.Equal(new DateTime(2027, 1, 11, 8, 0, 0, DateTimeKind.Utc), letto.Inizio);

        // Lo stesso tecnico non può essere in due posti insieme.
        var sovrapposto = await client.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId,
            tipo = "riparazione",
            inizio = "2027-01-11T10:00:00+01:00",
            fine = "2027-01-11T12:00:00+01:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, sovrapposto.StatusCode);
        Assert.Contains("fascia oraria", await DettaglioAsync(sovrapposto), StringComparison.Ordinal);

        // Il calendario della settimana lo mostra; quello della settimana dopo no.
        var settimana = (await client.GetFromJsonAsync<Pagina<RiepilogoIntervento>>(
            $"/api/interventi?da=2027-01-11T00:00:00%2B01:00&a=2027-01-18T00:00:00%2B01:00&tecnico={tecnicoId}", Ct))!;
        Assert.Equal(creato.Id, Assert.Single(settimana.Elementi).InterventoId);
        var dopo = (await client.GetFromJsonAsync<Pagina<RiepilogoIntervento>>(
            "/api/interventi?da=2027-01-18T00:00:00%2B01:00&a=2027-01-25T00:00:00%2B01:00", Ct))!;
        Assert.DoesNotContain(dopo.Elementi, i => i.InterventoId == creato.Id);

        var avviato = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync(
            $"/api/interventi/{creato.Id}/avvio", new { versione = pianificato.Versione }, Ct));

        // Una versione vecchia non chiude nulla.
        var superata = await client.PostAsJsonAsync($"/api/interventi/{creato.Id}/chiusura",
            new { versione = pianificato.Versione, esito = "risolto" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, superata.StatusCode);

        var chiuso = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync($"/api/interventi/{creato.Id}/chiusura", new
        {
            versione = avviato.Versione,
            esito = "risolto",
            note = "Pulizia del bruciatore",
            righe = new object[]
            {
                new { tipo = "manodopera", descrizione = "Manutenzione ordinaria", quantita = 1.5m, prezzoUnitario = 40m, aliquotaIva = 22m },
                new { tipo = "materiale", codice = "GUA-01", descrizione = "Guarnizione di prova", unita = "pz", quantita = 2, prezzoUnitario = 4.99m, scontoPercentuale = 10m },
            },
        }, Ct));
        letto = (await client.GetFromJsonAsync<Intervento>($"/api/interventi/{creato.Id}", Ct))!;
        Assert.Equal(("completato", "risolto"), (letto.Stato, letto.Esito));
        Assert.Equal(new[] { "h", "pz" }, letto.Righe.Select(r => r.Unita));
        Assert.Equal(68.98m, letto.Totale);
        Assert.NotNull(letto.ChiusoIl);

        // Il rapportino PDF c'è solo da chiuso.
        var rapportino = await client.GetAsync($"/api/interventi/{creato.Id}/rapportino", Ct);
        Assert.Equal(HttpStatusCode.OK, rapportino.StatusCode);
        Assert.Equal("application/pdf", rapportino.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"rapportino-{creato.Anno}-{creato.Numero:D4}.pdf", rapportino.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await rapportino.Content.ReadAsByteArrayAsync(Ct))[..4]));

        // Completato è finale.
        var annullaChiuso = await client.PostAsJsonAsync($"/api/interventi/{creato.Id}/annullamento",
            new { versione = chiuso.Versione, motivo = "Ripensamento" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, annullaChiuso.StatusCode);
        Assert.Contains("completato", await DettaglioAsync(annullaChiuso), StringComparison.Ordinal);
    }

    [Fact]
    public async Task I_dati_sbagliati_vengono_spiegati_tutti_insieme()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var impiantoId = await ImpiantoAsync(client, "Via degli Errori", "2");
        var consulente = await UtenteAsync(await api.CreaUtenteAsync("consulente.interventi@alfa.example", PasswordProva, "sola_lettura"));

        var risposta = await client.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId,
            tipo = "pulizia",
            inizio = "2027-02-01T12:00:00+01:00",
            fine = "2027-02-01T10:00:00+01:00",
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        var testo = await DettaglioAsync(risposta);
        Assert.Contains("il tipo deve essere", testo, StringComparison.Ordinal);
        Assert.Contains("la fine deve essere dopo l'inizio", testo, StringComparison.Ordinal);
        Assert.Contains("serve il tecnico", testo, StringComparison.Ordinal);

        // Le regole di forma passano, ma impianto e tecnico non vanno bene.
        risposta = await client.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId = Guid.NewGuid(),
            tipo = "riparazione",
            inizio = "2027-02-01T10:00:00+01:00",
            fine = "2027-02-01T12:00:00+01:00",
            tecnicoUtenteId = consulente,
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        testo = await DettaglioAsync(risposta);
        Assert.Contains("l'impianto indicato non esiste", testo, StringComparison.Ordinal);
        Assert.Contains("non può ricevere interventi", testo, StringComparison.Ordinal);

        var creato = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi",
            new { impiantoId, tipo = "sopralluogo" }, Ct));
        var rapportinoAperto = await client.GetAsync($"/api/interventi/{creato.Id}/rapportino", Ct);
        Assert.Equal(HttpStatusCode.Conflict, rapportinoAperto.StatusCode);
        Assert.Contains("solo per un intervento completato", await DettaglioAsync(rapportinoAperto), StringComparison.Ordinal);

        var chiusuraSenzaData = await client.PostAsJsonAsync($"/api/interventi/{creato.Id}/chiusura",
            new { versione = creato.Versione, esito = "risolto" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, chiusuraSenzaData.StatusCode);

        var righeSbagliate = await client.PostAsJsonAsync($"/api/interventi/{creato.Id}/chiusura", new
        {
            versione = creato.Versione,
            esito = "boh",
            righe = new object[] { new { tipo = "manodopera", descrizione = "Ore", unita = "pz", quantita = 0 } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, righeSbagliate.StatusCode);
        testo = await DettaglioAsync(righeSbagliate);
        Assert.Contains("l'esito deve essere", testo, StringComparison.Ordinal);
        Assert.Contains("in ore", testo, StringComparison.Ordinal);
        Assert.Contains("quantità", testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_intervento_annullato_ha_un_motivo_e_libera_il_calendario()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var impiantoId = await ImpiantoAsync(client, "Via degli Annullati", "3");
        var tecnicoId = await UtenteAsync(await api.CreaUtenteAsync("tecnico.annullati@alfa.example", PasswordProva, "tecnico"));
        var fascia = new { impiantoId, tipo = "riparazione", inizio = "2027-03-01T09:00:00+01:00", fine = "2027-03-01T10:00:00+01:00", tecnicoUtenteId = tecnicoId };

        var primo = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi", fascia, Ct));

        var senzaMotivo = await client.PostAsJsonAsync($"/api/interventi/{primo.Id}/annullamento", new { versione = primo.Versione, motivo = " " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, senzaMotivo.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/interventi/{primo.Id}/annullamento",
            new { versione = primo.Versione, motivo = "Il cliente ha rinviato" }, Ct)).StatusCode);
        var letto = (await client.GetFromJsonAsync<Intervento>($"/api/interventi/{primo.Id}", Ct))!;
        Assert.Equal(("annullato", "Il cliente ha rinviato"), (letto.Stato, letto.MotivoAnnullamento));

        var secondo = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi", fascia, Ct));
        Assert.Equal(primo.Numero + 1, secondo.Numero);
    }

    [Fact]
    public async Task Il_tecnico_vede_avvia_e_chiude_solo_i_propri_e_ne_vede_gli_impianti()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var suo = await ImpiantoAsync(titolare, "Via del Tecnico", "4");
        var altro = await ImpiantoAsync(titolare, "Via del Collega", "5");
        var email = await api.CreaUtenteAsync("tecnico.propri@alfa.example", PasswordProva, "tecnico");
        var tecnicoId = await UtenteAsync(email);
        var collegaId = await UtenteAsync(await api.CreaUtenteAsync("collega.propri@alfa.example", PasswordProva, "tecnico"));

        var assegnato = await LeggiAsync<RispostaScritturaIntervento>(await titolare.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId = suo,
            tipo = "manutenzione",
            inizio = "2027-04-01T09:00:00+02:00",
            fine = "2027-04-01T11:00:00+02:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct));
        var delCollega = await LeggiAsync<RispostaScritturaIntervento>(await titolare.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId = altro,
            tipo = "manutenzione",
            inizio = "2027-04-01T09:00:00+02:00",
            fine = "2027-04-01T11:00:00+02:00",
            tecnicoUtenteId = collegaId,
        }, Ct));

        using var client = api.Client(await api.TokenAsync(email, PasswordProva));

        // Chiedere gli interventi del collega non serve: vede comunque solo i propri.
        var elenco = (await client.GetFromJsonAsync<Pagina<RiepilogoIntervento>>($"/api/interventi?tecnico={collegaId}", Ct))!;
        Assert.Equal(assegnato.Id, Assert.Single(elenco.Elementi).InterventoId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/interventi/{delCollega.Id}", Ct)).StatusCode);

        // Gli impianti: solo quello del proprio intervento.
        var impianti = (await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>("/api/impianti", Ct))!;
        Assert.Equal(suo, Assert.Single(impianti.Elementi).ImpiantoId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/impianti/{suo}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{altro}", Ct)).StatusCode);

        // Pianificare, creare e annullare spetta all'ufficio.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/interventi", new { impiantoId = suo, tipo = "altro" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/interventi/{assegnato.Id}",
            new { impiantoId = suo, tipo = "altro", versione = assegnato.Versione }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/interventi/{assegnato.Id}/annullamento",
            new { versione = assegnato.Versione, motivo = "No" }, Ct)).StatusCode);

        // Il proprio lo avvia e lo chiude; quello del collega no.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/interventi/{delCollega.Id}/avvio",
            new { versione = delCollega.Versione }, Ct)).StatusCode);
        var avviato = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync(
            $"/api/interventi/{assegnato.Id}/avvio", new { versione = assegnato.Versione }, Ct));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/interventi/{assegnato.Id}/chiusura",
            new { versione = avviato.Versione, esito = "da_completare", note = "Manca un ricambio" }, Ct)).StatusCode);

        // Il calendario dei tecnici assegnabili comprende entrambi.
        var tecnici = (await titolare.GetFromJsonAsync<List<TecnicoAssegnabile>>("/api/interventi/tecnici", Ct))!;
        Assert.Contains(tecnici, t => t.UtenteId == tecnicoId);
        Assert.Contains(tecnici, t => t.UtenteId == collegaId);

        // Amministratore di sistema: nessun accesso.
        using var amministratore = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore));
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/interventi", Ct)).StatusCode);
    }

    private async Task<Guid> UtenteAsync(string email)
    {
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("SELECT utente_id FROM sec.utenti WHERE lower(email) = lower(@email)", connessione);
        comando.Parameters.AddWithValue("email", email);
        return (Guid)(await comando.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<Guid> ImpiantoAsync(HttpClient client, string indirizzo, string civico)
    {
        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Interventi", cognome = "Fittizio" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto[0].Id }, Ct));
        return impianto.Id;
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

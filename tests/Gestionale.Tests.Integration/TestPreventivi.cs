using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Commerciale;
using Gestionale.Application.Impianti;
using Gestionale.Application.Interventi;

namespace Gestionale.Tests.Integration;

/// <summary>Listino e preventivi dall'API al database: prezzi con storico, bozza, emissione, accettazione, intervento, PDF e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestPreventivi(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "preventivo-da-firmare";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Il_listino_tiene_lo_storico_dei_prezzi()
    {
        using var client = api.Client(await api.TokenTitolareAsync());

        var voce = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync("/api/listino", new
        {
            codice = " fil-01 ",
            descrizione = "Filtro di prova",
            tipo = "materiale",
            unita = "pz",
            aliquotaIva = 22m,
            prezzoIniziale = 12.50m,
            validoDal = "2026-01-01",
        }, Ct));

        var doppione = await client.PostAsJsonAsync("/api/listino", new { codice = "FIL-01", descrizione = "Doppione", tipo = "materiale", unita = "pz" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, doppione.StatusCode);
        Assert.Contains("questo codice", await DettaglioAsync(doppione), StringComparison.Ordinal);

        // Un prezzo nuovo nel futuro non cambia quello di oggi.
        await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync($"/api/listino/{voce.Id}/prezzi",
            new { prezzo = 14m, validoDal = "2099-01-01" }, Ct));
        var stessaData = await client.PostAsJsonAsync($"/api/listino/{voce.Id}/prezzi", new { prezzo = 15m, validoDal = "2099-01-01" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stessaData.StatusCode);

        var letta = (await client.GetFromJsonAsync<VoceListino>($"/api/listino/{voce.Id}", Ct))!;
        Assert.Equal(("FIL-01", 12.50m), (letta.Codice, letta.Prezzo));
        Assert.Equal([new DateOnly(2099, 1, 1), new DateOnly(2026, 1, 1)], letta.Prezzi.Select(p => p.ValidoDal));

        // Disattivata, sparisce dalla ricerca predefinita ma non dall'elenco completo.
        await LeggiAsync<RispostaScritturaCommerciale>(await client.PutAsJsonAsync($"/api/listino/{voce.Id}", new
        {
            codice = "FIL-01",
            descrizione = "Filtro di prova",
            tipo = "materiale",
            unita = "pz",
            aliquotaIva = 22m,
            attiva = false,
            versione = letta.Versione,
        }, Ct));
        var attive = (await client.GetFromJsonAsync<Pagina<VoceListino>>("/api/listino?cerca=fil", Ct))!;
        Assert.DoesNotContain(attive.Elementi, v => v.VoceId == voce.Id);
        var tutte = (await client.GetFromJsonAsync<Pagina<VoceListino>>("/api/listino?cerca=fil&tutte=true", Ct))!;
        Assert.Contains(tutte.Elementi, v => v.VoceId == voce.Id);

        var sbagliata = await client.PostAsJsonAsync("/api/listino", new { codice = "MAN 01", tipo = "manodopera", unita = "pz" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sbagliata.StatusCode);
        var testo = await DettaglioAsync(sbagliata);
        Assert.Contains("né contenere spazi", testo, StringComparison.Ordinal);
        Assert.Contains("in ore", testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dalla_bozza_all_intervento()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var (clienteId, sedeId, impiantoId) = await ImpiantoAsync(client, "Via dei Preventivi", "1");

        var bozza = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync("/api/preventivi", new
        {
            clienteId,
            sedeId,
            impiantoId,
            oggetto = "  Sostituzione del vaso di espansione  ",
            righe = new object[]
            {
                new { tipo = "manodopera", descrizione = "Manodopera tecnico", quantita = 1.5m, prezzoUnitario = 45m, aliquotaIva = 22m },
                new { tipo = "materiale", codice = "VAS-08", descrizione = "Vaso di espansione (prova)", unita = "pz", quantita = 1, prezzoUnitario = 90m, aliquotaIva = 22m },
            },
        }, Ct));
        Assert.Null(bozza.Numero);

        var letto = (await client.GetFromJsonAsync<Preventivo>($"/api/preventivi/{bozza.Id}", Ct))!;
        Assert.Equal(("bozza", "Sostituzione del vaso di espansione", 30), (letto.Stato, letto.Oggetto, letto.ValiditaGiorni));
        Assert.Equal((157.50m, 34.65m, 192.15m), (letto.Imponibile, letto.Iva, letto.Totale));
        Assert.Equal(["h", "pz"], letto.Righe.Select(r => r.Unita));

        // La bozza si modifica: le righe si sostituiscono.
        var modificata = await LeggiAsync<RispostaScritturaCommerciale>(await client.PutAsJsonAsync($"/api/preventivi/{bozza.Id}", new
        {
            clienteId,
            sedeId,
            impiantoId,
            oggetto = "Sostituzione del vaso di espansione",
            validitaGiorni = 15,
            righe = new object[] { new { tipo = "materiale", descrizione = "Vaso di espansione (prova)", unita = "pz", quantita = 1, prezzoUnitario = 90m, aliquotaIva = 22m } },
            versione = letto.Versione,
        }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/preventivi/{bozza.Id}", new
        {
            clienteId,
            oggetto = "Versione vecchia",
            versione = letto.Versione,
        }, Ct)).StatusCode);

        // Il PDF della bozza c'è già, senza numero.
        var pdfBozza = await client.GetAsync($"/api/preventivi/{bozza.Id}/pdf", Ct);
        Assert.Equal("bozza-di-preventivo.pdf", pdfBozza.Content.Headers.ContentDisposition?.FileName);

        var emesso = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync(
            $"/api/preventivi/{bozza.Id}/emissione", new { versione = modificata.Versione }, Ct));
        Assert.NotNull(emesso.Numero);
        letto = (await client.GetFromJsonAsync<Preventivo>($"/api/preventivi/{bozza.Id}", Ct))!;
        Assert.Equal(("emesso", 15, 109.80m), (letto.Stato, letto.ScadeIl!.Value.DayNumber - letto.EmessoIl!.Value.DayNumber, letto.Totale));

        // Emesso: non cambia, non si elimina.
        var modificaEmesso = await client.PutAsJsonAsync($"/api/preventivi/{bozza.Id}", new { clienteId, oggetto = "Ripensamento", versione = emesso.Versione }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, modificaEmesso.StatusCode);
        Assert.Contains("emesso", await DettaglioAsync(modificaEmesso), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/preventivi/{bozza.Id}?versione={emesso.Versione}", Ct)).StatusCode);

        var pdf = await client.GetAsync($"/api/preventivi/{bozza.Id}/pdf", Ct);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"preventivo-{emesso.Anno}-{emesso.Numero:D4}.pdf", pdf.Content.Headers.ContentDisposition?.FileName);

        // Accettato: genera l'intervento da pianificare, una volta sola.
        var accettato = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync(
            $"/api/preventivi/{bozza.Id}/accettazione", new { versione = emesso.Versione }, Ct));
        var senzaTipo = await client.PostAsJsonAsync($"/api/preventivi/{bozza.Id}/intervento", new { versione = accettato.Versione }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, senzaTipo.StatusCode);

        var convertito = await client.PostAsJsonAsync($"/api/preventivi/{bozza.Id}/intervento", new { versione = accettato.Versione, tipo = "riparazione" }, Ct);
        Assert.Equal(HttpStatusCode.Created, convertito.StatusCode);
        var esito = (await convertito.Content.ReadFromJsonAsync<RispostaScritturaCommerciale>(Ct))!;
        var intervento = (await client.GetFromJsonAsync<Intervento>($"/api/interventi/{esito.InterventoId}", Ct))!;
        Assert.Equal(("da_pianificare", "riparazione", impiantoId), (intervento.Stato, intervento.Tipo, intervento.ImpiantoId));
        Assert.Equal(90m, Assert.Single(intervento.Righe).PrezzoUnitario);

        var secondo = await client.PostAsJsonAsync($"/api/preventivi/{bozza.Id}/intervento", new { versione = esito.Versione, tipo = "riparazione" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, secondo.StatusCode);
        Assert.Contains("ha già generato", await DettaglioAsync(secondo), StringComparison.Ordinal);

        var elenco = (await client.GetFromJsonAsync<Pagina<RiepilogoPreventivo>>($"/api/preventivi?cliente={clienteId}", Ct))!;
        Assert.Equal(esito.InterventoId, Assert.Single(elenco.Elementi).InterventoId);
    }

    [Fact]
    public async Task Bozze_annullamenti_e_numerazione()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var (clienteId, _, impiantoAltro) = await ImpiantoAsync(client, "Via delle Bozze", "2");
        var (altroCliente, _, _) = await ImpiantoAsync(client, "Via degli Altri", "3");
        var riga = new object[] { new { tipo = "materiale", descrizione = "Ricambio (prova)", unita = "pz", quantita = 1, prezzoUnitario = 10m, aliquotaIva = 22m } };

        // L'impianto di un altro cliente non va bene.
        var sbagliato = await client.PostAsJsonAsync("/api/preventivi", new { clienteId = altroCliente, impiantoId = impiantoAltro, oggetto = "Prova", righe = riga }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sbagliato.StatusCode);
        Assert.Contains("non è del cliente", await DettaglioAsync(sbagliato), StringComparison.Ordinal);

        // Senza righe non si emette; una bozza si elimina.
        var vuota = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync("/api/preventivi", new { clienteId, oggetto = "Vuoto" }, Ct));
        var emissioneVuota = await client.PostAsJsonAsync($"/api/preventivi/{vuota.Id}/emissione", new { versione = vuota.Versione }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, emissioneVuota.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/preventivi/{vuota.Id}?versione={vuota.Versione}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/preventivi/{vuota.Id}", Ct)).StatusCode);

        // Due emissioni di fila: numeri consecutivi, la bozza eliminata non ne ha consumati.
        var primo = await EmessoAsync(client, clienteId, riga);
        var secondo = await EmessoAsync(client, clienteId, riga);
        Assert.Equal(primo.Numero + 1, secondo.Numero);

        // Annullare vuole il motivo; un annullato è finale.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/preventivi/{primo.Id}/annullamento",
            new { versione = primo.Versione, motivo = " " }, Ct)).StatusCode);
        var annullato = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync($"/api/preventivi/{primo.Id}/annullamento",
            new { versione = primo.Versione, motivo = "Il cliente ha cambiato idea" }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/preventivi/{primo.Id}/accettazione",
            new { versione = annullato.Versione }, Ct)).StatusCode);

        // Un rifiutato non genera interventi; senza impianto nemmeno un accettato.
        var rifiutato = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync($"/api/preventivi/{secondo.Id}/rifiuto",
            new { versione = secondo.Versione }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/preventivi/{secondo.Id}/intervento",
            new { versione = rifiutato.Versione, tipo = "riparazione" }, Ct)).StatusCode);

        var terzo = await EmessoAsync(client, clienteId, riga);
        var accettato = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync($"/api/preventivi/{terzo.Id}/accettazione",
            new { versione = terzo.Versione }, Ct));
        var senzaImpianto = await client.PostAsJsonAsync($"/api/preventivi/{terzo.Id}/intervento", new { versione = accettato.Versione, tipo = "riparazione" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, senzaImpianto.StatusCode);
        Assert.Contains("deve indicare l'impianto", await DettaglioAsync(senzaImpianto), StringComparison.Ordinal);

        var annullati = (await client.GetFromJsonAsync<Pagina<RiepilogoPreventivo>>($"/api/preventivi?cliente={clienteId}&stato=annullato", Ct))!;
        Assert.Equal(primo.Id, Assert.Single(annullati.Elementi).PreventivoId);
    }

    [Fact]
    public async Task Permessi_di_listino_e_preventivi()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var (clienteId, _, _) = await ImpiantoAsync(titolare, "Via dei Permessi", "4");
        var preventivo = await LeggiAsync<RispostaScritturaCommerciale>(await titolare.PostAsJsonAsync("/api/preventivi", new { clienteId, oggetto = "Permessi (prova)" }, Ct));

        // Il Tecnico legge il listino, ma non vede i preventivi.
        using var tecnico = api.Client(await api.TokenAsync(await api.CreaUtenteAsync("tecnico.preventivi@alfa.example", PasswordProva, "tecnico"), PasswordProva));
        Assert.Equal(HttpStatusCode.OK, (await tecnico.GetAsync("/api/listino", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync("/api/listino", new { codice = "X", descrizione = "X", tipo = "materiale", unita = "pz" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.GetAsync("/api/preventivi", Ct)).StatusCode);

        // Il Responsabile tecnico legge i preventivi ma non li scrive.
        using var responsabile = api.Client(await api.TokenAsync(
            await api.CreaUtenteAsync("responsabile.preventivi@alfa.example", PasswordProva, "responsabile_tecnico"), PasswordProva));
        Assert.Equal(HttpStatusCode.OK, (await responsabile.GetAsync($"/api/preventivi/{preventivo.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await responsabile.DeleteAsync($"/api/preventivi/{preventivo.Id}?versione={preventivo.Versione}", Ct)).StatusCode);

        // Amministratore di sistema: nessun accesso.
        using var amministratore = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore));
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/preventivi", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/listino", Ct)).StatusCode);
    }

    private static async Task<RispostaScritturaCommerciale> EmessoAsync(HttpClient client, Guid clienteId, object[] righe)
    {
        var bozza = await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync("/api/preventivi", new { clienteId, oggetto = "Ricambio (prova)", righe }, Ct));
        return await LeggiAsync<RispostaScritturaCommerciale>(await client.PostAsJsonAsync($"/api/preventivi/{bozza.Id}/emissione", new { versione = bozza.Versione }, Ct));
    }

    private static async Task<(Guid Cliente, Guid Sede, Guid Impianto)> ImpiantoAsync(HttpClient client, string indirizzo, string civico)
    {
        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Preventivi", cognome = "Fittizio" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto[0].Id }, Ct));
        return (cliente.Id, sede.Id, impianto.Id);
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

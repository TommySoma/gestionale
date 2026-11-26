using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Application.Rapporti;
using Gestionale.Domain.Rapporti;
using Npgsql;

namespace Gestionale.Tests.Integration;

/// <summary>
/// Rapporti di controllo dall'API al database: bozza precompilata, dati contro il
/// tracciato SIERT, bollini, convalida, firma, file di import ed esiti del report,
/// permessi del Tecnico.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestRapportiControllo(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "rapporto-col-bollino";
    private const int CodiceDitta = 4321;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Ieri => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    [Fact]
    public async Task Dal_rapporto_al_file_di_invio()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var (impiantoId, generatoreId) = await ImpiantoAsync(client, "Via dei Rapporti", "7", "0123456781");

        var tracciato = (await client.GetFromJsonAsync<List<CampoTracciato>>("/api/rapporti-controllo/tracciato/tipo_1", Ct))!;
        Assert.Equal(67, tracciato.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/rapporti-controllo/tracciato/tipo_9", Ct)).StatusCode);

        // La bozza nasce precompilata dalle anagrafiche.
        var bozza = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync("/api/rapporti-controllo",
            new { impiantoId, generatoreId, modello = "tipo_1", dataControllo = Ieri }, Ct));
        var letto = (await client.GetFromJsonAsync<RapportoControllo>($"/api/rapporti-controllo/{bozza.Id}", Ct))!;
        Assert.Equal(("0123456781", "048017", "GT01"), (Testo(letto.Dati, "codice_catasto"), Testo(letto.Dati, "comune"), Testo(letto.Dati, "gruppo")));
        Assert.Contains(letto.Errori, e => e is { Campo: "NUMERO_BOLLINO", Bloccante: true });

        // Una chiave fuori dal tracciato è rifiutata, con il suo nome.
        var dati = Completa(letto.Dati);
        var estranea = JsonNode.Parse(dati.ToJsonString())!.AsObject();
        estranea["colore_caldaia"] = "rosso";
        var rifiutata = await client.PutAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}",
            new { dataControllo = Ieri, dati = estranea, versione = letto.Versione }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rifiutata.StatusCode);
        Assert.Contains("colore_caldaia", await DettaglioAsync(rifiutata), StringComparison.Ordinal);

        var aggiornata = await LeggiAsync<RispostaScritturaRapporto>(await client.PutAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}",
            new { dataControllo = Ieri, dati, versione = letto.Versione }, Ct));

        // Senza bollino non si convalida.
        var senzaBollino = await client.PostAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}/convalida", new { versione = aggiornata.Versione }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, senzaBollino.StatusCode);
        Assert.Contains("NUMERO_BOLLINO", await DettaglioAsync(senzaBollino), StringComparison.Ordinal);

        // Bollini: registrati in blocco, niente doppioni, il tipo giusto per il modello.
        var tipi = (await client.GetFromJsonAsync<List<TipoBollino>>("/api/bollini/tipi", Ct))!;
        var gt = tipi.Single(t => t.Codice == "gt_fino_35");
        var gf = tipi.Single(t => t.Codice == "gf_fino_35");
        var registrati = await LeggiAsync<RispostaBolliniRegistrati>(await client.PostAsJsonAsync("/api/bollini",
            new { tipoBollinoId = gt.TipoBollinoId, acquistatoIl = "2026-01-15", codici = new[] { "2026000000201", " 2026000000202 " } }, Ct));
        Assert.Equal(2, registrati.Numero);
        var doppione = await client.PostAsJsonAsync("/api/bollini",
            new { tipoBollinoId = gt.TipoBollinoId, acquistatoIl = "2026-01-15", codici = new[] { "2026000000202" } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, doppione.StatusCode);
        Assert.Contains("2026000000202", await DettaglioAsync(doppione), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}/bollino",
            new { tipoBollinoId = gf.TipoBollinoId, versione = aggiornata.Versione }, Ct)).StatusCode);

        var conBollino = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}/bollino",
            new { tipoBollinoId = gt.TipoBollinoId, versione = aggiornata.Versione }, Ct));
        var pronto = (await client.GetFromJsonAsync<RapportoControllo>($"/api/rapporti-controllo/{bozza.Id}", Ct))!;
        Assert.Equal("2026000000201", pronto.CodiceBollino);
        Assert.DoesNotContain(pronto.Errori, e => e.Bloccante);

        var convalidato = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync(
            $"/api/rapporti-controllo/{bozza.Id}/convalida", new { versione = conBollino.Versione }, Ct));
        var firmato = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync(
            $"/api/rapporti-controllo/{bozza.Id}/firma", new { versione = convalidato.Versione }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/rapporti-controllo/{bozza.Id}",
            new { dataControllo = Ieri, dati, versione = firmato.Versione }, Ct)).StatusCode);

        // Il file: 67 colonne a tabulazioni, CRLF, UTF-8 senza BOM.
        var invio = await LeggiAsync<RispostaInvioPreparato>(await client.PostAsJsonAsync("/api/invii-catasto", new { modello = "tipo_1" }, Ct));
        Assert.Equal(1, invio.Righe);
        var file = await client.GetAsync($"/api/invii-catasto/{invio.Id}/file", Ct);
        var byteFile = await file.Content.ReadAsByteArrayAsync(Ct);
        Assert.NotEqual(0xEF, byteFile[0]);
        var testo = Encoding.UTF8.GetString(byteFile);
        Assert.EndsWith("\r\n", testo, StringComparison.Ordinal);
        var colonne = testo.TrimEnd('\r', '\n').Split('\t');
        Assert.Equal(67, colonne.Length);
        Assert.Equal(("0123456781", CodiceDitta.ToString(System.Globalization.CultureInfo.InvariantCulture), "2026000000201"),
            (colonne[0], colonne[10], colonne[66]));
        Assert.Equal(Ieri.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), colonne[62]);

        // Un secondo file non riprende lo stesso rapporto.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/invii-catasto", new { modello = "tipo_1" }, Ct)).StatusCode);

        // Caricato e scartato dal SIERT, il rapporto entra in un invio nuovo; poi è trasmesso.
        var dettaglio = (await client.GetFromJsonAsync<InvioCatasto>($"/api/invii-catasto/{invio.Id}", Ct))!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/invii-catasto/{invio.Id}/esiti",
            new { versione = dettaglio.Invio.Versione, esiti = new[] { new { rapportoControlloId = bozza.Id, esito = "scartato", causa = "Prova" } } }, Ct)).StatusCode);
        var caricato = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync(
            $"/api/invii-catasto/{invio.Id}/caricamento", new { versione = dettaglio.Invio.Versione }, Ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/invii-catasto/{invio.Id}/esiti",
            new { versione = caricato.Versione, esiti = new[] { new { rapportoControlloId = bozza.Id, esito = "scartato" } } }, Ct)).StatusCode);
        await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync($"/api/invii-catasto/{invio.Id}/esiti",
            new { versione = caricato.Versione, esiti = new[] { new { rapportoControlloId = bozza.Id, esito = "scartato", causa = "Generatore non trovato sul CIT" } } }, Ct));
        Assert.Equal(StatiRapporto.Scartato, (await client.GetFromJsonAsync<RapportoControllo>($"/api/rapporti-controllo/{bozza.Id}", Ct))!.Stato);

        var reinvio = await LeggiAsync<RispostaInvioPreparato>(await client.PostAsJsonAsync("/api/invii-catasto", new { modello = "tipo_1" }, Ct));
        var reinvioCaricato = await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync(
            $"/api/invii-catasto/{reinvio.Id}/caricamento", new { versione = reinvio.Versione }, Ct));
        await LeggiAsync<RispostaScritturaRapporto>(await client.PostAsJsonAsync($"/api/invii-catasto/{reinvio.Id}/esiti",
            new { versione = reinvioCaricato.Versione, esiti = new[] { new { rapportoControlloId = bozza.Id, esito = "importato" } } }, Ct));
        Assert.Equal(StatiRapporto.Trasmesso, (await client.GetFromJsonAsync<RapportoControllo>($"/api/rapporti-controllo/{bozza.Id}", Ct))!.Stato);

        var bollini = (await client.GetFromJsonAsync<Pagina<Bollino>>("/api/bollini?codice=2026000000", Ct))!;
        Assert.Equal([StatiBollino.Usato, StatiBollino.Disponibile], bollini.Elementi.Select(b => b.Stato));
    }

    [Fact]
    public async Task Il_tecnico_compila_i_propri_rapporti_ma_non_li_firma()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var (impiantoId, generatoreId) = await ImpiantoAsync(titolare, "Via del Tecnico", "8", "0123456782");
        var email = "tecnico.rapporti@alfa.example";
        await api.CreaUtenteAsync(email, PasswordProva, "tecnico");
        var tecnicoId = await UtenteAsync(email);

        var intervento = await LeggiAsync<RispostaScritturaIntervento>(await titolare.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId,
            generatoreId,
            tipo = "manutenzione",
            descrizione = "Controllo di efficienza (prova)",
            inizio = DateTimeOffset.UtcNow.AddHours(-3),
            fine = DateTimeOffset.UtcNow.AddHours(-2),
            tecnicoUtenteId = tecnicoId,
        }, Ct));

        using var tecnico = api.Client(await api.TokenAsync(email, PasswordProva));

        // Senza intervento no; dal proprio intervento sì, con il suo nome come manutentore.
        Assert.Equal(HttpStatusCode.BadRequest, (await tecnico.PostAsJsonAsync("/api/rapporti-controllo",
            new { impiantoId, generatoreId, modello = "tipo_1", dataControllo = Ieri }, Ct)).StatusCode);
        var rapporto = await LeggiAsync<RispostaScritturaRapporto>(await tecnico.PostAsJsonAsync("/api/rapporti-controllo",
            new { impiantoId, generatoreId, interventoId = intervento.Id, modello = "tipo_1", dataControllo = Ieri }, Ct));
        var letto = (await tecnico.GetFromJsonAsync<RapportoControllo>($"/api/rapporti-controllo/{rapporto.Id}", Ct))!;
        Assert.Equal(tecnicoId, letto.TecnicoId);
        Assert.NotNull(Testo(letto.Dati, "manutentore"));

        // Il titolare crea un rapporto senza intervento: il tecnico non lo vede.
        var altro = await LeggiAsync<RispostaScritturaRapporto>(await titolare.PostAsJsonAsync("/api/rapporti-controllo",
            new { impiantoId, generatoreId, modello = "tipo_1", dataControllo = Ieri }, Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await tecnico.GetAsync($"/api/rapporti-controllo/{altro.Id}", Ct)).StatusCode);
        var elenco = (await tecnico.GetFromJsonAsync<Pagina<RiepilogoRapporto>>($"/api/rapporti-controllo?impianto={impiantoId}", Ct))!;
        Assert.Equal(rapporto.Id, Assert.Single(elenco.Elementi).RapportoControlloId);

        // Non firma, non vede gli invii, non registra bollini.
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync($"/api/rapporti-controllo/{rapporto.Id}/firma",
            new { versione = letto.Versione }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.GetAsync("/api/invii-catasto", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync("/api/bollini",
            new { codici = new[] { "2026000000299" } }, Ct)).StatusCode);

        // Annulla la propria bozza, con il motivo.
        Assert.Equal(HttpStatusCode.BadRequest, (await tecnico.PostAsJsonAsync($"/api/rapporti-controllo/{rapporto.Id}/annullamento",
            new { versione = letto.Versione }, Ct)).StatusCode);
        await LeggiAsync<RispostaScritturaRapporto>(await tecnico.PostAsJsonAsync($"/api/rapporti-controllo/{rapporto.Id}/annullamento",
            new { versione = letto.Versione, motivo = "Rapporto aperto per errore" }, Ct));

        // Amministratore di sistema: nessun accesso.
        using var amministratore = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore));
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/rapporti-controllo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/bollini/tipi", Ct)).StatusCode);
    }

    // Completa i dati precompilati con i valori di un controllo regolare, nella codifica del tracciato.
    private static JsonObject Completa(JsonObject precompilati)
    {
        var dati = JsonNode.Parse(precompilati.ToJsonString())!.AsObject();
        var valori = new Dictionary<string, JsonNode?>
        {
            ["responsabile_impianto"] = "Rapporti Fittizio",
            ["codice_fiscale"] = "FTTRPP80A01D612X",
            ["titolo_responsabile"] = "PROPRIETARIO",
            ["dichiarazione_conformita"] = "3",
            ["libretto_uso_manutenzione"] = "3",
            ["libretto_climatizzazione"] = "3",
            ["libretto_compilato_correttamente"] = "3",
            ["trattamento_riscaldamento"] = "17",
            ["trattamento_acs"] = "16",
            ["locale_idoneo"] = "16",
            ["generatore_idoneo_installazione_esterna"] = "3",
            ["aperture_ventilazione_libere"] = "9",
            ["aperture_ventilazione_adeguate"] = "9",
            ["canale_fumo_idoneo"] = "3",
            ["regolazione_temperatura_funzionante"] = "3",
            ["assenza_perdite_combustibile"] = "16",
            ["idonea_tenuta_impianto"] = "3",
            ["data_installazione"] = "2018-03-15",
            ["fabbricante"] = "Marca di prova",
            ["modello"] = "Modello di prova",
            ["matricola"] = "PROVA-0001",
            ["tipo_generatore"] = "GRUPPO TERMICO SINGOLO",
            ["potenza_totale"] = 24.5m,
            ["potenza_focolare"] = 25.8m,
            ["potenza_utile"] = 24.5m,
            ["climatizzazione_invernale"] = "SI",
            ["produzione_acs"] = "SI",
            ["combustibile"] = "Gas naturale",
            ["evacuazione_fumi"] = "Forzata",
            ["dispositivi_comando_funzionanti"] = "3",
            ["dispositivi_sicurezza_integri"] = "3",
            ["valvola_sicurezza"] = "3",
            ["pulizia_scambiatore"] = "3",
            ["presenza_riflussi"] = "4",
            ["risultati_uni_10389"] = "3",
            ["temperatura_fumi"] = 68.2m,
            ["temperatura_aria_comburente"] = 18.5m,
            ["o2"] = 3.1m,
            ["co2"] = 9.8m,
            ["co"] = 12,
            ["rendimento_combustione"] = 97.4m,
            ["funziona_in_sicurezza"] = "21",
            ["manutentore"] = "Rossi Mario",
        };

        foreach (var (chiave, valore) in valori)
        {
            dati[chiave] = valore;
        }

        return dati;
    }

    private static string? Testo(JsonObject dati, string chiave) => dati[chiave]?.GetValue<string>();

    // Cliente, sede con codice ISTAT, impianto con codice catasto, generatore con la sigla; codice ditta all'azienda.
    private async Task<(Guid Impianto, Guid Generatore)> ImpiantoAsync(HttpClient client, string indirizzo, string civico, string codiceCatasto)
    {
        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Fittizio", cognome = "Rapporti" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto[0].Id, potenzaUtileKw = 24.5m }, Ct));
        var generatore = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"/api/impianti/{impianto.Id}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore[0].Id, marca = "Marca di prova", modello = "Modello di prova", matricola = "PROVA-0001", potenzaKw = 24.5m, installatoIl = "2018-03-15" }, Ct));
        await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"/api/impianti/{impianto.Id}/riferimenti-catasto",
            new { regione = "toscana", codice = codiceCatasto }, Ct));

        // Codice ISTAT, sigla del generatore e codice ditta non hanno ancora un'API: si scrivono come app_owner.
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(
            """
            UPDATE anagrafica.sedi SET codice_istat_comune = '048017' WHERE sede_id = @sede;
            UPDATE impianti.generatori SET sigla_catasto = 'GT01' WHERE generatore_id = @generatore;
            UPDATE sec.aziende SET codice_ditta_siert = @ditta;
            """,
            connessione);
        comando.Parameters.AddWithValue("sede", sede.Id);
        comando.Parameters.AddWithValue("generatore", generatore.Id);
        comando.Parameters.AddWithValue("ditta", CodiceDitta);
        await comando.ExecuteNonQueryAsync(Ct);

        return (impianto.Id, generatore.Id);
    }

    private async Task<Guid> UtenteAsync(string email)
    {
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("SELECT utente_id FROM sec.utenti WHERE lower(email) = lower(@email)", connessione);
        comando.Parameters.AddWithValue("email", email);
        return (Guid)(await comando.ExecuteScalarAsync(Ct))!;
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

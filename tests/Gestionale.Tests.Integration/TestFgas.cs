using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Fgas;
using Gestionale.Application.Impianti;
using Gestionale.Application.Scadenze;
using Npgsql;

namespace Gestionale.Tests.Integration;

/// <summary>
/// Registro F-Gas dall'API al database: apparecchiatura e frequenza, scadenza del controllo,
/// tecnico senza certificato bloccato, registro, Banca dati e permessi (ADR 0008).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestFgas(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "fgas-di-prova-lunga";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Oggi => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome")).DateTime);

    [Fact]
    public async Task Dall_apparecchiatura_alla_banca_dati()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var email = await api.CreaUtenteAsync("tecnico.fgas@alfa.example", PasswordProva, "tecnico");
        var tecnicoId = await UtenteAsync(email);
        var (impiantoId, generatoreId) = await ImpiantoAsync(titolare);

        // Gas e apparecchiatura: 3 kg di R410A sono 6,263 t di CO2 equivalente, da controllare ogni 12 mesi.
        var gas = (await titolare.GetFromJsonAsync<List<GasFluorurato>>("/api/fgas/gas", Ct))!;
        var r410a = gas.Single(g => g.Codice == "R410A");
        Assert.True(r410a.DaVerificare);
        Assert.False(gas.Single(g => g.Codice == "R32").DaVerificare);

        Assert.Equal(HttpStatusCode.BadRequest, (await titolare.PutAsJsonAsync($"/api/impianti/{impiantoId}/generatori/{generatoreId}/fgas",
            new { gasId = r410a.GasId, caricaKg = 0 }, Ct)).StatusCode);
        await LeggiAsync<RispostaScritturaFgas>(await titolare.PutAsJsonAsync($"/api/impianti/{impiantoId}/generatori/{generatoreId}/fgas",
            new { gasId = r410a.GasId, caricaKg = 3, codiceUnivoco = "FGAS-PROVA-1" }, Ct));
        var apparecchiatura = Assert.Single((await titolare.GetFromJsonAsync<List<ApparecchiaturaFgas>>($"/api/impianti/{impiantoId}/fgas", Ct))!);
        Assert.Equal((6.263m, 12), (apparecchiatura.Co2eTonnellate, apparecchiatura.FrequenzaMesi));

        var scadenza = Assert.Single((await titolare.GetFromJsonAsync<List<Scadenza>>($"/api/impianti/{impiantoId}/scadenze", Ct))!, s => s.Tipo == "controllo_perdite");
        Assert.Equal(Oggi, scadenza.DataScadenza);

        // Senza certificato il tecnico non si assegna alla manutenzione dell'impianto.
        var intervento = new
        {
            impiantoId,
            generatoreId,
            tipo = "manutenzione",
            descrizione = "Controllo delle perdite (prova)",
            inizio = DateTimeOffset.UtcNow.AddHours(-3),
            fine = DateTimeOffset.UtcNow.AddHours(-2),
            tecnicoUtenteId = tecnicoId,
        };
        var bloccato = await titolare.PostAsJsonAsync("/api/interventi", intervento, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bloccato.StatusCode);
        Assert.Contains("certificato F-Gas", await bloccato.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        await LeggiAsync<RispostaScritturaFgas>(await titolare.PostAsJsonAsync("/api/fgas/certificazioni",
            new { utenteId = tecnicoId, numero = "PERS-FGAS-PROVA", ente = "Ente di prova", scadeIl = Oggi.AddYears(5) }, Ct));
        await LeggiAsync<RispostaScritturaFgas>(await titolare.PostAsJsonAsync("/api/fgas/certificazioni",
            new { numero = "IMPR-FGAS-PROVA", ente = "Ente di prova" }, Ct));
        var creato = await LeggiAsync<RispostaScritturaIntervento>(await titolare.PostAsJsonAsync("/api/interventi", intervento, Ct));

        // Il tecnico registra il controllo sul proprio intervento: chiude la scadenza e fissa la prossima.
        using var tecnico = api.Client(await api.TokenAsync(email, PasswordProva));
        var operazione = await LeggiAsync<RispostaScritturaFgas>(await tecnico.PostAsJsonAsync("/api/fgas/operazioni", new
        {
            apparecchiaturaId = apparecchiatura.ApparecchiaturaFgasId,
            interventoId = creato.Id,
            tipo = "controllo_perdite",
            eseguitoIl = Oggi,
            perditaRilevata = false,
        }, Ct));
        var scadenze = (await titolare.GetFromJsonAsync<List<Scadenza>>($"/api/impianti/{impiantoId}/scadenze", Ct))!;
        Assert.Equal(Oggi.AddMonths(12), scadenze.Single(s => s.Tipo == "controllo_perdite" && s.Stato == "aperta").DataScadenza);

        // Il Tecnico non vede le comunicazioni dell'azienda; l'ufficio sì, con i certificati copiati.
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.GetAsync("/api/fgas/comunicazioni", Ct)).StatusCode);
        var daComunicare = Assert.Single((await titolare.GetFromJsonAsync<List<ComunicazioneFgas>>("/api/fgas/comunicazioni", Ct))!);
        Assert.Equal(("PERS-FGAS-PROVA", "IMPR-FGAS-PROVA", "FGAS-PROVA-1", Oggi.AddDays(30)),
            (daComunicare.CertificatoTecnico, daComunicare.CertificatoImpresa, daComunicare.CodiceUnivoco, daComunicare.TermineComunicazione));

        await LeggiAsync<RispostaScritturaFgas>(await titolare.PostAsJsonAsync($"/api/fgas/operazioni/{operazione.Id}/comunicazione",
            new { versione = operazione.Versione }, Ct));
        Assert.Empty((await titolare.GetFromJsonAsync<List<ComunicazioneFgas>>("/api/fgas/comunicazioni", Ct))!);
        Assert.Equal(HttpStatusCode.Conflict, (await titolare.PostAsJsonAsync($"/api/fgas/operazioni/{operazione.Id}/annullamento",
            new { versione = operazione.Versione + 1, motivo = "ripensamento" }, Ct)).StatusCode);

        // Con operazioni nel registro l'apparecchiatura non si toglie.
        Assert.Equal(HttpStatusCode.BadRequest, (await titolare.DeleteAsync(
            $"/api/impianti/{impiantoId}/generatori/{generatoreId}/fgas?versione={apparecchiatura.Versione}", Ct)).StatusCode);
    }

    [Fact]
    public async Task I_certificati_li_gestisce_il_titolare()
    {
        var amministrativo = await api.CreaUtenteAsync("amm.fgas@alfa.example", PasswordProva, "amministrativo");
        using var ufficio = api.Client(await api.TokenAsync(amministrativo, PasswordProva));
        Assert.Equal(HttpStatusCode.OK, (await ufficio.GetAsync("/api/fgas/certificazioni", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ufficio.PostAsJsonAsync("/api/fgas/certificazioni", new { numero = "X" }, Ct)).StatusCode);

        using var titolare = api.Client(await api.TokenTitolareAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await titolare.PostAsJsonAsync("/api/fgas/certificazioni", new { numero = " " }, Ct)).StatusCode);
    }

    // Un impianto di climatizzazione con un generatore, di un cliente fittizio.
    private static async Task<(Guid Impianto, Guid Generatore)> ImpiantoAsync(HttpClient client)
    {
        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti", new { tipo = "privato", nome = "Fittizio", cognome = "Fgas" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo = "Via del Freddo", civico = "3", cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto.First(v => v.Codice == "prova_climatizzazione").Id }, Ct));
        var generatore = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync($"/api/impianti/{impianto.Id}/generatori",
            new { tipoGeneratoreId = voci.TipiGeneratore.First(v => v.Codice == "prova_climatizzatore").Id, matricola = "SPLIT-PROVA-1" }, Ct));
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

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

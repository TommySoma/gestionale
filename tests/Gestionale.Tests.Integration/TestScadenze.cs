using System.Net;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Application.Interventi;
using Gestionale.Application.Scadenze;
using Npgsql;

namespace Gestionale.Tests.Integration;

/// <summary>Scadenze dall'API al database: calcolo, data corretta a mano, richiami, chiusura con l'intervento e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestScadenze(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "termostato-in-corridoio";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // La data di oggi in Italia, come la calcola il database.
    private static DateOnly Oggi => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FusoItaliano()).DateTime);

    [Fact]
    public async Task Le_scadenze_nascono_con_l_impianto_e_si_chiudono_con_la_manutenzione()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var tecnicoId = await UtenteAsync(await api.CreaUtenteAsync("tecnico.scadenze@alfa.example", PasswordProva, "tecnico"));
        var impiantoId = await ImpiantoAsync(client, "Via delle Scadenze", "1", manutenzioneMesi: 12);

        var impianto = (await client.GetFromJsonAsync<Impianto>($"/api/impianti/{impiantoId}", Ct))!;
        Assert.Equal(12, impianto.ManutenzioneMesi);

        // Senza interventi alle spalle sono da fare subito; il controllo viene da una regola di prova.
        var scadenze = await ScadenzeAsync(client, impiantoId);
        Assert.Equal(["controllo_efficienza", "manutenzione"], scadenze.Select(s => s.Tipo).Order());
        Assert.All(scadenze, s => Assert.Equal((Oggi, "aperta", "non_contattato"), (s.DataScadenza, s.Stato, s.StatoRichiamo)));
        var controllo = scadenze.Single(s => s.Tipo == "controllo_efficienza");
        Assert.True(controllo.RegolaDaVerificare);
        Assert.Contains("voce di prova", controllo.Regola, StringComparison.Ordinal);

        var inArrivo = (await client.GetFromJsonAsync<Pagina<ScadenzaInArrivo>>("/api/scadenze?giorni=0&dimensione=200", Ct))!;
        var riga = Assert.Single(inArrivo.Elementi, s => s.ScadenzaId == controllo.ScadenzaId);
        Assert.Equal(("Fittizio Scadenze", "Via delle Scadenze 1", 0), (riga.Cliente, riga.Indirizzo, riga.GiorniMancanti));
        var soloManutenzioni = (await client.GetFromJsonAsync<Pagina<ScadenzaInArrivo>>("/api/scadenze?giorni=0&tipo=manutenzione&dimensione=200", Ct))!;
        Assert.All(soloManutenzioni.Elementi, s => Assert.Equal("manutenzione", s.Tipo));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/scadenze?giorni=-1", Ct)).StatusCode);

        // L'ufficio corregge la data della manutenzione: il ricalcolo non la sposta più.
        var manutenzione = scadenze.Single(s => s.Tipo == "manutenzione");
        var nuovaData = Oggi.AddDays(60);
        var corretta = await LeggiAsync<RispostaScritturaScadenza>(await client.PutAsJsonAsync(
            $"/api/scadenze/{manutenzione.ScadenzaId}/data", new { data = nuovaData, versione = manutenzione.Versione }, Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(
            $"/api/scadenze/{manutenzione.ScadenzaId}/data", new { data = nuovaData, versione = manutenzione.Versione }, Ct)).StatusCode);
        var letta = (await client.GetFromJsonAsync<Scadenza>($"/api/scadenze/{manutenzione.ScadenzaId}", Ct))!;
        Assert.Equal((nuovaData, true), (letta.DataScadenza, letta.DataManuale));

        // Un tentativo di contatto senza risposta.
        var dopoRichiamo = await LeggiAsync<RispostaScritturaScadenza>(await client.PostAsJsonAsync(
            $"/api/scadenze/{manutenzione.ScadenzaId}/richiami",
            new { canale = "Telefono", esito = "nessuna_risposta", richiamareIl = Oggi.AddDays(2), note = "Richiamare dopo le 18", versione = corretta.Versione }, Ct));
        Assert.True(dopoRichiamo.Versione > corretta.Versione);
        letta = (await client.GetFromJsonAsync<Scadenza>($"/api/scadenze/{manutenzione.ScadenzaId}", Ct))!;
        Assert.Equal("in_attesa", letta.StatoRichiamo);
        var richiamo = Assert.Single(letta.Richiami);
        Assert.Equal(("telefono", "nessuna_risposta", Oggi.AddDays(2)), (richiamo.Canale, richiamo.Esito, richiamo.RichiamareIl));
        Assert.False(string.IsNullOrWhiteSpace(richiamo.EffettuatoDa));

        var sbagliato = await client.PostAsJsonAsync($"/api/scadenze/{manutenzione.ScadenzaId}/richiami",
            new { canale = "piccione", esito = "rifiutato", richiamareIl = Oggi, versione = dopoRichiamo.Versione }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sbagliato.StatusCode);
        var testo = await DettaglioAsync(sbagliato);
        Assert.Contains("il canale deve essere", testo, StringComparison.Ordinal);
        Assert.Contains("la data per risentirsi vale solo", testo, StringComparison.Ordinal);

        // L'appuntamento: una manutenzione pianificata, collegata al richiamo.
        var intervento = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId,
            tipo = "manutenzione",
            inizio = "2027-05-10T09:00:00+02:00",
            fine = "2027-05-10T10:00:00+02:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct));
        await LeggiAsync<RispostaScritturaScadenza>(await client.PostAsJsonAsync($"/api/scadenze/{manutenzione.ScadenzaId}/richiami",
            new { canale = "telefono", esito = "appuntamento", interventoId = intervento.Id, versione = dopoRichiamo.Versione }, Ct));
        Assert.Equal("fissato", (await client.GetFromJsonAsync<Scadenza>($"/api/scadenze/{manutenzione.ScadenzaId}", Ct))!.StatoRichiamo);

        // Il controllo si spunta solo su una manutenzione.
        var riparazione = await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId,
            tipo = "riparazione",
            inizio = "2027-05-11T09:00:00+02:00",
            fine = "2027-05-11T10:00:00+02:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct));
        var controlloSbagliato = await client.PostAsJsonAsync($"/api/interventi/{riparazione.Id}/chiusura",
            new { versione = riparazione.Versione, esito = "risolto", controlloEfficienza = true }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, controlloSbagliato.StatusCode);
        Assert.Contains("solo chiudendo una manutenzione", await DettaglioAsync(controlloSbagliato), StringComparison.Ordinal);

        // La manutenzione risolta con il controllo chiude entrambe e calcola le successive.
        await LeggiAsync<RispostaScritturaIntervento>(await client.PostAsJsonAsync($"/api/interventi/{intervento.Id}/chiusura",
            new { versione = intervento.Versione, esito = "risolto", controlloEfficienza = true }, Ct));
        Assert.True((await client.GetFromJsonAsync<Intervento>($"/api/interventi/{intervento.Id}", Ct))!.ControlloEfficienza);

        scadenze = await ScadenzeAsync(client, impiantoId);
        Assert.All(scadenze.Where(s => s.Stato == "chiusa"), s => Assert.Equal(intervento.Id, s.InterventoChiusuraId));
        Assert.Equal(2, scadenze.Count(s => s.Stato == "chiusa"));
        var aperte = scadenze.Where(s => s.Stato == "aperta").ToDictionary(s => s.Tipo);
        Assert.Equal(Oggi.AddMonths(12), aperte["manutenzione"].DataScadenza);
        Assert.Equal(Oggi.AddMonths(controllo.PeriodicitaMesi), aperte["controllo_efficienza"].DataScadenza);
        Assert.Equal(intervento.Id, aperte["manutenzione"].InterventoOrigineId);

        // Una scadenza chiusa non si richiama più.
        var chiusa = scadenze.First(s => s.Stato == "chiusa");
        var richiamoChiusa = await client.PostAsJsonAsync($"/api/scadenze/{chiusa.ScadenzaId}/richiami",
            new { canale = "telefono", esito = "rifiutato", versione = chiusa.Versione }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, richiamoChiusa.StatusCode);
        Assert.Contains("chiusa", await DettaglioAsync(richiamoChiusa), StringComparison.Ordinal);

        // Togliendo la periodicità la manutenzione si annulla.
        impianto = (await client.GetFromJsonAsync<Impianto>($"/api/impianti/{impiantoId}", Ct))!;
        await LeggiAsync<RispostaScritturaImpianto>(await client.PutAsJsonAsync($"/api/impianti/{impiantoId}",
            new { sedeId = impianto.SedeId, tipoImpiantoId = impianto.TipoImpiantoId, versione = impianto.Versione }, Ct));
        scadenze = await ScadenzeAsync(client, impiantoId);
        Assert.Equal("annullata", scadenze.Single(s => s.Tipo == "manutenzione" && s.InterventoOrigineId == intervento.Id).Stato);
    }

    [Fact]
    public async Task Il_tecnico_legge_solo_le_scadenze_dei_propri_impianti_senza_richiami()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var email = await api.CreaUtenteAsync("tecnico.propri.scadenze@alfa.example", PasswordProva, "tecnico");
        var tecnicoId = await UtenteAsync(email);
        var suo = await ImpiantoAsync(titolare, "Via del Tecnico Scadenze", "2", manutenzioneMesi: 24);
        var altro = await ImpiantoAsync(titolare, "Via del Collega Scadenze", "3", manutenzioneMesi: 24);

        await LeggiAsync<RispostaScritturaIntervento>(await titolare.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId = suo,
            tipo = "manutenzione",
            inizio = "2027-06-01T09:00:00+02:00",
            fine = "2027-06-01T10:00:00+02:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct));

        var manutenzione = (await ScadenzeAsync(titolare, suo)).Single(s => s.Tipo == "manutenzione");
        await LeggiAsync<RispostaScritturaScadenza>(await titolare.PostAsJsonAsync($"/api/scadenze/{manutenzione.ScadenzaId}/richiami",
            new { canale = "email", esito = "da_richiamare", versione = manutenzione.Versione }, Ct));

        using var client = api.Client(await api.TokenAsync(email, PasswordProva));

        var elenco = (await client.GetFromJsonAsync<Pagina<ScadenzaInArrivo>>("/api/scadenze?giorni=0&dimensione=200", Ct))!;
        Assert.NotEmpty(elenco.Elementi);
        Assert.All(elenco.Elementi, s => Assert.Equal(suo, s.ImpiantoId));
        Assert.All(elenco.Elementi, s => Assert.Null(s.UltimoEsito));

        var dettaglio = (await client.GetFromJsonAsync<Scadenza>($"/api/scadenze/{manutenzione.ScadenzaId}", Ct))!;
        Assert.Empty(dettaglio.Richiami);
        Assert.NotEmpty(await ScadenzeAsync(client, suo));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{altro}/scadenze", Ct)).StatusCode);

        // I richiami e le date sono dell'ufficio.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/scadenze/{manutenzione.ScadenzaId}/richiami",
            new { canale = "telefono", esito = "rifiutato", versione = dettaglio.Versione }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/scadenze/{manutenzione.ScadenzaId}/data",
            new { data = Oggi, versione = dettaglio.Versione }, Ct)).StatusCode);

        // Amministratore di sistema: nessun accesso.
        using var amministratore = api.Client(await api.TokenAsync(ApiFixture.EmailAmministratore, ApiFixture.PasswordAmministratore));
        Assert.Equal(HttpStatusCode.Forbidden, (await amministratore.GetAsync("/api/scadenze", Ct)).StatusCode);
    }

    private static async Task<List<Scadenza>> ScadenzeAsync(HttpClient client, Guid impiantoId) =>
        (await client.GetFromJsonAsync<List<Scadenza>>($"/api/impianti/{impiantoId}/scadenze", Ct))!;

    private async Task<Guid> UtenteAsync(string email)
    {
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("SELECT utente_id FROM sec.utenti WHERE lower(email) = lower(@email)", connessione);
        comando.Parameters.AddWithValue("email", email);
        return (Guid)(await comando.ExecuteScalarAsync(Ct))!;
    }

    // Un impianto di climatizzazione: la sua regola di prova non ha fasce di potenza.
    private static async Task<Guid> ImpiantoAsync(HttpClient client, string indirizzo, string civico, int manutenzioneMesi)
    {
        var voci = (await client.GetFromJsonAsync<VociImpianti>("/api/impianti/voci", Ct))!;
        var cliente = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync("/api/clienti",
            new { tipo = "privato", nome = "Scadenze", cognome = "Fittizio" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti", new
        {
            sedeId = sede.Id,
            tipoImpiantoId = voci.TipiImpianto.Single(t => t.Codice == "prova_climatizzazione").Id,
            manutenzioneMesi,
        }, Ct));
        return impianto.Id;
    }

    private static TimeZoneInfo FusoItaliano() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Rome", out var fuso) ? fuso : TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

    /// <summary>Il messaggio per l'utente: nel JSON grezzo le lettere accentate sono codificate.</summary>
    private static async Task<string> DettaglioAsync(HttpResponseMessage risposta) =>
        (await risposta.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Ct))?.Detail ?? "";

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

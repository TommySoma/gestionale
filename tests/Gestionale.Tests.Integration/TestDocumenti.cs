using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Documenti;
using Gestionale.Application.Impianti;
using Npgsql;

namespace Gestionale.Tests.Integration;

/// <summary>Foto e documenti degli impianti dall'API al disco e al database: carica, elenca, scarica, rifiuta, elimina.</summary>
[Trait("Category", "Integration")]
public sealed class TestDocumenti(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "chiave-inglese-da-quattordici";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Una_foto_si_carica_si_elenca_si_scarica_e_si_elimina()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var impiantoId = await ImpiantoAsync(client, "Via delle Foto", "1");
        var foto = Jpeg(150_000);

        var risposta = await client.PostAsync($"/api/impianti/{impiantoId}/documenti", Modulo(foto, @"C:\Telefono\IMG 0001.JPG", "Caldaia vista di fronte"), Ct);
        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
        var caricato = (await risposta.Content.ReadFromJsonAsync<Documento>(Ct))!;
        Assert.Equal(("foto", "image/jpeg", "IMG 0001.jpg", 150_000L), (caricato.Categoria, caricato.TipoMime, caricato.NomeOriginale, caricato.Dimensione));
        Assert.Equal("Mario Rossi", caricato.CreatoDa);

        // Sul disco il file ha l'id del documento, non il nome originale.
        Assert.Single(Directory.GetFiles(api.CartellaDocumenti, caricato.DocumentoId.ToString("D"), SearchOption.AllDirectories));

        var elenco = (await client.GetFromJsonAsync<List<Documento>>($"/api/impianti/{impiantoId}/documenti", Ct))!;
        Assert.Equal(caricato.DocumentoId, Assert.Single(elenco).DocumentoId);

        var scaricato = await client.GetAsync($"/api/impianti/{impiantoId}/documenti/{caricato.DocumentoId}/contenuto", Ct);
        Assert.Equal(HttpStatusCode.OK, scaricato.StatusCode);
        Assert.Equal("image/jpeg", scaricato.Content.Headers.ContentType?.MediaType);
        Assert.Equal("IMG 0001.jpg", scaricato.Content.Headers.ContentDisposition?.FileNameStar ?? scaricato.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("nosniff", scaricato.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(foto, await scaricato.Content.ReadAsByteArrayAsync(Ct));

        // L'impronta salvata è quella del file.
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(foto), await ImprontaAsync(caricato.DocumentoId));

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/impianti/{impiantoId}/documenti/{caricato.DocumentoId}?versione=9", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/impianti/{impiantoId}/documenti/{caricato.DocumentoId}?versione=1", Ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Documento>>($"/api/impianti/{impiantoId}/documenti", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{impiantoId}/documenti/{caricato.DocumentoId}/contenuto", Ct)).StatusCode);

        // L'eliminazione è logica: il file resta sul disco.
        Assert.Single(Directory.GetFiles(api.CartellaDocumenti, caricato.DocumentoId.ToString("D"), SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Tipi_non_ammessi_file_vuoti_e_troppo_grandi_sono_rifiutati_senza_lasciare_file()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var impiantoId = await ImpiantoAsync(client, "Via dei Rifiuti", "2");
        var primaDiTutto = Directory.Exists(api.CartellaDocumenti) ? Directory.GetFiles(api.CartellaDocumenti, "*", SearchOption.AllDirectories).Length : 0;

        // Un eseguibile rinominato in .pdf: conta il contenuto, non il nome.
        var eseguibile = await client.PostAsync($"/api/impianti/{impiantoId}/documenti", Modulo([0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0], "libretto.pdf"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, eseguibile.StatusCode);
        Assert.Contains("JPEG o PNG e documenti PDF", await DettaglioAsync(eseguibile), StringComparison.Ordinal);

        var vuoto = await client.PostAsync($"/api/impianti/{impiantoId}/documenti", Modulo([], "vuoto.pdf"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, vuoto.StatusCode);

        var grande = await client.PostAsync($"/api/impianti/{impiantoId}/documenti", Modulo(Jpeg((20 * 1024 * 1024) + 1), "enorme.jpg"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, grande.StatusCode);
        Assert.Contains("20 MB", await DettaglioAsync(grande), StringComparison.Ordinal);

        var senzaFile = await client.PostAsync($"/api/impianti/{impiantoId}/documenti", new MultipartFormDataContent { { new StringContent("x"), "descrizione" } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, senzaFile.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/impianti/{Guid.NewGuid()}/documenti", Modulo(Jpeg(10), "x.jpg"), Ct)).StatusCode);

        var dopo = Directory.Exists(api.CartellaDocumenti) ? Directory.GetFiles(api.CartellaDocumenti, "*", SearchOption.AllDirectories).Length : 0;
        Assert.Equal(primaDiTutto, dopo);
    }

    [Fact]
    public async Task Il_tecnico_carica_sugli_impianti_dei_propri_interventi_ma_non_elimina()
    {
        using var titolare = api.Client(await api.TokenTitolareAsync());
        var suo = await ImpiantoAsync(titolare, "Via del Tecnico", "3");
        var altro = await ImpiantoAsync(titolare, "Via del Collega", "4");
        var email = await api.CreaUtenteAsync("tecnico.documenti@alfa.example", PasswordProva, "tecnico");
        var tecnicoId = await UtenteAsync(email);
        Assert.True((await titolare.PostAsJsonAsync("/api/interventi", new
        {
            impiantoId = suo,
            tipo = "manutenzione",
            inizio = "2027-05-03T09:00:00+02:00",
            fine = "2027-05-03T10:00:00+02:00",
            tecnicoUtenteId = tecnicoId,
        }, Ct)).IsSuccessStatusCode);

        using var client = api.Client(await api.TokenAsync(email, PasswordProva));

        var risposta = await client.PostAsync($"/api/impianti/{suo}/documenti", Modulo(Pdf(), "scheda tecnica.pdf"), Ct);
        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
        var caricato = (await risposta.Content.ReadFromJsonAsync<Documento>(Ct))!;
        Assert.Equal("documento", caricato.Categoria);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/impianti/{altro}/documenti", Modulo(Pdf(), "x.pdf"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/impianti/{altro}/documenti", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/impianti/{suo}/documenti/{caricato.DocumentoId}?versione=1", Ct)).StatusCode);

        // Sola lettura: vede, non carica.
        var consulente = await api.CreaUtenteAsync("consulente.documenti@alfa.example", PasswordProva, "sola_lettura");
        using var lettura = api.Client(await api.TokenAsync(consulente, PasswordProva));
        Assert.Single((await lettura.GetFromJsonAsync<List<Documento>>($"/api/impianti/{suo}/documenti", Ct))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await lettura.PostAsync($"/api/impianti/{suo}/documenti", Modulo(Pdf(), "x.pdf"), Ct)).StatusCode);
    }

    private static MultipartFormDataContent Modulo(byte[] dati, string nome, string? descrizione = null)
    {
        var file = new ByteArrayContent(dati);
        // Il tipo dichiarato da chi invia non conta: il server guarda il contenuto.
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var modulo = new MultipartFormDataContent { { file, "file", nome } };
        if (descrizione is not null)
        {
            modulo.Add(new StringContent(descrizione), "descrizione");
        }

        return modulo;
    }

    private static byte[] Jpeg(int dimensione)
    {
        var dati = new byte[dimensione];
        Random.Shared.NextBytes(dati);
        if (dimensione >= 3)
        {
            (dati[0], dati[1], dati[2]) = ((byte)0xFF, (byte)0xD8, (byte)0xFF);
        }

        return dati;
    }

    private static byte[] Pdf() => "%PDF-1.7\n% documento di prova\n%%EOF\n"u8.ToArray();

    private async Task<byte[]> ImprontaAsync(Guid documentoId)
    {
        await using var connessione = new NpgsqlConnection(api.StringaConnessioneOwner);
        await connessione.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("SELECT sha256 FROM documenti.documenti WHERE documento_id = @id", connessione);
        comando.Parameters.AddWithValue("id", documentoId);
        return (byte[])(await comando.ExecuteScalarAsync(Ct))!;
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
            new { tipo = "privato", nome = "Documenti", cognome = "Fittizio" }, Ct));
        var sede = await LeggiAsync<RispostaScrittura>(await client.PostAsJsonAsync($"/api/clienti/{cliente.Id}/sedi",
            new { indirizzo, civico, cap = "50100", comune = "Firenze", provincia = "FI" }, Ct));
        var impianto = await LeggiAsync<RispostaScritturaImpianto>(await client.PostAsJsonAsync("/api/impianti",
            new { sedeId = sede.Id, tipoImpiantoId = voci.TipiImpianto[0].Id }, Ct));
        return impianto.Id;
    }

    private static async Task<string> DettaglioAsync(HttpResponseMessage risposta) =>
        (await risposta.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Ct))?.Detail ?? "";

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

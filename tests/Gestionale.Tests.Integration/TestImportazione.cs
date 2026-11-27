using System.Net;
using System.Net.Http.Json;
using System.Text;
using Gestionale.Api.Endpoint;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;

namespace Gestionale.Tests.Integration;

/// <summary>Importazione da CSV dall'API al database: anteprima senza scritture, tutto o niente, doppioni e permessi.</summary>
[Trait("Category", "Integration")]
public sealed class TestImportazione(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string PasswordProva = "importazione-di-prova";

    private const string Intestazione =
        "tipo_cliente;denominazione;partita_iva;email;telefono;indirizzo;civico;cap;comune;provincia;tipo_impianto;potenza_kw;pdr;codice_catasto;tipo_generatore;matricola";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task L_anteprima_non_scrive_e_l_importazione_si()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var piva = PartitaIva("0000000101");
        var file = Csv(
            Intestazione,
            $"azienda;Condominio Prova Uno (fittizio);{piva};amministratore@example.com;055 000001;Via dell'Import;1;50100;Firenze;FI;prova_riscaldamento;34,5;IMPORT-PDR-1;1000000001;prova_caldaia;IMP-MAT-1",
            $"azienda;Condominio Prova Uno (fittizio);{piva};;;Via dell'Import;1;50100;Firenze;FI;prova_riscaldamento;12;IMPORT-PDR-2;;;");

        var anteprima = await LeggiAsync<RispostaImportazione>(await client.PostAsync("/api/importazioni/anteprima", file, Ct));
        Assert.False(anteprima.Importata);
        Assert.Empty(anteprima.Errori);
        Assert.Equal((1, 0, 1, 2, 1), (anteprima.ClientiNuovi, anteprima.ClientiEsistenti, anteprima.SediNuove, anteprima.ImpiantiNuovi, anteprima.Generatori));
        Assert.Equal(0, (await CercaClientiAsync(client, piva)).Totale);

        var importata = await LeggiAsync<RispostaImportazione>(await client.PostAsync("/api/importazioni", Csv(file), Ct));
        Assert.True(importata.Importata);
        var clienti = await CercaClientiAsync(client, piva);
        var cliente = (await client.GetFromJsonAsync<Cliente>($"/api/clienti/{Assert.Single(clienti.Elementi).ClienteId}", Ct))!;
        Assert.Single(cliente.Sedi);
        Assert.Equal(["email", "telefono"], cliente.Contatti.Select(c => c.Tipo).Order());

        var impianti = (await client.GetFromJsonAsync<Pagina<RiepilogoImpianto>>("/api/impianti?cerca=IMP-MAT-1", Ct))!;
        var impianto = (await client.GetFromJsonAsync<Impianto>($"/api/impianti/{Assert.Single(impianti.Elementi).ImpiantoId}", Ct))!;
        Assert.Equal((34.5m, "IMPORT-PDR-1"), (impianto.PotenzaUtileKw, impianto.Pdr));
        Assert.Equal("1000000001", Assert.Single(impianto.RiferimentiCatasto).Codice);
        Assert.Equal("IMP-MAT-1", Assert.Single(impianto.Generatori).Matricola);

        // Lo stesso file una seconda volta: cliente e sede si usano, gli impianti con PDR già presente si saltano.
        var ripetuta = await LeggiAsync<RispostaImportazione>(await client.PostAsync("/api/importazioni", Csv(file), Ct));
        Assert.True(ripetuta.Importata);
        Assert.Equal((0, 1, 0, 0, 2), (ripetuta.ClientiNuovi, ripetuta.ClientiEsistenti, ripetuta.SediNuove, ripetuta.ImpiantiNuovi, ripetuta.ImpiantiSaltati));
        Assert.Equal(2, ripetuta.Avvisi.Count(a => a.Codice == "ImpiantoGiaPresentePdr"));
    }

    [Fact]
    public async Task Con_un_errore_non_si_scrive_nulla()
    {
        using var client = api.Client(await api.TokenTitolareAsync());
        var piva = PartitaIva("0000000202");
        var file = Csv(
            Intestazione,
            $"azienda;Clima Prova Due (fittizia);{piva};;;Via Buona;1;50100;Firenze;FI;prova_riscaldamento;;;;;",
            $"azienda;Clima Prova Tre (fittizia);;;;Via Errata;2;50100;Firenze;FI;voce_che_non_esiste;;;;;");

        var esito = await LeggiAsync<RispostaImportazione>(await client.PostAsync("/api/importazioni", file, Ct));
        Assert.False(esito.Importata);
        var errore = Assert.Single(esito.Errori);
        Assert.Equal((3, "TipoImpiantoSconosciuto"), (errore.Riga, errore.Codice));
        Assert.Contains("voce_che_non_esiste", errore.Messaggio, StringComparison.Ordinal);
        Assert.Equal(0, (await CercaClientiAsync(client, piva)).Totale);

        // Un file senza le colonne obbligatorie si ferma all'intestazione.
        var senzaColonne = await LeggiAsync<RispostaImportazione>(await client.PostAsync("/api/importazioni/anteprima", Csv("nome;cognome", "Mario;Prova"), Ct));
        Assert.Equal(4, senzaColonne.Errori.Count(e => e is { Riga: 0, Codice: "ColonnaObbligatoriaMancante" }));
    }

    [Fact]
    public async Task Importa_solo_chi_scrive_clienti_e_impianti_di_tutta_l_azienda()
    {
        var file = Csv(Intestazione, "azienda;Prova Permessi (fittizia);;;;Via Permessi;1;50100;Firenze;FI;;;;;;");

        var amministrativo = await api.CreaUtenteAsync("amm.importazione@alfa.example", PasswordProva, "amministrativo");
        using var ufficio = api.Client(await api.TokenAsync(amministrativo, PasswordProva));
        Assert.Equal(HttpStatusCode.OK, (await ufficio.PostAsync("/api/importazioni/anteprima", file, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ufficio.GetAsync("/api/importazioni/modello", Ct)).StatusCode);

        var responsabile = await api.CreaUtenteAsync("resp.importazione@alfa.example", PasswordProva, "responsabile_tecnico");
        using var tecnico = api.Client(await api.TokenAsync(responsabile, PasswordProva));
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsync("/api/importazioni/anteprima", Csv(file), Ct)).StatusCode);

        using var titolare = api.Client(await api.TokenTitolareAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await titolare.PostAsync("/api/importazioni", new MultipartFormDataContent { { new StringContent("x"), "altro" } }, Ct)).StatusCode);
    }

    private static MultipartFormDataContent Csv(params string[] righe) => Modulo(Encoding.UTF8.GetBytes(string.Join("\r\n", righe)));

    // Un contenuto multipart si invia una volta sola: per inviarlo di nuovo se ne fa una copia.
    private static MultipartFormDataContent Csv(MultipartFormDataContent originale) =>
        Modulo(originale.First().ReadAsByteArrayAsync().GetAwaiter().GetResult());

    private static MultipartFormDataContent Modulo(byte[] dati) => new() { { new ByteArrayContent(dati), "file", "importazione.csv" } };

    private static async Task<Pagina<RiepilogoCliente>> CercaClientiAsync(HttpClient client, string piva) =>
        (await client.GetFromJsonAsync<Pagina<RiepilogoCliente>>($"/api/clienti?cerca={piva}", Ct))!;

    // Partita IVA fittizia ma con la cifra di controllo giusta.
    private static string PartitaIva(string dieciCifre)
    {
        var somma = 0;
        for (var i = 0; i < 10; i++)
        {
            var cifra = dieciCifre[i] - '0';
            if (i % 2 == 1)
            {
                cifra *= 2;
                cifra = cifra > 9 ? cifra - 9 : cifra;
            }

            somma += cifra;
        }

        return dieciCifre + ((10 - (somma % 10)) % 10);
    }

    private static async Task<T> LeggiAsync<T>(HttpResponseMessage risposta)
    {
        Assert.True(risposta.IsSuccessStatusCode, $"{(int)risposta.StatusCode}: {await risposta.Content.ReadAsStringAsync(Ct)}");
        return (await risposta.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}

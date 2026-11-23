using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit;

public sealed class TestFormatoImpianti
{
    [Theory]
    [InlineData("24", 24)]
    [InlineData(" 24,5 ", 24.5)]
    [InlineData("24.5", 24.5)]
    [InlineData("1250", 1250)]
    public void La_potenza_si_scrive_con_la_virgola_o_con_il_punto(string testo, double atteso)
    {
        Assert.True(FormatoImpianti.ProvaDecimale(testo, out var valore));
        Assert.Equal((decimal)atteso, valore);
    }

    [Theory]
    [InlineData("ventiquattro")]
    [InlineData("24 kW")]
    [InlineData("1.234,5")] // separatore delle migliaia: ambiguo, meglio chiedere di riscrivere
    [InlineData("-3")]
    public void Una_potenza_non_numerica_non_passa(string testo)
    {
        Assert.False(FormatoImpianti.ProvaDecimale(testo, out _));
    }

    [Fact]
    public void Un_campo_vuoto_significa_nessun_valore()
    {
        Assert.True(FormatoImpianti.ProvaDecimale("  ", out var valore));
        Assert.Null(valore);
    }
}

public sealed class TestSchedaImpianto
{
    private static readonly Guid s_impianto = Guid.CreateVersion7();
    private static readonly Guid s_tipo = Guid.CreateVersion7();

    private static object Impianto() => new
    {
        impiantoId = s_impianto,
        sedeId = Guid.CreateVersion7(),
        clienteId = Guid.CreateVersion7(),
        cliente = "Rossi Mario",
        indirizzo = "Via dei Platani",
        civico = "12",
        cap = "50100",
        comune = "Firenze",
        provincia = "FI",
        tipoImpiantoId = s_tipo,
        tipoImpianto = "Riscaldamento (voce di prova)",
        tipoDaVerificare = true,
        stato = "attivo",
        annoInstallazione = 2015,
        potenzaUtileKw = 24.5m,
        pdr = "00012345678901",
        versione = 3,
        indirizzoDuplicato = true,
        generatori = new object[]
        {
            new { generatoreId = Guid.CreateVersion7(), tipoGeneratoreId = s_tipo, tipoGeneratore = "Caldaia", marca = "Marca", modello = "M24", matricola = "MAT-1", potenzaKw = 24m, installatoIl = "2015-03-10", versione = 1, matricolaDuplicata = true },
            new { generatoreId = Guid.CreateVersion7(), tipoGeneratoreId = s_tipo, tipoGeneratore = "Caldaia", dismessoIl = "2026-11-01", versione = 2, matricolaDuplicata = false },
        },
        riferimentiCatasto = new object[]
        {
            new { riferimentoCatastoId = Guid.CreateVersion7(), regione = "toscana", sistema = "siert", codice = "CAT-1", registratoIl = "2016-01-20", versione = 1 },
        },
    };

    private static async Task<(ShellViewModel Shell, ServerFinto Server)> CreaAsync(params Permesso[] permessi)
    {
        var server = new ServerFinto(richiesta => richiesta.RequestUri!.AbsolutePath switch
        {
            "/api/accesso" => ServerFinto.Json(new
            {
                esito = "completato",
                token = new { tokenAccesso = "a", accessoScadeIl = DateTimeOffset.UtcNow.AddMinutes(15), tokenRinnovo = "r", rinnovoScadeIl = DateTimeOffset.UtcNow.AddHours(12) },
            }),
            "/api/impianti/voci" => ServerFinto.Json(new
            {
                tipiImpianto = new[] { new { id = s_tipo, codice = "prova", nome = "Riscaldamento (voce di prova)", daVerificare = true } },
                tipiGeneratore = new[] { new { id = s_tipo, codice = "prova", nome = "Caldaia (voce di prova)", daVerificare = true } },
                combustibili = Array.Empty<object>(),
            }),
            _ when richiesta.Method == HttpMethod.Get => ServerFinto.Json(Impianto()),
            _ => ServerFinto.Json(new { id = s_impianto, versione = 4, avvisi = Array.Empty<string>() }),
        });
        var api = new ClientApi(new PortachiaviInMemoria(), TimeProvider.System, indirizzo => new HttpClient(server) { BaseAddress = indirizzo });
        api.ImpostaServer(new Uri("http://gestionale.locale:5225"));
        await api.AccediAsync("t@a.example", "una-password-lunga", TestContext.Current.CancellationToken);

        var profilo = new Profilo(Guid.NewGuid(), Guid.NewGuid(), "t@a.example", "Anna", "Titolare", ["titolare"], permessi);
        return (new ShellViewModel(api, profilo, new NavigazioneMuta(), new PreferenzaTema(new ArchivioImpostazioniInMemoria(), _ => { })), server);
    }

    [Fact]
    public async Task La_scheda_mostra_dati_tecnici_generatori_catasto_e_avvisi()
    {
        var (shell, _) = await CreaAsync(new Permesso("impianti.lettura", "tutti"), new Permesso("impianti.scrittura", "tutti"));
        var scheda = new SchedaImpiantoViewModel(shell, s_impianto);

        await scheda.CaricaAsync();

        Assert.True(scheda.Caricato);
        Assert.Equal("Via dei Platani 12, 50100 Firenze (FI)", scheda.Indirizzo);
        Assert.True(scheda.IndirizzoDuplicato);
        Assert.True(scheda.VociDaVerificare);
        Assert.True(scheda.PuoModificare);
        Assert.False(scheda.PuoAprireCliente);
        Assert.Contains(scheda.DatiTecnici, c => c.Etichetta == Testi.PotenzaUtile && c.Valore == "24,5 kW");

        Assert.Equal(2, scheda.Generatori.Count);
        var attivo = scheda.Generatori[0];
        Assert.Equal("Caldaia · Marca M24", attivo.Titolo);
        Assert.True(attivo.MatricolaDuplicata);
        Assert.False(attivo.Dismesso);
        Assert.True(scheda.Generatori[1].Dismesso);
        Assert.Equal(Testi.NessunDettaglio, scheda.Generatori[1].Dettaglio);

        var riferimento = Assert.Single(scheda.Riferimenti);
        Assert.Equal((Testi.CatastoToscana, "CAT-1"), (riferimento.Sistema, riferimento.Codice));
    }

    [Fact]
    public async Task Il_tecnico_con_l_ambito_propri_non_modifica()
    {
        var (shell, _) = await CreaAsync(new Permesso("impianti.lettura", "propri"), new Permesso("impianti.scrittura", "propri"));

        Assert.True(shell.VedeImpianti);
        Assert.False(shell.ModificaImpianti);
    }

    [Fact]
    public async Task L_editor_controlla_i_numeri_prima_di_chiamare_il_server()
    {
        var (shell, server) = await CreaAsync(new Permesso("impianti.lettura", "tutti"), new Permesso("impianti.scrittura", "tutti"));
        var voci = (await shell.VociImpiantiAsync()).Valore!;
        var editor = new EditorImpiantoViewModel(shell, voci, Guid.CreateVersion7(), new OpzioneCliente(Guid.CreateVersion7(), "Rossi Mario"), null, _ => { })
        {
            Potenza = "ventiquattro",
        };
        var richiestePrima = server.Richieste.Count;

        await editor.SalvaCommand.ExecuteAsync(null);

        Assert.Equal(Testi.PotenzaNonNumerica, editor.Errore);
        Assert.Equal(richiestePrima, server.Richieste.Count);

        editor.Potenza = "24,5";
        editor.Anno = "2015";
        await editor.SalvaCommand.ExecuteAsync(null);

        Assert.Null(editor.Errore);
        Assert.Equal((HttpMethod.Post, "/api/impianti"), (server.Richieste[^1].Metodo, server.Richieste[^1].Percorso));
    }

    private sealed class NavigazioneMuta : INavigazione
    {
        public void VaiAdAccesso(string? messaggio = null)
        {
        }

        public void VaiAVerificaMfa(string tokenPassaggio, string email)
        {
        }

        public void VaiAConfiguraMfa(string tokenPassaggio, string email)
        {
        }

        public void VaiACodiciRecupero(IReadOnlyList<string> codici)
        {
        }

        public Task VaiAllApplicazioneAsync() => Task.CompletedTask;
    }
}

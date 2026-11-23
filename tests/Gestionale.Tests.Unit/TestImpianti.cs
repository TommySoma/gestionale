using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Tests.Unit;

public sealed class TestRegoleImpianti
{
    private static readonly Guid s_sede = Guid.CreateVersion7();
    private static readonly Guid s_tipo = Guid.CreateVersion7();

    private static DatiImpianto Impianto(string? stato = null, DateOnly? dismessoIl = null, string? pdr = null, int? anno = null, decimal? potenza = null) =>
        new(s_sede, s_tipo, stato, anno, potenza, null, pdr, null, null, "  ", dismessoIl);

    [Fact]
    public void Un_impianto_senza_stato_è_attivo_e_il_pdr_perde_gli_spazi()
    {
        var (dati, errori) = RegoleImpianti.Valida(Impianto(pdr: " 0001 2345 abc "));

        Assert.Empty(errori);
        Assert.Equal(StatiImpianto.Attivo, dati.Stato);
        Assert.Equal("00012345ABC", dati.Pdr);
        Assert.Null(dati.Note);
    }

    [Fact]
    public void Un_impianto_attivo_non_tiene_la_data_di_dismissione()
    {
        var (dati, errori) = RegoleImpianti.Valida(Impianto(stato: "Attivo", dismessoIl: new DateOnly(2026, 1, 1)));

        Assert.Empty(errori);
        Assert.Null(dati.DismessoIl);
    }

    [Fact]
    public void Un_impianto_dismesso_vuole_la_data()
    {
        Assert.Equal([ErroreImpianto.DataDismissioneMancante], RegoleImpianti.Valida(Impianto(stato: "dismesso")).Errori);
        Assert.Empty(RegoleImpianti.Valida(Impianto(stato: "dismesso", dismessoIl: new DateOnly(2026, 1, 1))).Errori);
        Assert.Equal([ErroreImpianto.StatoImpiantoNonValido], RegoleImpianti.Valida(Impianto(stato: "rottamato")).Errori);
    }

    [Theory]
    [InlineData(1899, null)]
    [InlineData(2101, null)]
    [InlineData(null, 0)]
    [InlineData(null, -1)]
    [InlineData(null, 10_000_000)]
    public void Anno_e_potenza_fuori_misura_non_passano(int? anno, int? potenza)
    {
        var errori = RegoleImpianti.Valida(Impianto(anno: anno, potenza: potenza)).Errori;

        Assert.Single(errori);
        Assert.Contains(errori[0], new[] { ErroreImpianto.AnnoInstallazioneNonValido, ErroreImpianto.PotenzaNonValida });
    }

    [Fact]
    public void Sede_e_tipo_sono_obbligatori()
    {
        var errori = RegoleImpianti.Valida(new DatiImpianto(null, Guid.Empty, null, null, null, null, null, null, null, null, null)).Errori;

        Assert.Equal([ErroreImpianto.SedeNonValida, ErroreImpianto.TipoImpiantoNonValido], errori);
    }

    [Fact]
    public void Un_generatore_non_si_dismette_prima_di_essere_installato()
    {
        var dati = new DatiGeneratore(s_tipo, " Marca ", null, " MAT-1 ", 24, null, new DateOnly(2020, 5, 1), new DateOnly(2020, 4, 30), null);

        var (normalizzati, errori) = RegoleImpianti.Valida(dati);

        Assert.Equal([ErroreImpianto.DateGeneratoreNonValide], errori);
        Assert.Equal("Marca", normalizzati.Marca);
        Assert.Equal("MAT-1", normalizzati.Matricola);
    }

    [Theory]
    [InlineData("Toscana", "siert")]
    [InlineData(" LOMBARDIA ", "curit")]
    public void Il_sistema_del_catasto_dipende_dalla_regione(string regione, string sistema)
    {
        var (dati, errori) = RegoleImpianti.Valida(new DatiRiferimentoCatasto(regione, "curit", " ABC123 ", null));

        Assert.Empty(errori);
        Assert.Equal(sistema, dati.Sistema);
        Assert.Equal("ABC123", dati.Codice);
    }

    [Fact]
    public void Regione_sconosciuta_e_codice_con_spazi_non_passano()
    {
        var errori = RegoleImpianti.Valida(new DatiRiferimentoCatasto("veneto", null, "AB 12", null)).Errori;

        Assert.Equal([ErroreImpianto.RegioneNonValida, ErroreImpianto.CodiceCatastoNonValido], errori);
    }
}

public sealed class TestServizioImpianti
{
    private static readonly Contesto s_contesto = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public async Task Con_l_ambito_propri_il_tecnico_non_vede_e_non_crea_impianti()
    {
        var repository = new RepositoryCheFallisce();
        var servizio = new ServizioImpianti(repository);
        var ct = TestContext.Current.CancellationToken;

        var pagina = await servizio.CercaAsync(s_contesto, AmbitoPermesso.Propri, new FiltroImpianti("x", null, null), 0, 1000, ct);
        Assert.Equal((0, 1, ServizioImpianti.DimensioneMassimaPagina), (pagina.Totale, pagina.NumeroPagina, pagina.Dimensione));
        Assert.Null(await servizio.ImpiantoAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), ct));

        var dati = new DatiImpianto(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, null, null, null, null);
        Assert.IsType<EsitoImpianto.Vietata>(await servizio.CreaAsync(s_contesto, AmbitoPermesso.Propri, dati, ct));
        Assert.IsType<EsitoImpianto.NonTrovata>(await servizio.AggiornaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), 1, dati, ct));
        Assert.IsType<EsitoImpianto.NonTrovata>(await servizio.EliminaRiferimentoAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), Guid.NewGuid(), 1, ct));
    }

    [Fact]
    public async Task I_dati_non_validi_non_arrivano_al_database()
    {
        var servizio = new ServizioImpianti(new RepositoryCheFallisce());

        var esito = await servizio.CreaAsync(
            s_contesto, AmbitoPermesso.Tutti, new DatiImpianto(null, null, null, null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);

        Assert.IsType<EsitoImpianto.NonValida>(esito);
    }

    /// <summary>Ogni chiamata al database fa fallire il test: qui non deve arrivarne nessuna.</summary>
    private sealed class RepositoryCheFallisce : IRepositoryImpianti
    {
        public Task<Pagina<RiepilogoImpianto>> CercaAsync(Contesto contesto, FiltroImpianti filtro, int numeroPagina, int dimensione, CancellationToken ct) => Fallisci<Pagina<RiepilogoImpianto>>();

        public Task<Impianto?> ImpiantoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct) => Fallisci<Impianto?>();

        public Task<VociImpianti> VociAsync(Contesto contesto, CancellationToken ct) => Fallisci<VociImpianti>();

        public Task<EsitoImpianto> CreaAsync(Contesto contesto, DatiImpianto dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> AggiornaAsync(Contesto contesto, Guid impiantoId, int versione, DatiImpianto dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> EliminaAsync(Contesto contesto, Guid impiantoId, int versione, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> CreaGeneratoreAsync(Contesto contesto, Guid impiantoId, DatiGeneratore dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> AggiornaGeneratoreAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, DatiGeneratore dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> CreaRiferimentoAsync(Contesto contesto, Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> AggiornaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, DatiRiferimentoCatasto dati, CancellationToken ct) => Fallisci<EsitoImpianto>();

        public Task<EsitoImpianto> EliminaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct) => Fallisci<EsitoImpianto>();

        private static Task<T> Fallisci<T>() => throw new InvalidOperationException("Il servizio non doveva chiamare il database.");
    }
}

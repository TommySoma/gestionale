using Gestionale.Application.Anagrafica;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Tests.Unit;

public sealed class TestRegoleInterventi
{
    private static readonly Guid s_impianto = Guid.CreateVersion7();
    private static readonly Guid s_tecnico = Guid.CreateVersion7();
    private static readonly DateTimeOffset s_inizio = new(2027, 1, 11, 9, 0, 0, TimeSpan.FromHours(1));

    private static DatiIntervento Intervento(string? tipo = "manutenzione", DateTimeOffset? inizio = null, DateTimeOffset? fine = null, Guid? tecnico = null) =>
        new(s_impianto, Guid.Empty, tipo, "  ", inizio, fine, tecnico);

    private static DatiRiga Riga(string? tipo = "materiale", string? unita = "pz", decimal? quantita = 1, decimal? prezzo = 10) =>
        new(tipo, null, "Ricambio", unita, quantita, prezzo, null, null);

    [Fact]
    public void Un_intervento_senza_finestra_è_valido_e_viene_normalizzato()
    {
        var (dati, errori) = RegoleInterventi.Valida(Intervento(tipo: " Riparazione "));

        Assert.Empty(errori);
        Assert.Equal("riparazione", dati.Tipo);
        Assert.Null(dati.Descrizione);
        Assert.Null(dati.GeneratoreId);
    }

    [Fact]
    public void La_finestra_vuole_inizio_e_fine_in_ordine_e_un_tecnico()
    {
        Assert.Equal([ErroreIntervento.FinestraIncompleta], RegoleInterventi.Valida(Intervento(inizio: s_inizio)).Errori);
        Assert.Equal(
            [ErroreIntervento.FinestraNonValida, ErroreIntervento.FinestraSenzaTecnico],
            RegoleInterventi.Valida(Intervento(inizio: s_inizio, fine: s_inizio)).Errori);
        Assert.Equal(
            [ErroreIntervento.FinestraTroppoLunga],
            RegoleInterventi.Valida(Intervento(inizio: s_inizio, fine: s_inizio.AddDays(40), tecnico: s_tecnico)).Errori);
        Assert.Empty(RegoleInterventi.Valida(Intervento(inizio: s_inizio, fine: s_inizio.AddHours(2), tecnico: s_tecnico)).Errori);
    }

    [Fact]
    public void Il_tipo_e_l_impianto_sono_obbligatori()
    {
        var (_, errori) = RegoleInterventi.Valida(new DatiIntervento(null, null, "pulizia", null, null, null, null));

        Assert.Equal([ErroreIntervento.ImpiantoInterventoNonValido, ErroreIntervento.TipoInterventoNonValido], errori);
    }

    [Fact]
    public void La_manodopera_senza_unità_si_conta_in_ore()
    {
        var (dati, errori) = RegoleInterventi.Valida(new DatiChiusura("Risolto", " ", [Riga(tipo: "Manodopera", unita: null)]));

        Assert.Empty(errori);
        Assert.Equal("risolto", dati.Esito);
        Assert.Null(dati.Note);
        var riga = Assert.Single(dati.Righe!);
        Assert.Equal(("manodopera", "h", 0m), (riga.Tipo, riga.Unita, riga.ScontoPercentuale));
    }

    [Fact]
    public void Gli_errori_delle_righe_compaiono_una_volta_sola()
    {
        var righe = new[]
        {
            Riga(tipo: "manodopera", unita: "pz"),
            Riga(quantita: 0),
            Riga(quantita: -1),
            Riga(prezzo: 1.234m),
            Riga(quantita: 1.2345m),
        };

        var (_, errori) = RegoleInterventi.Valida(new DatiChiusura("boh", null, righe));

        Assert.Equal(
            [ErroreIntervento.EsitoInterventoNonValido, ErroreIntervento.ManodoperaNonInOre, ErroreIntervento.RigaQuantitaNonValida, ErroreIntervento.RigaPrezzoNonValido],
            errori);
    }

    [Fact]
    public void Troppe_righe_sono_un_errore()
    {
        var righe = Enumerable.Repeat(Riga(), RegoleInterventi.RigheMassime + 1).ToList();

        Assert.Contains(ErroreIntervento.TroppeRighe, RegoleInterventi.Valida(new DatiChiusura("risolto", null, righe)).Errori);
    }
}

public sealed class TestServizioInterventi
{
    private static readonly Contesto s_contesto = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public async Task Con_l_ambito_propri_si_vedono_e_si_chiudono_solo_i_propri()
    {
        var repository = new RepositoryRegistra();
        var servizio = new ServizioInterventi(repository);
        var ct = TestContext.Current.CancellationToken;

        await servizio.CercaAsync(s_contesto, AmbitoPermesso.Propri, new FiltroInterventi(TecnicoUtenteId: Guid.NewGuid(), Stato: " Pianificato "), 0, 5000, ct);
        Assert.Equal((s_contesto.UtenteId, "pianificato", 1, ServizioInterventi.DimensioneMassimaPagina), (repository.Filtro!.TecnicoUtenteId, repository.Filtro.Stato, repository.Pagina, repository.Dimensione));

        await servizio.InterventoAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), ct);
        Assert.Equal(s_contesto.UtenteId, repository.SoloTecnico);

        await servizio.AvviaAsync(s_contesto, AmbitoPermesso.Tutti, Guid.NewGuid(), 1, ct);
        Assert.Null(repository.SoloTecnico);

        await servizio.ChiudiAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), 1, new DatiChiusura("risolto", null, null), ct);
        Assert.Equal(s_contesto.UtenteId, repository.SoloTecnico);
    }

    [Fact]
    public async Task Con_l_ambito_propri_non_si_crea_non_si_pianifica_e_non_si_annulla()
    {
        var servizio = new ServizioInterventi(new RepositoryRegistra());
        var ct = TestContext.Current.CancellationToken;
        var dati = new DatiIntervento(Guid.NewGuid(), null, "manutenzione", null, null, null, null);

        Assert.IsType<EsitoIntervento.Vietata>(await servizio.CreaAsync(s_contesto, AmbitoPermesso.Propri, dati, ct));
        Assert.IsType<EsitoIntervento.Vietata>(await servizio.AggiornaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), 1, dati, ct));
        Assert.IsType<EsitoIntervento.Vietata>(await servizio.AnnullaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), 1, "Motivo", ct));
    }

    [Fact]
    public async Task I_dati_non_validi_non_arrivano_al_database()
    {
        var servizio = new ServizioInterventi(new RepositoryRegistra());
        var ct = TestContext.Current.CancellationToken;

        var esito = await servizio.CreaAsync(s_contesto, AmbitoPermesso.Tutti, new DatiIntervento(null, null, null, null, null, null, null), ct);
        Assert.IsType<EsitoIntervento.NonValida>(esito);

        var annullamento = await servizio.AnnullaAsync(s_contesto, AmbitoPermesso.Tutti, Guid.NewGuid(), 1, "   ", ct);
        Assert.Equal([ErroreIntervento.MotivoAnnullamentoMancante], Assert.IsType<EsitoIntervento.NonValida>(annullamento).Errori);
    }

    /// <summary>Registra ciò che riceve; le scritture che non devono arrivare al database fanno fallire il test.</summary>
    private sealed class RepositoryRegistra : IRepositoryInterventi
    {
        public FiltroInterventi? Filtro { get; private set; }

        public int Pagina { get; private set; }

        public int Dimensione { get; private set; }

        public Guid? SoloTecnico { get; private set; }

        public Task<Pagina<RiepilogoIntervento>> CercaAsync(Contesto contesto, FiltroInterventi filtro, int numeroPagina, int dimensione, CancellationToken ct)
        {
            (Filtro, Pagina, Dimensione) = (filtro, numeroPagina, dimensione);
            return Task.FromResult(new Pagina<RiepilogoIntervento>([], 0, numeroPagina, dimensione));
        }

        public Task<Intervento?> InterventoAsync(Contesto contesto, Guid interventoId, Guid? soloTecnico, CancellationToken ct)
        {
            SoloTecnico = soloTecnico;
            return Task.FromResult<Intervento?>(null);
        }

        public Task<IReadOnlyList<TecnicoAssegnabile>> TecniciAsync(Contesto contesto, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TecnicoAssegnabile>>([]);

        public Task<EsitoIntervento> AvviaAsync(Contesto contesto, Guid interventoId, int versione, Guid? soloTecnico, CancellationToken ct)
        {
            SoloTecnico = soloTecnico;
            return Task.FromResult<EsitoIntervento>(new EsitoIntervento.NonTrovata());
        }

        public Task<EsitoIntervento> ChiudiAsync(Contesto contesto, Guid interventoId, int versione, DatiChiusura dati, Guid? soloTecnico, CancellationToken ct)
        {
            SoloTecnico = soloTecnico;
            return Task.FromResult<EsitoIntervento>(new EsitoIntervento.NonTrovata());
        }

        public Task<EsitoIntervento> CreaAsync(Contesto contesto, DatiIntervento dati, CancellationToken ct) => Fallisci();

        public Task<EsitoIntervento> AggiornaAsync(Contesto contesto, Guid interventoId, int versione, DatiIntervento dati, CancellationToken ct) => Fallisci();

        public Task<EsitoIntervento> AnnullaAsync(Contesto contesto, Guid interventoId, int versione, string motivo, CancellationToken ct) => Fallisci();

        private static Task<EsitoIntervento> Fallisci() => throw new InvalidOperationException("Il servizio non doveva chiamare il database.");
    }
}

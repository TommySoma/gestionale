using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Scadenze;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Scadenze;
using Gestionale.Domain.Sicurezza;
using Gestionale.Tests.Unit.Schermate;

namespace Gestionale.Tests.Unit;

public sealed class TestRegoleScadenze
{
    private static readonly DateOnly s_oggi = new(2026, 11, 25);

    [Fact]
    public void Un_richiamo_valido_viene_normalizzato()
    {
        var (dati, errori) = RegoleScadenze.Valida(
            new DatiRichiamo(" Telefono ", "Da_Richiamare", s_oggi.AddDays(3), Guid.Empty, "  "), s_oggi);

        Assert.Empty(errori);
        Assert.Equal(("telefono", "da_richiamare"), (dati.Canale, dati.Esito));
        Assert.Null(dati.InterventoId);
        Assert.Null(dati.Note);
    }

    [Fact]
    public void Gli_errori_di_un_richiamo_arrivano_tutti_insieme()
    {
        var errori = RegoleScadenze.Valida(new DatiRichiamo("piccione", "forse", null, null, new string('x', 2001)), s_oggi).Errori;

        Assert.Equal(
            [ErroreScadenza.CanaleRichiamoNonValido, ErroreScadenza.EsitoRichiamoNonValido, ErroreScadenza.NoteRichiamoTroppoLunghe],
            errori);
    }

    [Fact]
    public void Data_per_risentirsi_e_intervento_dipendono_dall_esito()
    {
        Assert.Equal(
            [ErroreScadenza.RichiamareIlSoloDaRisentire, ErroreScadenza.InterventoSoloConAppuntamento],
            RegoleScadenze.Valida(new DatiRichiamo("email", "rifiutato", s_oggi, Guid.CreateVersion7(), null), s_oggi).Errori);
        Assert.Equal(
            [ErroreScadenza.RichiamareIlNelPassato],
            RegoleScadenze.Valida(new DatiRichiamo("sms", "nessuna_risposta", s_oggi.AddDays(-1), null, null), s_oggi).Errori);
        Assert.Empty(RegoleScadenze.Valida(new DatiRichiamo("telefono", "appuntamento", null, Guid.CreateVersion7(), null), s_oggi).Errori);
    }

    [Fact]
    public void La_data_corretta_a_mano_resta_entro_dieci_anni()
    {
        Assert.Equal([ErroreScadenza.DataScadenzaMancante], RegoleScadenze.ValidaData(null, s_oggi));
        Assert.Equal([ErroreScadenza.DataScadenzaFuoriIntervallo], RegoleScadenze.ValidaData(s_oggi.AddYears(11), s_oggi));
        Assert.Empty(RegoleScadenze.ValidaData(s_oggi.AddMonths(-2), s_oggi));
    }

    [Fact]
    public void La_periodicità_di_manutenzione_va_da_1_a_120_mesi()
    {
        var impianto = new DatiImpianto(Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, null, null, null, null, null);

        Assert.Empty(RegoleImpianti.Valida(impianto).Errori);
        Assert.Empty(RegoleImpianti.Valida(impianto with { ManutenzioneMesi = 12 }).Errori);
        Assert.Equal([ErroreImpianto.PeriodicitaManutenzioneNonValida], RegoleImpianti.Valida(impianto with { ManutenzioneMesi = 0 }).Errori);
        Assert.Equal([ErroreImpianto.PeriodicitaManutenzioneNonValida], RegoleImpianti.Valida(impianto with { ManutenzioneMesi = 121 }).Errori);
    }

    [Fact]
    public void La_chiusura_conserva_la_spunta_del_controllo()
    {
        var (dati, errori) = RegoleInterventi.Valida(new DatiChiusura("Risolto", null, [], ControlloEfficienza: true));

        Assert.Empty(errori);
        Assert.True(dati.ControlloEfficienza);
    }

    [Fact]
    public void Ogni_errore_nuovo_ha_il_suo_testo()
    {
        var nomi = Enum.GetNames<ErroreScadenza>()
            .Append(nameof(ErroreImpianto.PeriodicitaManutenzioneNonValida))
            .Append(nameof(ErroreIntervento.ControlloSoloManutenzione))
            .Concat(new[] { StatiScadenza.Aperta, StatiScadenza.Chiusa, StatiScadenza.Annullata }.Select(s => "StatoScadenza_" + s));

        Assert.All(nomi, nome => Assert.False(string.IsNullOrWhiteSpace(Messaggi.ResourceManager.GetString(nome, Messaggi.Culture)), nome));
    }
}

public sealed class TestServizioScadenze
{
    private static readonly Contesto s_contesto = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Il_tecnico_vede_solo_i_propri_impianti_e_senza_telefono_né_richiami()
    {
        var repository = new RepositoryFinto();
        var servizio = new ServizioScadenze(repository, new OrologioFisso(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero)));

        var (pagina, errori) = await servizio.InArrivoAsync(s_contesto, AmbitoPermesso.Propri, new FiltroScadenze(30, " Manutenzione "), 1, 50, Ct);

        Assert.Empty(errori);
        Assert.Equal(s_contesto.UtenteId, repository.UltimoFiltro!.TecnicoUtenteId);
        Assert.Equal("manutenzione", repository.UltimoFiltro.Tipo);
        var riga = Assert.Single(pagina!.Elementi);
        Assert.Equal("Neri Giulia", riga.Cliente);
        Assert.Null(riga.Telefono);
        Assert.Null(riga.UltimoEsito);

        var scadenza = await servizio.ScadenzaAsync(s_contesto, AmbitoPermesso.Propri, Guid.CreateVersion7(), Ct);
        Assert.Empty(scadenza!.Richiami);
    }

    [Fact]
    public async Task L_ufficio_vede_tutto_e_i_giorni_sono_controllati()
    {
        var repository = new RepositoryFinto();
        var servizio = new ServizioScadenze(repository, TimeProvider.System);

        var (pagina, _) = await servizio.InArrivoAsync(s_contesto, AmbitoPermesso.Tutti, new FiltroScadenze(30), 1, 50, Ct);
        Assert.Null(repository.UltimoFiltro!.TecnicoUtenteId);
        Assert.Equal("+39 333 000 0001", Assert.Single(pagina!.Elementi).Telefono);
        Assert.Single((await servizio.ScadenzaAsync(s_contesto, AmbitoPermesso.Tutti, Guid.CreateVersion7(), Ct))!.Richiami);

        var (nessuna, errori) = await servizio.InArrivoAsync(s_contesto, AmbitoPermesso.Tutti, new FiltroScadenze(-1), 1, 50, Ct);
        Assert.Null(nessuna);
        Assert.Equal([ErroreScadenza.GiorniNonValidi], errori);
    }

    [Fact]
    public async Task Le_richieste_non_valide_non_arrivano_al_database()
    {
        var repository = new RepositoryFinto();
        var servizio = new ServizioScadenze(repository, TimeProvider.System);

        var data = await servizio.CorreggiDataAsync(s_contesto, Guid.CreateVersion7(), 1, null, Ct);
        var richiamo = await servizio.RegistraRichiamoAsync(s_contesto, Guid.CreateVersion7(), 1, new DatiRichiamo("telefono", null, null, null, null), Ct);

        Assert.Equal([ErroreScadenza.DataScadenzaMancante], Assert.IsType<EsitoScadenza.NonValida>(data).Errori);
        Assert.Equal([ErroreScadenza.EsitoRichiamoNonValido], Assert.IsType<EsitoScadenza.NonValida>(richiamo).Errori);
        Assert.Equal(0, repository.Scritture);
    }

    [Fact]
    public void Oggi_è_la_data_italiana()
    {
        // Alle 23:30 UTC del 25 novembre in Italia è già il 26.
        var servizio = new ServizioScadenze(new RepositoryFinto(), new OrologioFisso(new DateTimeOffset(2026, 11, 25, 23, 30, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 11, 26), servizio.Oggi());
    }

    private sealed class RepositoryFinto : IRepositoryScadenze
    {
        public FiltroScadenze? UltimoFiltro { get; private set; }

        public int Scritture { get; private set; }

        public Task<Pagina<ScadenzaInArrivo>> InArrivoAsync(Contesto contesto, FiltroScadenze filtro, int numeroPagina, int dimensione, CancellationToken ct)
        {
            UltimoFiltro = filtro;
            var riga = new ScadenzaInArrivo
            {
                ScadenzaId = Guid.CreateVersion7(),
                Tipo = TipiScadenza.Manutenzione,
                Cliente = "Neri Giulia",
                Telefono = "+39 333 000 0001",
                UltimoEsito = EsitiRichiamo.NessunaRisposta,
            };
            return Task.FromResult(new Pagina<ScadenzaInArrivo>([riga], 1, numeroPagina, dimensione));
        }

        public Task<Scadenza?> ScadenzaAsync(Contesto contesto, Guid scadenzaId, Guid? soloTecnico, CancellationToken ct) =>
            Task.FromResult<Scadenza?>(new Scadenza { ScadenzaId = scadenzaId, Richiami = [new Richiamo { Esito = EsitiRichiamo.Appuntamento }] });

        public Task<IReadOnlyList<Scadenza>?> DellImpiantoAsync(Contesto contesto, Guid impiantoId, Guid? soloTecnico, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Scadenza>?>([]);

        public Task<EsitoScadenza> CorreggiDataAsync(Contesto contesto, Guid scadenzaId, int versione, DateOnly data, CancellationToken ct)
        {
            Scritture++;
            return Task.FromResult<EsitoScadenza>(new EsitoScadenza.Riuscita(scadenzaId, versione + 1));
        }

        public Task<EsitoScadenza> RegistraRichiamoAsync(Contesto contesto, Guid scadenzaId, int versione, DatiRichiamo dati, CancellationToken ct)
        {
            Scritture++;
            return Task.FromResult<EsitoScadenza>(new EsitoScadenza.Riuscita(scadenzaId, versione + 1));
        }
    }
}

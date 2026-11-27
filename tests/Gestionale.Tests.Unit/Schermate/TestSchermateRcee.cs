using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>M4: rapporti di controllo, bollini e invii al SIERT nel client.</summary>
public sealed class TestSchermateRcee
{
    private static readonly Permesso[] s_responsabile =
    [
        .. Schermate.ResponsabileTecnico,
        new("rapporti_controllo.lettura", "tutti"),
        new("rapporti_controllo.scrittura", "tutti"),
        new("rapporti_controllo.firma", "tutti"),
        new("bollini.lettura", "tutti"),
        new("bollini.scrittura", "tutti"),
    ];

    [Fact]
    public Task Elenco_dei_rapporti_con_il_termine_vicino() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(s_responsabile);
        Assert.True(shell.VedeRapporti && shell.VedeBollini && shell.VedeInvii);

        await shell.ApriRapportiCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "40-rapporti");

        var elenco = Assert.IsType<RapportiViewModel>(shell.Contenuto);
        Assert.Equal(2, elenco.Righe.Count);
        Assert.True(elenco.Righe[0].TermineVicino);
        Assert.False(elenco.Righe[1].TermineVicino);
        Assert.Contains(Testi.SenzaBollino, testi);
        Assert.Contains("2027000000101", testi);
    });

    [Fact]
    public Task Scheda_del_rapporto_in_bozza_con_gli_errori() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(s_responsabile);

        await shell.ApriRapportoAsync(DatiFittizi.RapportoBozza);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "41-scheda-rapporto");

        var scheda = Assert.IsType<SchedaRapportoViewModel>(shell.Contenuto);
        Assert.True(scheda.PuoModificare && scheda.PuoConvalidare && scheda.PuoAssegnareBollino);
        Assert.False(scheda.PuoFirmare);

        // Solo le colonne con origine "dati" entrano nel modulo.
        Assert.Equal(8, scheda.Campi.Count);
        Assert.DoesNotContain(scheda.Campi, c => c.Chiave is "codice_ditta_siert" or "codice" or "data_controllo");

        // Codici noti con il loro nome, gli altri come codice; i numeri all'italiana.
        var riflussi = scheda.Campi.Single(c => c.Chiave == "presenza_riflussi");
        Assert.Equal(Testi.CodiceNo, riflussi.Scelta?.Nome);
        var trattamento = scheda.Campi.Single(c => c.Chiave == "trattamento_riscaldamento");
        Assert.Contains(trattamento.Opzioni!, o => o.Nome == string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.CodiceNumerico, "17"));
        Assert.Equal("24,5", scheda.Campi.Single(c => c.Chiave == "potenza_utile").Testo);
        Assert.NotNull(scheda.Campi.Single(c => c.Chiave == "data_installazione").Data);

        // L'errore di un campo va sotto il campo; quello del bollino in cima.
        Assert.Equal("campo obbligatorio", scheda.Campi.Single(c => c.Chiave == "temperatura_fumi").Errore);
        Assert.Contains(scheda.ErroriGenerali, e => e.Contains("bollino", StringComparison.Ordinal));
        Assert.Single(scheda.Avvisi);
        Assert.True(scheda.TermineVicino);
        Assert.Contains(Testi.DisclaimerRapporto, testi);

        // Il salvataggio manda la codifica del tracciato: punto decimale, codici come testo, date ISO.
        scheda.Campi.Single(c => c.Chiave == "temperatura_fumi").Testo = "68,2";
        await scheda.SalvaCommand.ExecuteAsync(null);
        var corpo = DatiFittizi.UltimaScritturaRcee!;
        Assert.Contains("\"temperatura_fumi\":\"68.2\"", corpo, StringComparison.Ordinal);
        Assert.Contains("\"potenza_utile\":\"24.5\"", corpo, StringComparison.Ordinal);
        Assert.Contains("\"presenza_riflussi\":\"4\"", corpo, StringComparison.Ordinal);
        Assert.Contains("\"data_installazione\":\"2018-03-15\"", corpo, StringComparison.Ordinal);
        Assert.Equal(Testi.RapportoSalvato, scheda.Avviso);

        // Un numero che non si legge non parte.
        scheda.Campi.Single(c => c.Chiave == "temperatura_fumi").Testo = "caldo";
        await scheda.SalvaCommand.ExecuteAsync(null);
        Assert.NotNull(scheda.Errore);
    });

    [Fact]
    public Task Rapporto_firmato_in_sola_lettura() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriAsync(s_responsabile);

        await shell.ApriRapportoAsync(DatiFittizi.RapportoFirmato);
        var scheda = Assert.IsType<SchedaRapportoViewModel>(shell.Contenuto);
        Assert.True(scheda.SolaLettura);
        Assert.False(scheda.PuoAssegnareBollino || scheda.PuoLiberareBollino || scheda.PuoConvalidare);
        Assert.True(scheda.PuoAnnullare);
        Assert.Empty(scheda.ErroriGenerali);
    });

    [Fact]
    public Task Bollino_assegnato_dal_tipo_della_categoria_giusta() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(s_responsabile);
        await shell.ApriRapportoAsync(DatiFittizi.RapportoBozza);
        var scheda = Assert.IsType<SchedaRapportoViewModel>(shell.Contenuto);

        await scheda.AssegnaBollinoCommand.ExecuteAsync(null);
        var dialogo = Assert.IsType<AssegnaBollinoViewModel>(shell.Dialogo);
        Schermate.Attendi();
        Schermate.Salva(finestra, "42-assegna-bollino");

        // Un rapporto tipo 1 vuole un bollino GT: il tipo GF non compare.
        Assert.Equal(DatiFittizi.TipoBollinoGt, Assert.Single(dialogo.Tipi).Id);
        await dialogo.SalvaCommand.ExecuteAsync(null);
        Assert.Null(dialogo.Errore);
        Assert.Contains(DatiFittizi.TipoBollinoGt.ToString(), DatiFittizi.UltimaScritturaRcee, StringComparison.Ordinal);
    });

    [Fact]
    public Task Registro_dei_bollini_e_registrazione_dei_codici() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(s_responsabile);

        await shell.ApriBolliniCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "43-bollini");

        var bollini = Assert.IsType<BolliniViewModel>(shell.Contenuto);
        Assert.Equal(2, bollini.Tipi.Count);
        Assert.True(bollini.Tipi[1].Esaurito);
        Assert.True(bollini.Righe[0].Annullabile);
        Assert.False(bollini.Righe[1].Annullabile);
        Assert.Contains("2027000000100", testi);

        await bollini.RegistraCommand.ExecuteAsync(null);
        var registra = Assert.IsType<RegistraBolliniViewModel>(shell.Dialogo);
        registra.Codici = "2027000000200\n2027000000201, 2027000000202\n2027000000200";
        Assert.Equal(3, registra.CodiciLetti.Count);
        await registra.SalvaCommand.ExecuteAsync(null);
        Assert.Null(registra.Errore);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.BolliniRegistrati, 3), bollini.Avviso);
    });

    [Fact]
    public Task Invio_caricato_con_gli_esiti_da_registrare() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(s_responsabile);

        await shell.ApriInviiCommand.ExecuteAsync(null);
        var invii = Assert.IsType<InviiViewModel>(shell.Contenuto);
        Assert.Single(invii.Righe);

        await shell.ApriInvioAsync(DatiFittizi.InvioCaricato, null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "44-scheda-invio");

        var scheda = Assert.IsType<SchedaInvioViewModel>(shell.Contenuto);
        Assert.True(scheda.PuoRegistrareEsiti);
        Assert.False(scheda.PuoSegnareCaricato || scheda.PuoAnnullare);
        Assert.Contains("rcee-tipo1-20270112-1.txt", testi);

        scheda.RegistraEsitiCommand.Execute(null);
        var esiti = Assert.IsType<EsitiInvioViewModel>(shell.Dialogo);
        Schermate.Attendi();
        Schermate.Salva(finestra, "45-esiti-invio");

        // Uno scarto senza causa non parte.
        esiti.Righe[0].Esito = esiti.Righe[0].Esiti.Single(e => e.Codice == "scartato");
        await esiti.SalvaCommand.ExecuteAsync(null);
        Assert.NotNull(esiti.Errore);

        esiti.Righe[0].Causa = "Codice catasto inesistente (prova)";
        await esiti.SalvaCommand.ExecuteAsync(null);
        Assert.Null(esiti.Errore);
        Assert.Contains("\"esito\":\"scartato\"", DatiFittizi.UltimaScritturaRcee, StringComparison.Ordinal);
    });

    [Fact]
    public Task Preparazione_di_un_invio() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriAsync(s_responsabile);
        await shell.ApriInviiCommand.ExecuteAsync(null);
        var invii = Assert.IsType<InviiViewModel>(shell.Contenuto);

        await invii.PreparaTipo1Command.ExecuteAsync(null);
        var scheda = Assert.IsType<SchedaInvioViewModel>(shell.Contenuto);
        Assert.Contains("\"modello\":\"tipo_1\"", DatiFittizi.UltimaScritturaRcee, StringComparison.Ordinal);
        Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.RapportiEsclusi, 1), scheda.Avviso, StringComparison.Ordinal);
    });

    [Fact]
    public Task Il_tecnico_non_vede_gli_invii() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico",
            [.. Schermate.Tecnico, new("rapporti_controllo.lettura", "propri"), new("rapporti_controllo.scrittura", "propri"), new("bollini.lettura", "propri")]);

        Assert.True(shell.VedeRapporti && shell.VedeBollini && shell.CompilaRapporti);
        Assert.False(shell.VedeInvii || shell.FirmaRapporti || shell.CreaRapportiLiberi || shell.GestisceBollini);
    });
}

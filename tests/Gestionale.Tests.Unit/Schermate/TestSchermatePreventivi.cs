using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>Le schermate di listino e preventivi disegnate davvero, in tema chiaro e scuro, con dati fittizi.</summary>
public sealed class TestSchermatePreventivi
{
    private static Task<(ShellViewModel Shell, Avalonia.Controls.Window Finestra)> ApriAmministrativoAsync() =>
        Schermate.ApriComeAsync(Guid.NewGuid(), "Paola", "Ferri", "amministrativo", Schermate.Amministrativo);

    [Fact]
    public Task Elenco_dei_preventivi() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();

        await shell.ApriPreventiviCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "21-preventivi");

        var elenco = Assert.IsType<PreventiviViewModel>(shell.Contenuto);
        Assert.True(shell.PreventiviAttivo);
        Assert.Equal([Testi.StatoPreventivoBozza, "Preventivo 7/2027", "Preventivo 6/2027"], elenco.Righe.Select(r => r.Numero));
        Assert.True(elenco.Righe[1].Scaduto);
        Assert.Contains(Testi.StatoPreventivoScaduto, testi);
        Assert.Contains(Testi.NuovoPreventivo, testi);
        Assert.Contains("18.300,00 €", testi);
    });

    [Fact]
    public Task Scheda_di_un_preventivo_emesso_e_scaduto() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();

        await shell.ApriPreventivoAsync(DatiFittizi.PreventivoEmesso);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "22-scheda-preventivo");

        var scheda = Assert.IsType<SchedaPreventivoViewModel>(shell.Contenuto);
        Assert.True(scheda.PuoRispondere);
        Assert.True(scheda.PuoAnnullare);
        Assert.False(scheda.PuoModificare);
        Assert.Equal("preventivo-2027-0007.pdf", scheda.NomePdf);
        Assert.Contains(Testi.AvvisoPreventivoScaduto, testi);
        Assert.Contains("192,15 €", testi);
        Assert.Contains("MAN-01 · Manodopera tecnico (prova)", testi);
        Assert.Contains(Testi.SegnaAccettato, testi);
    });

    [Fact]
    public Task Editor_della_bozza_con_le_voci_del_listino() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();
        await shell.ApriPreventivoAsync(DatiFittizi.PreventivoBozza);
        var scheda = Assert.IsType<SchedaPreventivoViewModel>(shell.Contenuto);
        Assert.True(scheda.PuoModificare);
        Assert.Equal("bozza-di-preventivo.pdf", scheda.NomePdf);

        await scheda.ModificaCommand.ExecuteAsync(null);
        var editor = Assert.IsType<EditorPreventivoViewModel>(shell.Dialogo);

        // Sede e impianto della bozza restano scelti; gli impianti sono quelli della sede.
        Assert.Equal(DatiFittizi.SedeFirenze, editor.Sede?.Id);
        Assert.Equal(DatiFittizi.Caldaia, editor.Impianto?.Id);
        Assert.DoesNotContain(editor.Impianti, i => i.Id == DatiFittizi.Dismesso);
        Assert.Equal(2, editor.Righe.Count);
        Assert.Equal(FormatoInterventi.Importo(192.15m), editor.Totale);

        // Una voce dal listino entra con il suo prezzo e la sua IVA.
        editor.RicercaListino = "vaso";
        await editor.CercaListinoCommand.ExecuteAsync(null);
        editor.AggiungiVoceCommand.Execute(editor.VociTrovate.Single(v => v.Codice == "VAS-08"));
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "23-editor-preventivo");

        Assert.Equal(3, editor.Righe.Count);
        Assert.Equal(DatiFittizi.VoceVaso, editor.Righe[2].VoceId);
        // 247,50 di imponibile, IVA 22% = 54,45: totale 301,95.
        Assert.Equal(FormatoInterventi.Importo(301.95m), editor.Totale);
        Assert.Contains(Testi.TitoloModificaPreventivo, testi);

        editor.Validita = "mille";
        await editor.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.ValiditaNonValida, editor.Errore);
    });

    [Fact]
    public Task Dall_accettato_l_intervento_e_dal_listino_la_voce() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();
        await shell.ApriPreventivoAsync(DatiFittizi.PreventivoAccettato);
        var scheda = Assert.IsType<SchedaPreventivoViewModel>(shell.Contenuto);

        // Questo ha già generato l'intervento: si apre, non se ne crea un altro.
        Assert.True(scheda.HaIntervento);
        Assert.False(scheda.PuoGenerareIntervento);
        Assert.False(scheda.PuoAnnullare);

        var preventivo = (await shell.Api.PreventivoAsync(DatiFittizi.PreventivoEmesso)).Valore!;
        var dialogo = new InterventoDaPreventivoViewModel(shell, preventivo, id => _ = shell.ApriInterventoAsync(id));
        shell.MostraDialogo(dialogo);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "24-genera-intervento");

        Assert.Contains(Testi.SpiegazioneGeneraIntervento, testi);
        await dialogo.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.ScegliTipoIntervento, dialogo.Errore);

        dialogo.Tipo = dialogo.Tipi.Single(t => t.Codice == "riparazione");
        await dialogo.SalvaCommand.ExecuteAsync(null);
        Assert.Null(shell.Dialogo);
    });

    [Fact]
    public Task Listino_con_le_voci_e_il_nuovo_prezzo() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();

        await shell.ApriListinoCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "25-listino");

        var listino = Assert.IsType<ListinoViewModel>(shell.Contenuto);
        Assert.True(shell.ListinoAttivo);
        Assert.Equal(3, listino.Righe.Count);
        Assert.Contains(Testi.SenzaPrezzo, testi);
        Assert.Contains("45,00 € dal 1 gennaio 2027", testi);

        listino.NuovaVoceCommand.Execute(null);
        var editor = Assert.IsType<EditorVoceViewModel>(shell.Dialogo);
        editor.Tipo = editor.Tipi.Single(t => t.Codice == "manodopera");
        Assert.Equal("h", editor.Unita);
        Assert.True(editor.ConPrezzoIniziale);

        shell.ChiudiDialogo();
        listino.NuovoPrezzoCommand.Execute(listino.Righe[0]);
        var prezzo = Assert.IsType<PrezzoVoceViewModel>(shell.Dialogo);
        Schermate.Attendi();
        testi = Schermate.Salva(finestra, "26-nuovo-prezzo");
        Assert.Contains(Testi.SpiegazioneNuovoPrezzo, testi);

        prezzo.Prezzo = "quaranta";
        await prezzo.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.PrezzoNonNumerico, prezzo.Errore);
    });

    [Fact]
    public Task Il_tecnico_non_vede_i_preventivi() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);

        Assert.False(shell.VedePreventivi);
        Assert.False(shell.GestisceListino);
    });
}

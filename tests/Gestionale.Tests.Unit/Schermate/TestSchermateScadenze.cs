using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>Le schermate delle scadenze disegnate davvero, in tema chiaro e scuro, con dati fittizi.</summary>
public sealed class TestSchermateScadenze
{
    [Fact]
    public Task Lista_da_richiamare() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "16-scadenze");

        var scadenze = Assert.IsType<ScadenzeViewModel>(shell.Contenuto);
        Assert.True(shell.ScadenzeAttivo);
        Assert.Equal([LivelloScadenza.Scaduta, LivelloScadenza.InScadenza, LivelloScadenza.InScadenza], scadenze.Righe.Select(r => r.Livello));
        Assert.Contains(Testi.ScadenzeTitolo, testi);
        Assert.Contains("Scaduta da 3 giorni", testi);
        Assert.Contains(Testi.ScadeOggi, testi);
        Assert.Contains("Tra 12 giorni", testi);
        Assert.Contains(Testi.AvvisoUnaScaduta, testi);
        Assert.Contains(Testi.TipoScadenzaControllo, testi);
        Assert.Contains("Ultimo: nessuna risposta, 12 gennaio 2027, 10:30 · risentire il 14 gennaio 2027", testi);
        Assert.Contains(Testi.RegistraRichiamo, testi);
    });

    [Fact]
    public Task Dialogo_del_richiamo() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        var scadenze = Assert.IsType<ScadenzeViewModel>(shell.Contenuto);

        scadenze.RegistraRichiamoCommand.Execute(scadenze.Righe[0]);
        var richiamo = Assert.IsType<RichiamoViewModel>(shell.Dialogo);
        richiamo.Esito = richiamo.Esiti.Single(e => e.Codice == "da_richiamare");
        richiamo.RichiamareIl = new DateTime(2027, 1, 15);
        richiamo.Note = "Preferisce essere richiamato dopo le 18";
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "17-richiamo");

        Assert.True(richiamo.ConData);
        Assert.Contains(Testi.TitoloRichiamo, testi);
        Assert.Contains("+39 333 000 0000", testi);

        // Con l'appuntamento la data per risentirsi non serve più.
        richiamo.Esito = richiamo.Esiti.Single(e => e.Codice == "appuntamento");
        Assert.False(richiamo.ConData);
        Assert.True(richiamo.ConAppuntamento);

        await richiamo.SalvaCommand.ExecuteAsync(null);
        Assert.Null(richiamo.Errore);
        Assert.Null(shell.Dialogo);
    });

    [Fact]
    public Task Dialogo_della_correzione_della_data() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        var scadenze = Assert.IsType<ScadenzeViewModel>(shell.Contenuto);

        scadenze.CorreggiDataCommand.Execute(scadenze.Righe[0]);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "18-correggi-data");

        var dialogo = Assert.IsType<DataScadenzaViewModel>(shell.Dialogo);
        Assert.Equal(new DateTime(2027, 1, 10), dialogo.Data);
        Assert.Contains(Testi.TitoloCorreggiData, testi);

        dialogo.Data = null;
        await dialogo.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.ScegliDataScadenza, dialogo.Errore);
    });

    [Fact]
    public Task Prossima_azione_nella_scheda_impianto() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "19-scheda-impianto-scadenze");

        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);
        // Solo le aperte, dalla più vicina; la chiusa resta fuori.
        Assert.Equal(["manutenzione", "controllo_efficienza"], scheda.Scadenze.Select(s => s.Dati.Tipo));
        Assert.Equal(LivelloScadenza.Scaduta, scheda.Scadenze[0].Livello);
        Assert.Contains("Scaduta da 3 giorni", testi);
        Assert.Contains($"{Testi.OgniAnno} · {Testi.DataCorrettaAMano}", testi);
        Assert.Contains($"Ogni 4 anni · {Testi.RegolaDaVerificare}", testi);
        Assert.Contains(Testi.InRegola, testi);
        Assert.Contains(Testi.OgniAnno, scheda.DatiTecnici.Single(d => d.Etichetta == Testi.Manutenzione).Valore, StringComparison.Ordinal);
    });

    [Fact]
    public Task Il_tecnico_legge_le_scadenze_senza_azioni() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);

        Assert.True(shell.VedeScadenze);
        Assert.False(shell.GestisceScadenze);
        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "20-scadenze-tecnico");

        Assert.Contains(Testi.ScadenzeTitolo, testi);
        Assert.DoesNotContain(Testi.RegistraRichiamo, testi);
        Assert.DoesNotContain(Testi.CorreggiData, testi);
    });

    [Fact]
    public Task Periodicita_nel_dialogo_dell_impianto() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);

        await scheda.ModificaCommand.ExecuteAsync(null);
        var editor = Assert.IsType<EditorImpiantoViewModel>(shell.Dialogo);
        Assert.Equal("12", editor.ManutenzioneMesi);

        // Un valore fuori dai limiti si spiega prima di chiamare il server.
        editor.ManutenzioneMesi = "200";
        await editor.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.ManutenzioneMesiNonValida, editor.Errore);

        // Salvando, la periodicità parte con l'impianto: il server la sovrascrive a ogni modifica.
        editor.ManutenzioneMesi = "24";
        await editor.SalvaCommand.ExecuteAsync(null);
        Assert.Contains("\"manutenzioneMesi\":24", DatiFittizi.UltimaModifica, StringComparison.Ordinal);
    });
}

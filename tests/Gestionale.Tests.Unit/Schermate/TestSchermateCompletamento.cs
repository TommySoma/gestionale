using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>Ciò che era rimasto fuori da M3: la pagina Oggi, i preventivi nella scheda cliente, l'appuntamento collegato all'intervento.</summary>
public sealed class TestSchermateCompletamento
{
    private static Task<(ShellViewModel Shell, Avalonia.Controls.Window Finestra)> ApriAmministrativoAsync() =>
        Schermate.ApriComeAsync(Guid.NewGuid(), "Paola", "Ferri", "amministrativo", Schermate.Amministrativo);

    [Fact]
    public Task Oggi_per_l_ufficio() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();

        await shell.ApriOggiCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "27-oggi");

        var oggi = Assert.IsType<OggiViewModel>(shell.Contenuto);
        Assert.True(shell.OggiAttivo);
        // Gli annullati non compaiono tra gli interventi del giorno.
        Assert.DoesNotContain(oggi.Interventi, i => i.Stato == FormatoInterventi.Stato("annullato"));
        Assert.Equal(1, oggi.DaPianificare);
        Assert.Equal(1, oggi.ScaduteTotali);
        Assert.Contains("Buongiorno, Paola", testi);
        Assert.Contains("mercoledì 13 gennaio 2027", testi);
        Assert.Contains(Testi.OggiUnoDaPianificare, testi);
        Assert.Contains("3 scadenze scadute o entro 7 giorni, di cui 1 già passate", testi);
        Assert.Contains("Scaduta da 3 giorni", testi);
    });

    [Fact]
    public Task Oggi_per_il_tecnico() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);

        await shell.ApriOggiCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "28-oggi-tecnico");

        var oggi = Assert.IsType<OggiViewModel>(shell.Contenuto);
        Assert.False(oggi.VedePreventivi);
        Assert.False(oggi.PianificaInterventi);
        Assert.DoesNotContain(Testi.OggiUnoDaPianificare, testi);
        Assert.Contains(Testi.InterventiDiOggi, testi);
    });

    [Fact]
    public Task Preventivi_nella_scheda_cliente() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();

        await shell.ApriClienteAsync(DatiFittizi.Cliente);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "29-scheda-cliente-preventivi");

        var scheda = Assert.IsType<SchedaClienteViewModel>(shell.Contenuto);
        Assert.Equal(3, scheda.Preventivi.Count);
        Assert.Contains("Preventivo 7/2027", testi);
        Assert.Contains(Testi.NuovoPreventivo, testi);

        // Il nuovo preventivo parte con il cliente già scelto e le sue sedi.
        await scheda.NuovoPreventivoCommand.ExecuteAsync(null);
        var editor = Assert.IsType<EditorPreventivoViewModel>(shell.Dialogo);
        Assert.Equal(DatiFittizi.Cliente, editor.Cliente?.Id);
        Assert.Contains(editor.Sedi, s => s.Id == DatiFittizi.SedeFirenze);
    });

    [Fact]
    public Task Richiamo_con_appuntamento_sull_intervento() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await ApriAmministrativoAsync();
        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        var scadenze = Assert.IsType<ScadenzeViewModel>(shell.Contenuto);

        scadenze.RegistraRichiamoCommand.Execute(scadenze.Righe[0]);
        var richiamo = Assert.IsType<RichiamoViewModel>(shell.Dialogo);
        richiamo.Esito = richiamo.Esiti.Single(e => e.Codice == "appuntamento");
        await richiamo.CaricaInterventiAsync();

        // Solo gli interventi aperti dell'impianto, più "nessuno".
        Assert.Equal(
            [null, DatiFittizi.InterventoPianificato, Guid.Parse("019ab000-0000-7000-8000-000000000073")],
            richiamo.Interventi.Select(i => i.Id));
        Assert.Null(richiamo.Intervento?.Id);

        richiamo.Intervento = richiamo.Interventi[1];
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "30-richiamo-appuntamento");
        Assert.Contains(Testi.InterventoAppuntamento, testi);

        await richiamo.SalvaCommand.ExecuteAsync(null);
        Assert.Null(richiamo.Errore);
        Assert.Null(shell.Dialogo);
    });
}

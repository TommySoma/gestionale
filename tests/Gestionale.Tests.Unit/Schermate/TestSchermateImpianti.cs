using Avalonia.Headless.XUnit;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>
/// Le schermate degli impianti disegnate davvero, in tema chiaro e scuro, con dati
/// fittizi. Le immagini finiscono in bin/.../schermate per guardarle; i controlli
/// verificano che i testi principali siano visibili.
/// </summary>
public sealed class TestSchermateImpianti
{
    [AvaloniaFact]
    public async Task Elenco_degli_impianti()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriImpiantiAsync();
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "01-elenco-impianti");

        Assert.Contains(Testi.ImpiantiTitolo, testi);
        Assert.Contains("Via dei Platani 12, Firenze (FI)", testi);
        Assert.Contains("TARGA-PROVA-77", testi);
        Assert.Contains(Testi.StatoDismesso, testi);
    }

    [AvaloniaFact]
    public async Task Scheda_impianto_con_generatori_catasto_e_avvisi()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "02-scheda-impianto");

        Assert.Contains("Riscaldamento (voce di prova)", testi);
        Assert.Contains(Testi.AvvisoIndirizzoDuplicato, testi);
        Assert.Contains(Testi.AvvisoMatricolaDuplicata, testi);
        Assert.Contains("24,5 kW", testi);
        Assert.Contains(Testi.CatastoToscana, testi);
    }

    [AvaloniaFact]
    public async Task Scheda_cliente_con_gli_impianti_per_sede()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriClienteAsync(DatiFittizi.Cliente);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "03-scheda-cliente");

        Assert.Contains("Climatizzazione (voce di prova)", testi);
        Assert.Contains(Testi.NessunImpiantoSede, testi);
        Assert.Contains(Testi.AggiungiImpianto, testi);
    }

    [AvaloniaFact]
    public async Task Dialogo_di_modifica_dell_impianto()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);

        await scheda.ModificaCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "04-modifica-impianto");

        var editor = Assert.IsType<EditorImpiantoViewModel>(shell.Dialogo);
        Assert.Equal("24,5", editor.Potenza);
        Assert.Contains(Testi.TitoloModificaImpianto, testi);
    }

    [AvaloniaFact]
    public async Task Dialogo_del_generatore()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);

        await scheda.ModificaGeneratoreCommand.ExecuteAsync(scheda.Generatori[0]);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "05-modifica-generatore");

        Assert.Contains(Testi.TitoloModificaGeneratore, testi);
    }

    [AvaloniaFact]
    public async Task Dialogo_del_riferimento_al_catasto()
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);

        scheda.ModificaRiferimentoCommand.Execute(scheda.Riferimenti[0]);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "06-riferimento-catasto");

        Assert.Contains(Testi.TitoloModificaRiferimento, testi);
    }
}

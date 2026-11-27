using System.Text;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>M4b: importazione di clienti e impianti da CSV, con l'anteprima prima di scrivere.</summary>
public sealed class TestSchermateImportazione
{
    [Fact]
    public Task Anteprima_con_un_errore_non_permette_di_importare() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(Guid.NewGuid(), "Paola", "Ferri", "amministrativo", Schermate.Amministrativo);
        Assert.True(shell.ImportaDati);

        shell.ApriImportazione();
        var importazione = Assert.IsType<ImportazioneViewModel>(shell.Contenuto);
        await importazione.AnalizzaAsync(Encoding.UTF8.GetBytes("indirizzo;cap;comune;provincia\r\n"), "clienti-da-importare.csv");
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "46-importazione");

        Assert.True(importazione.HaAnteprima);
        Assert.False(importazione.PuoImportare);
        Assert.Equal(("Riga 3", "Riga 4"), (Assert.Single(importazione.Errori).Riga, Assert.Single(importazione.Avvisi).Riga));
        Assert.Contains(Testi.CorreggiIlFile, testi);
        Assert.Contains("clienti-da-importare.csv", testi);
    });

    [Fact]
    public Task Chi_non_scrive_su_tutta_l_azienda_non_importa() => Schermate.Esegui(async () =>
    {
        var (tecnico, _) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);
        Assert.False(tecnico.ImportaDati);

        var (responsabile, _) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        Assert.False(responsabile.ImportaDati);
    });
}

using System.Globalization;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>M4b: promemoria email scelti dalla lista delle scadenze, con anteprima e invio confermato.</summary>
public sealed class TestSchermatePromemoria
{
    [Fact]
    public Task Promemoria_dalle_scadenze_scelte() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(Guid.NewGuid(), "Paola", "Ferri", "amministrativo", Schermate.Amministrativo);
        Assert.True(shell.InviaPromemoria);

        await shell.ApriScadenzeCommand.ExecuteAsync(null);
        var scadenze = Assert.IsType<ScadenzeViewModel>(shell.Contenuto);
        Assert.False(scadenze.InviaPromemoriaCommand.CanExecute(null));

        scadenze.Righe[0].Selezione.Scelta = true;
        scadenze.Righe[1].Selezione.Scelta = true;
        Assert.Equal(2, scadenze.Scelte);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Testi.InviaPromemoriaScelte, 2), scadenze.TestoInviaPromemoria);

        await scadenze.InviaPromemoriaCommand.ExecuteAsync(null);
        var dialogo = Assert.IsType<PromemoriaViewModel>(shell.Dialogo);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "47-promemoria");

        Assert.Equal(2, dialogo.Righe.Count);
        Assert.True(dialogo.Righe[1].Escluso);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Testi.RiepilogoPromemoria, 1, 1), dialogo.Riepilogo);
        Assert.StartsWith("La manutenzione dell'impianto", dialogo.Esempio, StringComparison.Ordinal);
        Assert.Contains(Testi.SpiegazioneNotaPromemoria, testi);

        dialogo.Nota = "Siamo in zona il martedì (prova).";
        await dialogo.SalvaCommand.ExecuteAsync(null);
        Assert.Null(dialogo.Errore);
        // Nel JSON le lettere accentate viaggiano come 00EC: basta il resto della nota.
        Assert.Contains("Siamo in zona il marted", DatiFittizi.UltimaScritturaRcee, StringComparison.Ordinal);
        Assert.StartsWith(string.Format(CultureInfo.CurrentCulture, Testi.PromemoriaInviati, 1), scadenze.Avviso, StringComparison.Ordinal);
    });

    [Fact]
    public Task Posta_in_uscita_nei_dati_aziendali() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(Guid.NewGuid(), "Mario", "Rossi", "titolare",
            [.. Schermate.Amministrativo, new("azienda.lettura", "tutti"), new("azienda.scrittura", "tutti")]);

        await shell.ApriDatiAziendaliCommand.ExecuteAsync(null);
        Schermate.Attendi();
        Schermate.Salva(finestra, "48-posta-in-uscita");

        var dati = Assert.IsType<DatiAziendaliViewModel>(shell.Contenuto);
        Assert.True(dati.PostaConfigurata);
        Assert.Equal(("posta", "1025", "nessuna"), (dati.HostPosta, dati.PortaPosta, dati.SicurezzaPosta?.Codice));
        Assert.Equal(Testi.PasswordNonSalvata, dati.SuggerimentoPassword);

        dati.PortaPosta = "porta";
        await dati.SalvaPostaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.PortaNonNumerica, dati.ErrorePosta);
    });

    [Fact]
    public Task Il_tecnico_non_invia_promemoria() => Schermate.Esegui(async () =>
    {
        var (tecnico, _) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);
        Assert.False(tecnico.InviaPromemoria);
    });
}

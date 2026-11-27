using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>M4: i dati che il tracciato SIERT chiede alle anagrafiche (codice ditta, codice ISTAT, sigla del generatore).</summary>
public sealed class TestSchermateSiert
{
    private static readonly Permesso[] s_titolare =
    [
        .. Schermate.Amministrativo,
        new("azienda.lettura", "tutti"),
        new("azienda.scrittura", "tutti"),
    ];

    [Fact]
    public Task Dati_aziendali_senza_codice_ditta() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(Guid.NewGuid(), "Mario", "Rossi", "titolare", s_titolare);
        Assert.True(shell.VedeAzienda);

        await shell.ApriDatiAziendaliCommand.ExecuteAsync(null);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "30-dati-aziendali");

        var dati = Assert.IsType<DatiAziendaliViewModel>(shell.Contenuto);
        Assert.True(shell.AziendaAttivo);
        Assert.True(dati.SenzaCodiceDitta);
        Assert.Equal("Toscana", dati.Regione);
        Assert.Contains(Testi.CodiceDittaMancante, testi);

        // Il codice ditta è un numero: le lettere non partono nemmeno verso il server.
        dati.CodiceDitta = "12A";
        await dati.SalvaCommand.ExecuteAsync(null);
        Assert.Equal(Testi.CodiceDittaNumerico, dati.Errore);

        dati.CodiceDitta = "4321";
        await dati.SalvaCommand.ExecuteAsync(null);
        Assert.Null(dati.Errore);
        Assert.Contains("\"codiceDittaSiert\":4321", DatiFittizi.UltimaModifica, StringComparison.Ordinal);
        Assert.Contains("\"versione\":2", DatiFittizi.UltimaModifica, StringComparison.Ordinal);
    });

    [Fact]
    public Task Dati_aziendali_in_sola_lettura_per_chi_non_e_titolare() => Schermate.Esegui(async () =>
    {
        var (shell, _) = await Schermate.ApriAsync([.. Schermate.ResponsabileTecnico, new("azienda.lettura", "tutti")]);

        await shell.ApriDatiAziendaliCommand.ExecuteAsync(null);
        var dati = Assert.IsType<DatiAziendaliViewModel>(shell.Contenuto);
        Assert.True(dati.SolaLettura);

        var (tecnico, _) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);
        Assert.False(tecnico.VedeAzienda);
    });

    [Fact]
    public Task Modifica_della_sede_con_il_codice_istat() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(Guid.NewGuid(), "Paola", "Ferri", "amministrativo", Schermate.Amministrativo);

        await shell.ApriClienteAsync(DatiFittizi.Cliente);
        var scheda = Assert.IsType<SchedaClienteViewModel>(shell.Contenuto);
        scheda.ModificaSedeCommand.Execute(scheda.Sedi.Single(s => s.Id == DatiFittizi.SedeFirenze));
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "31-modifica-sede");

        var editor = Assert.IsType<EditorSedeViewModel>(shell.Dialogo);
        Assert.Equal(("048017", "Via dei Platani"), (editor.CodiceIstat, editor.Indirizzo));
        Assert.Contains(Testi.TitoloModificaSede, testi);
        Assert.Contains(Testi.SpiegazioneCodiceIstat, testi);

        await editor.SalvaCommand.ExecuteAsync(null);
        Assert.Null(editor.Errore);
        Assert.Contains("\"codiceIstatComune\":\"048017\"", DatiFittizi.UltimaModifica, StringComparison.Ordinal);
    });

    [Fact]
    public Task Chiusura_con_la_bozza_del_rapporto() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync([.. Schermate.ResponsabileTecnico, new("rapporti_controllo.scrittura", "tutti")]);
        var intervento = (await shell.Api.InterventoAsync(DatiFittizi.InterventoChiuso)).Valore! with { Tipo = "manutenzione", GeneratoreId = null };
        var generatori = (await shell.Api.ImpiantoAsync(DatiFittizi.Caldaia)).Valore!.Generatori.Where(g => g.DismessoIl is null).ToList();

        var chiusura = new ChiusuraInterventoViewModel(shell, intervento, _ => { }, generatori);
        Assert.True(chiusura.PuoCreareBozza);
        Assert.False(chiusura.MostraBozza);
        chiusura.ControlloEfficienza = true;
        chiusura.Modello = chiusura.Modelli.Single(m => m.Codice == "tipo_1");
        shell.MostraDialogo(chiusura);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "32-chiusura-con-bozza");

        Assert.True(chiusura.MostraBozza);
        Assert.Contains(Testi.BozzaRapportoModello, testi);

        // Con più generatori e nessuno sull'intervento, la scelta spetta a chi chiude.
        if (generatori.Count > 1)
        {
            Assert.Null(chiusura.Generatore);
            await chiusura.SalvaCommand.ExecuteAsync(null);
            Assert.Equal(Testi.ScegliGeneratoreRapporto, chiusura.Errore);
        }

        chiusura.Generatore = chiusura.Generatori[0];
        await chiusura.SalvaCommand.ExecuteAsync(null);
        Assert.Null(chiusura.Errore);
        Assert.Contains("\"modelloRapporto\":\"tipo_1\"", DatiFittizi.UltimaChiusura, StringComparison.Ordinal);
    });
}

using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>
/// Calendario, scheda e dialoghi degli interventi disegnati davvero, in chiaro e in
/// scuro, con dati fittizi della settimana dell'11 gennaio 2027 e "oggi" fermo al 13.
/// </summary>
public sealed class TestSchermateInterventi
{
    [Fact]
    public Task Calendario_della_settimana_per_tecnico() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriCalendarioAsync(DatiFittizi.Settimana);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "07-calendario-settimana");

        var calendario = Assert.IsType<CalendarioViewModel>(shell.Contenuto);
        Assert.Equal(["Marco Bianchi", "Sara Neri"], calendario.Righe.Select(r => r.Tecnico));
        Assert.Equal(7, calendario.Giorni.Count);
        Assert.True(calendario.Giorni[2].Oggi);

        // Ore italiane (UTC+1 in inverno); l'annullato non occupa il calendario.
        Assert.Contains("09:00–11:00", testi);
        Assert.Contains("14:30–16:00", testi);
        Assert.Contains(FormatoInterventi.Stato("in_corso"), testi);
        Assert.DoesNotContain(calendario.Righe.SelectMany(r => r.Celle).SelectMany(c => c.Interventi), v => v.Dettaglio.Contains("Pisa", StringComparison.Ordinal));
        Assert.Contains("11–17 gennaio 2027", testi);
        Assert.Contains(Testi.DaPianificare, testi);
        Assert.Contains("n. 14/2027 · Sopralluogo · Livorno", calendario.DaPianificare.Select(d => d.Dettaglio));
        Assert.Contains(Testi.NuovoIntervento, testi);
    });

    [Fact]
    public Task Calendario_del_giorno() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriCalendarioAsync(DatiFittizi.Settimana.AddDays(2));
        var calendario = Assert.IsType<CalendarioViewModel>(shell.Contenuto);

        calendario.VistaGiorno = true;
        await calendario.CaricaAsync();
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "08-calendario-giorno");

        Assert.Single(calendario.Giorni);
        Assert.Contains("mercoledì 13 gennaio 2027", testi);
        Assert.Contains("14:30–16:00", testi);
        Assert.DoesNotContain("09:00–11:00", testi);
    });

    [Fact]
    public Task Il_tecnico_vede_solo_la_propria_riga_e_non_pianifica() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriComeAsync(DatiFittizi.TecnicoMarco, "Marco", "Bianchi", "tecnico", Schermate.Tecnico);

        await shell.ApriCalendarioAsync(DatiFittizi.Settimana);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "09-calendario-tecnico");

        var calendario = Assert.IsType<CalendarioViewModel>(shell.Contenuto);
        Assert.Equal("Marco Bianchi", Assert.Single(calendario.Righe).Tecnico);
        Assert.DoesNotContain(Testi.NuovoIntervento, testi);
        Assert.DoesNotContain(Testi.NienteDaPianificare, testi);
        Assert.Contains("09:00–11:00", testi);
    });

    [Fact]
    public Task Scheda_di_un_intervento_chiuso_con_righe() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriInterventoAsync(DatiFittizi.InterventoChiuso);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "10-scheda-intervento");

        var scheda = Assert.IsType<SchedaInterventoViewModel>(shell.Contenuto);
        Assert.Equal("Manutenzione n. 12/2027", scheda.Titolo);
        Assert.Contains("lunedì 11 gennaio 2027, dalle 09:00 alle 11:00", testi);
        Assert.Contains("GUA-01 · Guarnizione di prova", testi);
        Assert.Contains(FormatoInterventi.Importo(68.98m), scheda.Totale);
        Assert.Contains(FormatoInterventi.Esito("risolto"), testi);

        // Completato è finale: niente azioni.
        Assert.False(scheda.PuoAvviare || scheda.PuoChiudere || scheda.PuoPianificare || scheda.PuoAnnullare);
    });

    [Fact]
    public Task Scheda_impianto_con_lo_storico_degli_interventi() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);

        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "11-scheda-impianto-interventi");

        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);
        Assert.Equal(4, scheda.Interventi.Count);
        Assert.Contains("Manutenzione n. 12/2027", testi);
        Assert.Contains(Testi.NuovoInterventoImpianto, testi);
    });

    [Fact]
    public Task Dialogo_del_nuovo_intervento_dalla_scheda_impianto() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriImpiantoAsync(DatiFittizi.Caldaia);
        var scheda = Assert.IsType<SchedaImpiantoViewModel>(shell.Contenuto);

        await scheda.NuovoInterventoCommand.ExecuteAsync(null);
        var editor = Assert.IsType<EditorInterventoViewModel>(shell.Dialogo);
        editor.Data = new DateTime(2027, 1, 18);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "12-nuovo-intervento");

        Assert.True(editor.ImpiantoFisso);
        Assert.Equal(["Da assegnare", "Marco Bianchi", "Sara Neri"], editor.Tecnici.Select(t => t.Nome));
        Assert.Contains(Testi.TitoloNuovoIntervento, testi);
        Assert.Contains(Testi.OraInizio, testi);
    });

    [Fact]
    public Task Dialogo_di_chiusura_con_le_righe() => Schermate.Esegui(async () =>
    {
        var (shell, finestra) = await Schermate.ApriAsync(Schermate.ResponsabileTecnico);
        await shell.ApriInterventoAsync(DatiFittizi.InterventoChiuso);
        var scheda = Assert.IsType<SchedaInterventoViewModel>(shell.Contenuto);

        // La scheda è di un intervento chiuso; il dialogo si prova comunque con i suoi dati.
        var intervento = (await shell.Api.InterventoAsync(DatiFittizi.InterventoChiuso)).Valore!;
        var chiusura = new ChiusuraInterventoViewModel(shell, intervento, () => { });
        chiusura.AggiungiManodoperaCommand.Execute(null);
        chiusura.Righe[0].Quantita = "1,5";
        chiusura.Righe[0].Prezzo = "40";
        chiusura.AggiungiMaterialeCommand.Execute(null);
        chiusura.Righe[1].Descrizione = "Guarnizione di prova";
        chiusura.Righe[1].Quantita = "2";
        chiusura.Righe[1].Prezzo = "4,99";
        chiusura.Righe[1].Sconto = "10";
        shell.MostraDialogo(chiusura);
        Schermate.Attendi();
        var testi = Schermate.Salva(finestra, "13-chiusura-intervento");

        Assert.NotNull(scheda);
        Assert.Equal(FormatoInterventi.Importo(68.98m), chiusura.Totale);
        Assert.Contains(Testi.AggiungiMateriale, testi);
    });
}

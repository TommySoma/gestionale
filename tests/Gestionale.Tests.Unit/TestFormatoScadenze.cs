using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit;

public sealed class TestFormatoScadenze
{
    [Theory]
    [InlineData(-10, LivelloScadenza.Scaduta, "Scaduta da 10 giorni")]
    [InlineData(-1, LivelloScadenza.Scaduta, "Scaduta da ieri")]
    [InlineData(0, LivelloScadenza.InScadenza, "Scade oggi")]
    [InlineData(1, LivelloScadenza.InScadenza, "Scade domani")]
    [InlineData(30, LivelloScadenza.InScadenza, "Tra 30 giorni")]
    [InlineData(31, LivelloScadenza.InRegola, "In regola")]
    public void Livello_e_testo_dipendono_dai_giorni_mancanti(int giorni, LivelloScadenza livello, string testo)
    {
        Assert.Equal(livello, FormatoScadenze.Livello(giorni));
        Assert.Equal(testo, FormatoScadenze.Urgenza(giorni));
    }

    [Theory]
    [InlineData(1, "Ogni mese")]
    [InlineData(6, "Ogni 6 mesi")]
    [InlineData(12, "Ogni anno")]
    [InlineData(24, "Ogni 2 anni")]
    [InlineData(18, "Ogni 18 mesi")]
    public void La_periodicità_si_legge_in_mesi_o_anni(int mesi, string testo) =>
        Assert.Equal(testo, FormatoScadenze.Periodicita(mesi));

    [Fact]
    public void Ogni_codice_del_server_ha_il_suo_testo()
    {
        Assert.Equal(Testi.TipoScadenzaControllo, FormatoScadenze.Tipo("controllo_efficienza"));
        Assert.Equal(Testi.RichiamoInAttesa, FormatoScadenze.StatoRichiamo("in_attesa"));
        Assert.Equal(Testi.CanaleDiPersona, FormatoScadenze.Canale("di_persona"));
        Assert.Equal(Testi.EsitoNessunaRisposta, FormatoScadenze.EsitoRichiamo("nessuna_risposta"));
    }

    [Fact]
    public void L_ultimo_richiamo_compare_solo_se_c_è()
    {
        Assert.Null(FormatoScadenze.UltimoRichiamo(null, null, null));
        Assert.Equal(
            "Ultimo: da richiamare, 12 gennaio 2027, 10:30 · risentire il 14 gennaio 2027",
            FormatoScadenze.UltimoRichiamo("da_richiamare", new DateTime(2027, 1, 12, 9, 30, 0, DateTimeKind.Utc), new DateOnly(2027, 1, 14)));
    }
}

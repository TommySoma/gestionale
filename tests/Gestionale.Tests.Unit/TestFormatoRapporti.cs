using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit;

public sealed class TestFormatoRapporti
{
    [Theory]
    [InlineData("PRESENZA_RIFLUSSI", "Presenza riflussi")]
    [InlineData("CO2", "CO2")]
    [InlineData("PRODUZIONE ACS (ACQUA CALDA SANITARIA)", "Produzione ACS (acqua calda sanitaria)")]
    [InlineData("PARTITA_ IVA", "Partita IVA")]
    [InlineData("SOSTITUZIONE_ REGOLAZIONE_ ON/OFF", "Sostituzione regolazione ON/OFF")]
    public void Le_etichette_nascono_dai_nomi_del_tracciato(string nome, string etichetta) =>
        Assert.Equal(etichetta, FormatoRapporti.Etichetta(nome));

    [Fact]
    public void Si_nominano_solo_le_codifiche_note_della_guida()
    {
        Assert.Equal(Testi.CodiceSi, FormatoRapporti.NomeValore(["3", "4", "9"], "3"));
        Assert.Equal(Testi.CodiceNonRichiesto, FormatoRapporti.NomeValore(["3", "4", "9", "16"], "16"));
        Assert.Equal(Testi.CodiceNo, FormatoRapporti.NomeValore(["21", "13"], "13"));

        // Il trattamento dell'acqua usa 16-23 con un significato che la guida non dà qui: resta il codice.
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.CodiceNumerico, "16"),
            FormatoRapporti.NomeValore(["16", "17", "18"], "16"));
        Assert.Equal("Naturale", FormatoRapporti.NomeValore(["Naturale", "Forzata"], "Naturale"));
    }

    [Theory]
    [InlineData("68,2", false, "68.2")]
    [InlineData("12", true, "12")]
    [InlineData("12,5", true, null)]
    [InlineData("caldo", false, null)]
    public void I_numeri_passano_nella_codifica_del_tracciato(string testo, bool intero, string? atteso) =>
        Assert.Equal(atteso, FormatoRapporti.NumeroPerTracciato(testo, intero));

    [Fact]
    public void Il_termine_conta_solo_per_i_rapporti_non_trasmessi()
    {
        var oggi = new DateOnly(2027, 1, 13);
        Assert.True(FormatoRapporti.TermineVicino(new DateOnly(2027, 1, 18), "bozza", oggi));
        Assert.False(FormatoRapporti.TermineVicino(new DateOnly(2027, 1, 18), "trasmesso", oggi));
        Assert.False(FormatoRapporti.TermineVicino(new DateOnly(2027, 2, 18), "firmato", oggi));
    }
}

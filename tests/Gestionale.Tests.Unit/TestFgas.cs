using Gestionale.Domain.Fgas;

namespace Gestionale.Tests.Unit;

public sealed class TestFgas
{
    private static readonly DateOnly s_oggi = new(2027, 1, 13);

    [Fact]
    public void Un_apparecchiatura_ha_gas_e_carica()
    {
        Assert.Empty(RegoleFgas.Valida(new DatiApparecchiatura(Guid.NewGuid(), 2.4m, null, false, false, false, " COD-1 ", null, null)).Errori);
        Assert.Equal(
            [ErroreFgas.GasNonValido, ErroreFgas.CaricaNonValida, ErroreFgas.CaricaInstallazioneNonValida],
            RegoleFgas.Valida(new DatiApparecchiatura(null, 0, -1, false, false, false, null, null, null)).Errori);
    }

    [Fact]
    public void Un_controllo_delle_perdite_vuole_l_esito_e_lo_tiene_solo_lui()
    {
        var controllo = new DatiOperazione(Guid.NewGuid(), Guid.NewGuid(), " Controllo_Perdite ", s_oggi, null, null, null, false, null, null, null, null, null);
        Assert.Equal([ErroreFgas.EsitoControlloMancante], RegoleFgas.Valida(controllo, s_oggi).Errori);

        var (riparazione, errori) = RegoleFgas.Valida(controllo with { Tipo = "riparazione", PerditaRilevata = true, GasAggiuntoKg = 0.5m }, s_oggi);
        Assert.Empty(errori);
        Assert.Null(riparazione.PerditaRilevata);
        Assert.Equal(0m, riparazione.GasRecuperatoKg);
    }

    [Fact]
    public void Riciclo_smantellamento_date_e_quantita_hanno_le_loro_regole()
    {
        var base_ = new DatiOperazione(Guid.NewGuid(), Guid.NewGuid(), "smantellamento", s_oggi.AddDays(1), null, -1, null, true, null, null, " ", null, null);
        Assert.Equal(
            [ErroreFgas.DataOperazioneFutura, ErroreFgas.QuantitaGasNonValida, ErroreFgas.ImpiantoRiciclaggioMancante, ErroreFgas.MisureSmaltimentoMancanti],
            RegoleFgas.Valida(base_, s_oggi).Errori);
        Assert.Contains(ErroreFgas.TipoOperazioneNonValido, RegoleFgas.Valida(base_ with { Tipo = "ricarica" }, s_oggi).Errori);
    }

    [Fact]
    public void Un_certificato_ha_numero_e_date_coerenti()
    {
        Assert.Equal(
            [ErroreFgas.NumeroCertificatoMancante, ErroreFgas.DateCertificatoNonValide],
            RegoleFgas.Valida(new DatiCertificato(null, " ", null, s_oggi, s_oggi.AddDays(-1))).Errori);
    }
}

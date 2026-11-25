using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit;

/// <summary>Le ore degli interventi si mostrano e si inviano nel fuso italiano, con ora legale e solare.</summary>
public sealed class TestFormatoInterventi
{
    [Fact]
    public void L_ora_locale_tiene_conto_dell_ora_legale()
    {
        Assert.Equal(TimeSpan.FromHours(1), FormatoInterventi.DaLocale(new DateOnly(2027, 1, 11), new TimeSpan(9, 0, 0)).Offset);
        Assert.Equal(TimeSpan.FromHours(2), FormatoInterventi.DaLocale(new DateOnly(2027, 7, 12), new TimeSpan(9, 0, 0)).Offset);
        Assert.Equal("09:00", FormatoInterventi.Ora(new DateTime(2027, 7, 12, 7, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("09:00–11:00", FormatoInterventi.Orario(new DateTime(2027, 1, 11, 8, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 11, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void La_settimana_comincia_di_lunedì()
    {
        Assert.Equal(new DateOnly(2027, 1, 11), FormatoInterventi.Lunedi(new DateOnly(2027, 1, 11)));
        Assert.Equal(new DateOnly(2027, 1, 11), FormatoInterventi.Lunedi(new DateOnly(2027, 1, 17)));
        Assert.Equal(new DateOnly(2027, 1, 18), FormatoInterventi.Lunedi(new DateOnly(2027, 1, 18)));
    }

    [Fact]
    public void Il_periodo_si_scrive_per_esteso()
    {
        Assert.Equal("11–17 gennaio 2027", FormatoInterventi.Periodo(new DateOnly(2027, 1, 11), 7));
        Assert.Equal("29 marzo – 4 aprile 2027", FormatoInterventi.Periodo(new DateOnly(2027, 3, 29), 7));
        Assert.Equal("28 dicembre 2026 – 3 gennaio 2027", FormatoInterventi.Periodo(new DateOnly(2026, 12, 28), 7));
        Assert.Equal("mercoledì 13 gennaio 2027", FormatoInterventi.Periodo(new DateOnly(2027, 1, 13), 1));
    }

    [Fact]
    public void Tipi_stati_ed_esiti_hanno_un_nome_e_i_codici_sconosciuti_restano_come_sono()
    {
        Assert.Equal("Manutenzione", FormatoInterventi.Tipo("manutenzione"));
        Assert.Equal("In corso", FormatoInterventi.Stato("in_corso"));
        Assert.Equal("Da completare", FormatoInterventi.Esito("da_completare"));
        Assert.Equal("nuovo_stato", FormatoInterventi.Stato("nuovo_stato"));
        Assert.Equal("n. 12/2027", FormatoInterventi.Numero(12, 2027));
    }

    [Fact]
    public void L_importo_di_una_riga_segue_quantità_prezzo_e_sconto()
    {
        var riga = new RigaModificabile("materiale") { Quantita = "2", Prezzo = "4,99", Sconto = "10" };
        Assert.Equal(8.98m, riga.Importo());

        riga.Prezzo = "quattro";
        Assert.Null(riga.Importo());
    }
}

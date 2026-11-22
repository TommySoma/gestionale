using Gestionale.Domain.Anagrafica;

namespace Gestionale.Tests.Unit;

public sealed class TestPartitaIva
{
    [Theory]
    [InlineData("01234567897")]
    [InlineData("00000000000")]
    public void Una_partita_iva_con_la_cifra_di_controllo_giusta_è_valida(string valore)
    {
        Assert.True(PartitaIva.Valida(valore));
    }

    [Theory]
    [InlineData("01234567890")]
    [InlineData("0123456789")]
    [InlineData("012345678977")]
    [InlineData("0123456789A")]
    [InlineData("")]
    [InlineData(null)]
    public void Una_partita_iva_sbagliata_non_è_valida(string? valore)
    {
        Assert.False(PartitaIva.Valida(valore));
    }
}

public sealed class TestCodiceFiscale
{
    [Theory]
    [InlineData("RSSMRA80A01H501U")] // uomo, nato il 1° gennaio 1980 a Roma
    [InlineData("RSSMRA80A41H501Y")] // donna: giorno + 40
    [InlineData("RSSMRA80A01H50MM")] // omocodia: l'ultima cifra del comune diventa una lettera
    [InlineData("01234567897")]      // soggetto diverso da persona fisica, 11 cifre
    public void Un_codice_corretto_è_valido(string valore)
    {
        Assert.True(CodiceFiscale.Valido(valore));
    }

    [Theory]
    [InlineData("RSSMRA80A01H501A")] // carattere di controllo sbagliato
    [InlineData("RSSMRA80Z01H501U")] // mese inesistente
    [InlineData("RSSMRA80A35H501U")] // giorno fuori intervallo
    [InlineData("RSSMRA80A01H501")]  // troppo corto
    [InlineData("rssmra80a01h501u")] // non normalizzato
    [InlineData("01234567890")]
    [InlineData(null)]
    public void Un_codice_sbagliato_non_è_valido(string? valore)
    {
        Assert.False(CodiceFiscale.Valido(valore));
    }

    [Fact]
    public void La_normalizzazione_toglie_spazi_e_mette_in_maiuscolo()
    {
        Assert.Equal("RSSMRA80A01H501U", CodiceFiscale.Normalizza(" rssmra 80a01 h501u "));
    }
}

public sealed class TestRegoleAnagrafica
{
    [Fact]
    public void Un_privato_valido_viene_normalizzato()
    {
        var (dati, errori) = RegoleAnagrafica.Valida(new DatiCliente(
            " Privato ", "da ignorare", " Mario ", "Rossi", "rssmra80a01h501u", null, " Mario@PEC.example ", "  "));

        Assert.Empty(errori);
        Assert.Equal(new DatiCliente("privato", null, "Mario", "Rossi", "RSSMRA80A01H501U", null, "mario@pec.example", null), dati);
    }

    [Fact]
    public void Un_privato_senza_cognome_è_rifiutato()
    {
        Assert.Contains(ErroreAnagrafica.NomeCognomeMancanti,
            RegoleAnagrafica.Valida(new DatiCliente("privato", null, "Mario", " ", null, null, null, null)).Errori);
    }

    [Fact]
    public void Un_condominio_ha_bisogno_della_denominazione_e_non_tiene_nome_e_cognome()
    {
        var (dati, errori) = RegoleAnagrafica.Valida(new DatiCliente("condominio", "Condominio Le Querce", "Mario", "Rossi", "01234567897", null, null, null));

        Assert.Empty(errori);
        Assert.Null(dati.Nome);
        Assert.Contains(ErroreAnagrafica.DenominazioneMancante,
            RegoleAnagrafica.Valida(new DatiCliente("condominio", null, null, null, null, null, null, null)).Errori);
    }

    [Fact]
    public void Codice_fiscale_e_partita_iva_sbagliati_sono_segnalati_insieme()
    {
        var errori = RegoleAnagrafica.Valida(new DatiCliente("azienda", "Prova Srl", null, null, "RSSMRA80A01H501A", "01234567890", "senza-chiocciola", null)).Errori;

        Assert.Equal([ErroreAnagrafica.CodiceFiscaleNonValido, ErroreAnagrafica.PartitaIvaNonValida, ErroreAnagrafica.PecNonValida], errori);
    }

    [Fact]
    public void Una_sede_viene_normalizzata_e_validata()
    {
        var (dati, errori) = RegoleAnagrafica.Valida(new DatiSede(null, " Via dei Platani ", "12", "50100", "Firenze", "fi", null, 43.7696m, 11.2558m));

        Assert.Empty(errori);
        Assert.Equal("FI", dati.Provincia);
        Assert.Equal("Via dei Platani", dati.Indirizzo);
    }

    [Fact]
    public void Una_sede_con_cap_provincia_e_coordinate_sbagliati_è_rifiutata()
    {
        var errori = RegoleAnagrafica.Valida(new DatiSede(null, "", null, "501", "", "Firenze", null, 43.7m, null)).Errori;

        Assert.Equal(
            [ErroreAnagrafica.IndirizzoMancante, ErroreAnagrafica.ComuneMancante, ErroreAnagrafica.CapNonValido,
             ErroreAnagrafica.ProvinciaNonValida, ErroreAnagrafica.CoordinateNonValide],
            errori);
    }

    [Theory]
    [InlineData("cellulare", "+39 333 000 0000", true)]
    [InlineData("telefono", "055/000000", true)]
    [InlineData("email", "Giulia@Esempio.example", true)]
    [InlineData("telefono", "chiamare dopo le 18", false)]
    [InlineData("email", "giulia.esempio.example", false)]
    [InlineData("fax", "055000000", false)]
    public void I_contatti_seguono_il_formato_del_loro_tipo(string tipo, string valore, bool valido)
    {
        Assert.Equal(valido, RegoleAnagrafica.Valida(new DatiContatto(tipo, valore, null, false)).Errori.Count == 0);
    }
}

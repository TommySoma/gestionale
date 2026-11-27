using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gestionale.Domain.Rapporti;

namespace Gestionale.Tests.Unit;

/// <summary>Regole del rapporto di controllo lato API: forma dei dati, codici dei bollini, file di import SIERT.</summary>
public sealed class TestRapporti
{
    private static readonly CampoTracciato[] s_campi =
    [
        new(1, 1, "CODICE_CATASTO", "testo", 10, true, null, "^[0-9]{10}$", OriginiCampo.Dati, "codice_catasto"),
        new(2, 2, "POTENZA_TOTALE", "decimale", null, true, null, null, OriginiCampo.Dati, "potenza_totale"),
        new(3, 11, "ID_DITTA_MANUTENTORE", "intero", null, true, null, null, OriginiCampo.Azienda, "codice_ditta_siert"),
        new(4, 64, "DATA_CONTROLLO", "data", null, true, null, null, OriginiCampo.Rapporto, "data_controllo"),
        new(5, 59, "OSSERVAZIONI", "testo", 400, false, null, null, OriginiCampo.Dati, "osservazioni"),
        new(6, 68, "NUMERO_BOLLINO", "testo", 50, true, null, null, OriginiCampo.Bollino, "codice"),
    ];

    [Fact]
    public void I_dati_tengono_testo_e_numeri_senza_spazi_ai_bordi()
    {
        var (dati, errori, chiavi) = RegoleRapporti.NormalizzaDati(
            Json("""{ "codice_catasto": " 0123456789 ", "potenza_totale": 24.50, "osservazioni": "   " }"""), s_campi);

        Assert.Empty(errori);
        Assert.Empty(chiavi);
        Assert.Equal("""{"codice_catasto":"0123456789","potenza_totale":24.50}""", dati.ToJsonString());
        Assert.Empty(RegoleRapporti.NormalizzaDati(Json("""{ "osservazioni": null }"""), s_campi).Dati);
    }

    [Fact]
    public void Le_chiavi_che_non_vengono_dai_dati_sono_rifiutate()
    {
        // Il codice ditta viene dall'azienda, non dal rapporto.
        var (_, errori, chiavi) = RegoleRapporti.NormalizzaDati(Json("""{ "codice_ditta_siert": 1 }"""), s_campi);

        Assert.Equal([ErroreRapporto.ChiaveDatiSconosciuta], errori);
        Assert.Equal(["codice_ditta_siert"], chiavi);
    }

    [Fact]
    public void Le_chiavi_fuori_dal_tracciato_e_i_valori_strani_sono_errori()
    {
        var (_, errori, chiavi) = RegoleRapporti.NormalizzaDati(
            Json("""{ "colore": "rosso", "codice": "2026000000001", "potenza_totale": true, "osservazioni": ["a"] }"""), s_campi);

        Assert.Equal([ErroreRapporto.ChiaveDatiSconosciuta, ErroreRapporto.ValoreDatiNonValido], errori);
        // Il bollino non sta nei dati: lo assegna il registro.
        Assert.Equal(["colore", "codice", "potenza_totale", "osservazioni"], chiavi);
    }

    [Fact]
    public void I_dati_devono_essere_un_oggetto()
    {
        Assert.Equal([ErroreRapporto.DatiRapportoNonOggetto], RegoleRapporti.NormalizzaDati(Json("[1, 2]"), s_campi).Errori);
        Assert.Empty(RegoleRapporti.NormalizzaDati(null, s_campi).Dati);
    }

    [Fact]
    public void Un_valore_troppo_lungo_è_rifiutato()
    {
        var lungo = new string('x', RegoleRapporti.LunghezzaMassimaValore + 1);
        var (_, errori, chiavi) = RegoleRapporti.NormalizzaDati(Json($$"""{ "osservazioni": "{{lungo}}" }"""), s_campi);

        Assert.Equal([ErroreRapporto.ValoreDatiNonValido], errori);
        Assert.Equal(["osservazioni"], chiavi);
    }

    [Theory]
    [InlineData("2026000000001", true)]
    [InlineData("1999123456789", true)]
    [InlineData("202600000001", false)]
    [InlineData("20260000000012", false)]
    [InlineData("3026000000001", false)]
    [InlineData("2026A00000001", false)]
    public void Il_codice_del_bollino_ha_13_cifre_e_inizia_con_l_anno(string codice, bool valido) =>
        Assert.Equal(valido, RegoleRapporti.CodiceBollinoValido(codice));

    [Fact]
    public void I_codici_di_un_acquisto_si_puliscono_e_non_si_ripetono()
    {
        var (codici, errori, _) = RegoleRapporti.NormalizzaCodiciBollini([" 2026000000001", "2026000000001", "", "2026000000002"]);
        Assert.Empty(errori);
        Assert.Equal(["2026000000001", "2026000000002"], codici);

        var (_, sbagliati, nonValidi) = RegoleRapporti.NormalizzaCodiciBollini(["2026000000001", "12345"]);
        Assert.Equal([ErroreRapporto.CodiceBollinoNonValido], sbagliati);
        Assert.Equal(["12345"], nonValidi);

        Assert.Equal([ErroreRapporto.CodiciBolliniMancanti], RegoleRapporti.NormalizzaCodiciBollini([" "]).Errori);
    }

    [Fact]
    public void La_data_del_controllo_non_è_futura()
    {
        var oggi = new DateOnly(2026, 11, 26);

        Assert.Equal([ErroreRapporto.DataControlloMancante], RegoleRapporti.ValidaDataControllo(null, oggi));
        Assert.Equal([ErroreRapporto.DataControlloFutura], RegoleRapporti.ValidaDataControllo(oggi.AddDays(1), oggi));
        Assert.Empty(RegoleRapporti.ValidaDataControllo(oggi, oggi));
    }

    [Fact]
    public void La_riga_segue_le_posizioni_e_lascia_vuoti_i_valori_mancanti()
    {
        var dati = JsonNode.Parse("""{ "codice_catasto": "0123456789", "potenza_totale": 24.50 }""")!.AsObject();
        var mescolati = s_campi.Reverse();

        var riga = RegoleRapporti.RigaInvio(mescolati, c => c.Origine switch
        {
            OriginiCampo.Dati => RegoleRapporti.ValoreDati(dati, c.Chiave),
            OriginiCampo.Azienda => "4321",
            OriginiCampo.Rapporto => "2026-11-25",
            OriginiCampo.Bollino => "2026000000001",
            _ => null,
        });

        Assert.Equal("0123456789\t24.50\t4321\t2026-11-25\t\t2026000000001", riga);
    }

    [Fact]
    public void Il_file_è_utf8_senza_bom_con_righe_crlf()
    {
        var contenuto = RegoleRapporti.ContenutoInvio(["prima\triga", "città"]);

        Assert.NotEqual(0xEF, contenuto[0]);
        Assert.Equal("prima\triga\r\ncittà\r\n", Encoding.UTF8.GetString(contenuto));
    }

    [Fact]
    public void Il_nome_del_file_indica_modello_e_momento()
    {
        var nome = RegoleRapporti.NomeFileInvio(ModelliRapporto.Tipo1, new DateTimeOffset(2026, 11, 26, 9, 5, 7, TimeSpan.Zero));

        Assert.Equal("rcee_tipo_1_20261126_090507.txt", nome);
    }

    [Fact]
    public void Il_motivo_di_annullamento_è_obbligatorio_e_limitato()
    {
        Assert.Null(RegoleRapporti.MotivoAnnullamento("  "));
        Assert.Null(RegoleRapporti.MotivoAnnullamento(new string('x', RegoleRapporti.LunghezzaMassimaMotivo + 1)));
        Assert.Equal("Errore di compilazione", RegoleRapporti.MotivoAnnullamento(" Errore di compilazione "));
    }

    private static JsonElement Json(string testo) => JsonDocument.Parse(testo).RootElement.Clone();
}

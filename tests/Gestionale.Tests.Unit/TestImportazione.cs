using System.Text;
using Gestionale.Domain.Anagrafica;
using Gestionale.Domain.Importazione;

namespace Gestionale.Tests.Unit;

public sealed class TestImportazione
{
    private const string Intestazione = "Tipo cliente;Ragione sociale;Nome;Cognome;Partita IVA;Email;Telefono;Indirizzo;Civico;CAP;Comune;Provincia;Tipo impianto;Potenza kW;PDR;Tipo generatore;Installato il";

    private static AnalisiImportazione Analizza(params string[] righe) =>
        RegoleImportazione.Analizza(Encoding.UTF8.GetBytes(string.Join("\r\n", righe)));

    [Fact]
    public void Una_riga_completa_diventa_cliente_sede_impianto_e_generatore()
    {
        var analisi = Analizza(
            Intestazione,
            "privato;;Giulia;Esempio;;giulia@example.com;333 000 0001;\"Via delle Prove; interno 2\";1;50100;Firenze;fi;prova_riscaldamento;24,5;0123 4567 8901 23;prova_caldaia;15/03/2018");

        Assert.Empty(analisi.Errori);
        var riga = Assert.Single(analisi.Righe);
        Assert.Equal(2, riga.Riga);
        Assert.Equal(("Giulia", "Esempio", TipiCliente.Privato), (riga.Cliente.Nome, riga.Cliente.Cognome, riga.Cliente.Tipo));
        Assert.Equal(("Via delle Prove; interno 2", "FI"), (riga.Sede.Indirizzo, riga.Sede.Provincia));
        Assert.Equal((24.5m, "01234567890123"), (riga.Impianto!.PotenzaUtileKw, riga.Impianto.Pdr));
        Assert.Equal(new DateOnly(2018, 3, 15), riga.Impianto.Generatore!.InstallatoIl);

        // Senza codici il cliente si riconosce dal nome, e lo si segnala.
        Assert.Contains(analisi.Avvisi, a => a.Codice == nameof(AvvisoImportazione.ClienteSenzaCodici));
    }

    [Fact]
    public void Le_colonne_obbligatorie_mancanti_fermano_tutto_il_file()
    {
        var analisi = Analizza("nome;cognome;colore preferito", "Giulia;Esempio;blu");

        Assert.Empty(analisi.Righe);
        Assert.Equal(4, analisi.Errori.Count(e => e.Codice == nameof(ErroreImportazione.ColonnaObbligatoriaMancante)));
        Assert.Contains(analisi.Avvisi, a => a.Codice == nameof(AvvisoImportazione.ColonnaSconosciuta) && a.Dettaglio == "colore preferito");
    }

    [Fact]
    public void Gli_errori_indicano_la_riga()
    {
        var analisi = Analizza(
            Intestazione,
            "azienda;Termoidraulica Prova Srl;;;01234567897;;;Via Uno;1;50100;Firenze;FI;prova_riscaldamento;tanta;PDR1;;",
            "azienda;;;;;;;Via Due;2;501;Firenze;FI;;;;;",
            "privato;;Mario;Prova;;non-una-email;;Via Tre;3;50100;Firenze;FI;prova_riscaldamento;;PDR1;;01/13/2018");

        Assert.Empty(analisi.Righe);
        Assert.Contains(analisi.Errori, e => e is { Riga: 2, Codice: nameof(ErroreImportazione.NumeroNonValido) });
        Assert.Contains(analisi.Errori, e => e is { Riga: 3, Codice: nameof(ErroreAnagrafica.DenominazioneMancante) });
        Assert.Contains(analisi.Errori, e => e is { Riga: 3, Codice: nameof(ErroreAnagrafica.CapNonValido) });
        Assert.Contains(analisi.Errori, e => e is { Riga: 4, Codice: nameof(ErroreImportazione.EmailClienteNonValida) });
        Assert.Contains(analisi.Errori, e => e is { Riga: 4, Codice: nameof(ErroreImportazione.GeneratoreSenzaTipo) });
    }

    [Fact]
    public void Nel_file_un_pdr_vale_per_un_solo_impianto_e_un_cliente_ha_un_solo_nome()
    {
        var analisi = Analizza(
            Intestazione,
            "azienda;Clima Prova Srl;;;01234567897;;;Via Uno;1;50100;Firenze;FI;prova_riscaldamento;;PDR1;;",
            "azienda;Clima Prova Srl;;;01234567897;;;Via Uno;1;50100;Firenze;FI;prova_climatizzazione;;PDR1;;",
            "azienda;Clima Prova Diversa Srl;;;01234567897;;;Via Due;2;50100;Firenze;FI;;;;;");

        Assert.Contains(analisi.Errori, e => e is { Riga: 3, Codice: nameof(ErroreImportazione.PdrRipetutoNelFile) });
        Assert.Contains(analisi.Errori, e => e is { Riga: 4, Codice: nameof(ErroreImportazione.ClienteDiversoNelFile), Dettaglio: "2" });

        // Le prime due righe sono lo stesso cliente nella stessa sede.
        var valide = analisi.Righe.Where(r => r.Riga is 2 or 3).ToList();
        Assert.Single(valide.Select(r => r.ChiaveCliente).Distinct());
        Assert.Single(valide.Select(r => r.ChiaveSede).Distinct());
    }

    [Fact]
    public void Separatore_e_codifica_si_riconoscono_da_soli()
    {
        // Excel in italiano senza "UTF-8" salva in Windows-1252: gli accenti devono restare.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var testo = "indirizzo,cap,comune,provincia,cognome,nome\r\nVia Sàn Niccolò,50100,Firenze,FI,Città,Niccolò\r\n";
        var analisi = RegoleImportazione.Analizza(Encoding.GetEncoding(1252).GetBytes(testo));

        Assert.Empty(analisi.Errori);
        var riga = Assert.Single(analisi.Righe);
        Assert.Equal(("Via Sàn Niccolò", "Niccolò", TipiCliente.Privato), (riga.Sede.Indirizzo, riga.Cliente.Nome, riga.Cliente.Tipo));
        Assert.Null(riga.Impianto);
    }

    [Theory]
    [InlineData("Ragione sociale", "ragione_sociale")]
    [InlineData("  CODICE_FISCALE ", "codice_fiscale")]
    [InlineData("Città", "citta")]
    [InlineData("Potenza (kW)", "potenza_kw")]
    public void I_nomi_delle_colonne_si_normalizzano(string nome, string atteso) =>
        Assert.Equal(atteso, RegoleImportazione.NormalizzaNome(nome));

    [Fact]
    public void Un_file_vuoto_o_con_virgolette_aperte_si_rifiuta()
    {
        Assert.Equal(nameof(ErroreImportazione.FileVuoto), Assert.Single(Analizza(Intestazione).Errori).Codice);
        Assert.Equal(nameof(ErroreImportazione.VirgoletteNonChiuse), Assert.Single(Analizza(Intestazione, "\"aperta;").Errori).Codice);
    }
}

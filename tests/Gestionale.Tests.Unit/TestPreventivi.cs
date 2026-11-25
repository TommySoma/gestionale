using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Commerciale;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Commerciale;
using Gestionale.Domain.Interventi;
using Gestionale.Infrastructure.Documenti;
using UglyToad.PdfPig;

namespace Gestionale.Tests.Unit;

public sealed class TestRegoleCommerciali
{
    [Fact]
    public void Una_voce_valida_viene_normalizzata()
    {
        var (voce, errori) = RegoleCommerciali.Valida(new DatiVoce(" man-01 ", " Manodopera ", "Manodopera", null, 22, null, " "));

        Assert.Empty(errori);
        Assert.Equal(("MAN-01", "Manodopera", "manodopera", "h", true), (voce.Codice, voce.Descrizione, voce.Tipo, voce.Unita, voce.Attiva));
        Assert.Null(voce.Note);
    }

    [Fact]
    public void Gli_errori_di_una_voce_arrivano_tutti_insieme()
    {
        var errori = RegoleCommerciali.Valida(new DatiVoce("A B", null, "manodopera", "pz", 150, true, null)).Errori;

        Assert.Equal(
            [ErroreCommerciale.CodiceVoceNonValido, ErroreCommerciale.DescrizioneVoceMancante, ErroreCommerciale.ManodoperaVoceNonInOre, ErroreCommerciale.IvaVoceNonValida],
            errori.Cast<ErroreCommerciale>());
    }

    [Fact]
    public void Il_prezzo_non_è_negativo_e_ha_due_decimali()
    {
        Assert.Empty(RegoleCommerciali.Valida(new DatiPrezzo(45.50m, null)));
        Assert.Equal([ErroreCommerciale.PrezzoListinoNonValido], RegoleCommerciali.Valida(new DatiPrezzo(-1, null)).Cast<ErroreCommerciale>());
        Assert.Equal([ErroreCommerciale.PrezzoListinoNonValido], RegoleCommerciali.Valida(new DatiPrezzo(1.234m, null)).Cast<ErroreCommerciale>());
        Assert.Equal([ErroreCommerciale.PrezzoListinoNonValido], RegoleCommerciali.Valida(new DatiPrezzo(null, null)).Cast<ErroreCommerciale>());
    }

    [Fact]
    public void Il_preventivo_usa_le_regole_delle_righe_degli_interventi()
    {
        var righe = new[]
        {
            new RigaPreventivo(Guid.Empty, new DatiRiga("Manodopera", null, "Ore", null, 2, 40, null, 22)),
            new RigaPreventivo(null, new DatiRiga("materiale", null, " ", "pz", 0, -1, null, null)),
        };

        var (dati, normalizzate, errori) = RegoleCommerciali.Valida(new DatiPreventivo(Guid.CreateVersion7(), Guid.Empty, null, " Caldaia nuova ", null, null), righe);

        Assert.Equal(("Caldaia nuova", RegoleCommerciali.ValiditaPredefinita), (dati.Oggetto, dati.ValiditaGiorni));
        Assert.Null(dati.SedeId);
        Assert.Null(normalizzate[0].VoceId);
        Assert.Equal(("manodopera", "h"), (normalizzate[0].Riga.Tipo, normalizzate[0].Riga.Unita));
        Assert.Equal<Enum>(
            [ErroreIntervento.RigaDescrizioneMancante, ErroreIntervento.RigaQuantitaNonValida, ErroreIntervento.RigaPrezzoNonValido],
            errori);
    }

    [Fact]
    public void Senza_cliente_né_oggetto_e_con_una_validità_assurda_il_preventivo_non_va()
    {
        var errori = RegoleCommerciali.Valida(new DatiPreventivo(null, null, null, " ", null, 400), []).Errori;

        Assert.Equal(
            [ErroreCommerciale.ClientePreventivoNonValido, ErroreCommerciale.OggettoPreventivoMancante, ErroreCommerciale.ValiditaPreventivoNonValida],
            errori.Cast<ErroreCommerciale>());
    }

    [Fact]
    public void Ogni_errore_e_stato_ha_il_suo_testo()
    {
        var nomi = Enum.GetNames<ErroreCommerciale>()
            .Concat(new[] { "bozza", "emesso", "accettato", "rifiutato", "annullato", "convertito" }.Select(s => "StatoPreventivo_" + s));

        Assert.All(nomi, nome => Assert.False(string.IsNullOrWhiteSpace(Messaggi.ResourceManager.GetString(nome, Messaggi.Culture)), nome));
    }
}

public sealed class TestServizioPreventivi
{
    private static readonly Contesto s_contesto = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task La_conversione_vuole_un_tipo_di_intervento_valido()
    {
        var repository = new RepositoryFinto();
        var servizio = new ServizioPreventivi(repository, new GeneratorePreventivo());

        var senza = await servizio.ConvertiAsync(s_contesto, Guid.CreateVersion7(), 3, null, Ct);
        var sbagliato = await servizio.ConvertiAsync(s_contesto, Guid.CreateVersion7(), 3, "pulizia", Ct);
        var giusto = await servizio.ConvertiAsync(s_contesto, Guid.CreateVersion7(), 3, " Installazione ", Ct);

        Assert.Equal([ErroreCommerciale.TipoInterventoPreventivoNonValido], Assert.IsType<EsitoCommerciale.NonValida>(senza).Errori.Cast<ErroreCommerciale>());
        Assert.IsType<EsitoCommerciale.NonValida>(sbagliato);
        Assert.IsType<EsitoCommerciale.Riuscita>(giusto);
        Assert.Equal("installazione", repository.UltimoTipo);
    }

    [Fact]
    public async Task L_annullamento_vuole_il_motivo()
    {
        var servizio = new ServizioPreventivi(new RepositoryFinto(), new GeneratorePreventivo());

        var esito = await servizio.AnnullaAsync(s_contesto, Guid.CreateVersion7(), 1, "  ", Ct);

        Assert.Equal(
            [ErroreCommerciale.MotivoAnnullamentoPreventivoMancante],
            Assert.IsType<EsitoCommerciale.NonValida>(esito).Errori.Cast<ErroreCommerciale>());
    }

    private sealed class RepositoryFinto : IRepositoryPreventivi
    {
        public string? UltimoTipo { get; private set; }

        public Task<Pagina<RiepilogoPreventivo>> CercaAsync(Contesto contesto, FiltroPreventivi filtro, int numeroPagina, int dimensione, CancellationToken ct) =>
            Task.FromResult(new Pagina<RiepilogoPreventivo>([], 0, numeroPagina, dimensione));

        public Task<Preventivo?> PreventivoAsync(Contesto contesto, Guid preventivoId, CancellationToken ct) => Task.FromResult<Preventivo?>(null);

        public Task<IntestazioneAzienda> AziendaAsync(Contesto contesto, CancellationToken ct) => Task.FromResult(new IntestazioneAzienda());

        public Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> AggiornaAsync(
            Contesto contesto, Guid preventivoId, int versione, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> EliminaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> EmettiAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> RispostaClienteAsync(Contesto contesto, Guid preventivoId, int versione, bool accettato, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> AnnullaAsync(Contesto contesto, Guid preventivoId, int versione, string motivo, CancellationToken ct) => Riuscita();

        public Task<EsitoCommerciale> ConvertiAsync(Contesto contesto, Guid preventivoId, int versione, string tipoIntervento, CancellationToken ct)
        {
            UltimoTipo = tipoIntervento;
            return Riuscita();
        }

        private static Task<EsitoCommerciale> Riuscita() => Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.Riuscita(Guid.CreateVersion7(), 2));
    }
}

/// <summary>Il PDF del preventivo generato davvero e riletto con PdfPig. Dati fittizi; una copia finisce tra gli artefatti della CI.</summary>
public sealed class TestPdfPreventivo
{
    private static readonly IntestazioneAzienda s_azienda = new()
    {
        RagioneSociale = "Termoidraulica Alfa Srl (fittizia)",
        PartitaIva = "01234567897",
        Indirizzo = "Via delle Prove 1",
        Cap = "50100",
        Comune = "Firenze",
        Provincia = "FI",
    };

    private static Preventivo Preventivo(string stato, int? numero, bool scaduto = false) => new()
    {
        PreventivoId = Guid.CreateVersion7(),
        Anno = numero is null ? null : 2027,
        Numero = numero,
        Stato = stato,
        Scaduto = scaduto,
        Cliente = "Rossi Mario",
        CodiceFiscale = "RSSMRA80A01H501U",
        Indirizzo = "Via dei Platani",
        Civico = "12",
        Cap = "50100",
        Comune = "Firenze",
        Provincia = "FI",
        TipoImpianto = "Riscaldamento (voce di prova)",
        Oggetto = "Sostituzione del vaso di espansione (prova)",
        Note = "Prezzi validi per lavori eseguiti in orario ordinario.",
        ValiditaGiorni = 30,
        EmessoIl = numero is null ? null : new DateOnly(2027, 1, 11),
        ScadeIl = numero is null ? null : new DateOnly(2027, 2, 10),
        Imponibile = 157.50m,
        Iva = 34.65m,
        Totale = 192.15m,
        Righe =
        [
            new() { Posizione = 1, Tipo = "manodopera", Codice = "MAN-01", Descrizione = "Manodopera tecnico", Unita = "h", Quantita = 1.5m, PrezzoUnitario = 45m, AliquotaIva = 22m, Importo = 67.50m },
            new() { Posizione = 2, Tipo = "materiale", Codice = "VAS-08", Descrizione = "Vaso di espansione 8 l (prova)", Unita = "pz", Quantita = 1m, PrezzoUnitario = 90m, AliquotaIva = 22m, Importo = 90m },
        ],
    };

    [Fact]
    public void Il_preventivo_emesso_ha_numero_validità_righe_totali_e_accettazione()
    {
        var pdf = new GeneratorePreventivo().Genera(s_azienda, Preventivo("emesso", 7));
        Salva(pdf, "preventivo.pdf");

        var testo = Testo(pdf);

        Assert.Contains("Preventivo n. 7/2027", testo, StringComparison.Ordinal);
        Assert.Contains("10 febbraio 2027", testo, StringComparison.Ordinal);
        Assert.Contains("RSSMRA80A01H501U", testo, StringComparison.Ordinal);
        Assert.Contains("VAS-08 · Vaso di espansione 8 l (prova)", testo, StringComparison.Ordinal);
        Assert.Contains("157,50", testo, StringComparison.Ordinal);
        Assert.Contains("192,15", testo, StringComparison.Ordinal);
        Assert.Contains("Per accettazione", testo, StringComparison.Ordinal);
        Assert.DoesNotContain("Bozza", testo, StringComparison.Ordinal);
    }

    [Fact]
    public void La_bozza_e_lo_scaduto_lo_dicono_in_evidenza()
    {
        var bozza = Testo(new GeneratorePreventivo().Genera(s_azienda, Preventivo("bozza", null)));
        var scaduto = Testo(new GeneratorePreventivo().Genera(s_azienda, Preventivo("emesso", 7, scaduto: true)));

        Assert.Contains("Bozza di preventivo", bozza, StringComparison.Ordinal);
        Assert.Contains("30 giorni dall'emissione", bozza, StringComparison.Ordinal);
        Assert.Contains("Validità scaduta", scaduto, StringComparison.Ordinal);
    }

    private static string Testo(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        return string.Join(" ", documento.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static void Salva(byte[] pdf, string nome)
    {
        var cartella = Path.Combine(AppContext.BaseDirectory, "schermate");
        Directory.CreateDirectory(cartella);
        File.WriteAllBytes(Path.Combine(cartella, nome), pdf);
    }
}

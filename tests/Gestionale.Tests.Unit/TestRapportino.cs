using Gestionale.Application.Interventi;
using Gestionale.Infrastructure.Documenti;
using SkiaSharp;
using UglyToad.PdfPig;

namespace Gestionale.Tests.Unit;

/// <summary>
/// Il rapportino PDF generato davvero e riletto con PdfPig. Dati fittizi. Una copia
/// finisce accanto alle schermate (bin/.../schermate), tra gli artefatti della CI.
/// </summary>
public sealed class TestRapportino
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

    private static Intervento Intervento(IReadOnlyList<RigaIntervento> righe) => new()
    {
        InterventoId = Guid.CreateVersion7(),
        Anno = 2027,
        Numero = 12,
        Tipo = "manutenzione",
        Stato = "completato",
        Esito = "risolto",
        Inizio = new DateTime(2027, 1, 11, 8, 0, 0, DateTimeKind.Utc),
        Fine = new DateTime(2027, 1, 11, 10, 0, 0, DateTimeKind.Utc),
        Tecnico = "Marco Bianchi",
        TipoImpianto = "Riscaldamento (voce di prova)",
        Cliente = "Rossi Mario",
        Indirizzo = "Via dei Platani",
        Civico = "12",
        Cap = "50100",
        Comune = "Firenze",
        Provincia = "FI",
        Descrizione = "Manutenzione annuale, il cliente segnala rumore all'accensione.",
        NoteChiusura = "Pulito il bruciatore e sostituita la guarnizione.",
        ChiusoIl = new DateTime(2027, 1, 11, 10, 5, 0, DateTimeKind.Utc),
        Righe = righe,
    };

    private static readonly RigaIntervento[] s_righe =
    [
        new() { Posizione = 1, Tipo = "manodopera", Descrizione = "Manutenzione ordinaria", Unita = "h", Quantita = 1.5m, PrezzoUnitario = 40m, AliquotaIva = 22m, Importo = 60m },
        new() { Posizione = 2, Tipo = "materiale", Codice = "GUA-01", Descrizione = "Guarnizione di prova", Unita = "pz", Quantita = 2m, PrezzoUnitario = 4.99m, ScontoPercentuale = 10m, AliquotaIva = 22m, Importo = 8.98m },
    ];

    [Fact]
    public void Il_rapportino_contiene_intestazione_dati_righe_totali_e_disclaimer()
    {
        var pdf = new GeneratoreRapportino().Genera(s_azienda, Intervento(s_righe));
        Salva(pdf, "rapportino-breve.pdf");

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var testo = Testo(pdf, out var pagine);

        Assert.Equal(1, pagine);
        Assert.Contains("Termoidraulica Alfa Srl (fittizia)", testo, StringComparison.Ordinal);
        Assert.Contains("Rapporto di intervento n. 12/2027", testo, StringComparison.Ordinal);
        // Ore italiane: 08:00 UTC sono le 09:00 a gennaio.
        Assert.Contains("lunedì 11 gennaio 2027, dalle 09:00 alle 11:00", testo, StringComparison.Ordinal);
        Assert.Contains("Marco Bianchi", testo, StringComparison.Ordinal);
        Assert.Contains("GUA-01 · Guarnizione di prova", testo, StringComparison.Ordinal);
        // Imponibile 68,98; IVA 22% = 15,18; totale 84,16.
        Assert.Contains("68,98", testo, StringComparison.Ordinal);
        Assert.Contains("15,18", testo, StringComparison.Ordinal);
        Assert.Contains("84,16", testo, StringComparison.Ordinal);
        Assert.Contains("Firma del cliente", testo, StringComparison.Ordinal);
        // Nel piè di pagina il carattere è piccolo e PdfPig separa le parole a modo suo: si confronta senza spazi.
        Assert.Contains("lafirmaelaresponsabilitàrestano", SenzaSpazi(testo), StringComparison.Ordinal);
        Assert.Contains("1 / 1", testo, StringComparison.Ordinal);
    }

    [Fact]
    public void Con_molte_righe_si_va_a_pagina_nuova_con_intestazione_e_disclaimer_su_ogni_pagina()
    {
        var righe = Enumerable.Range(1, 90).Select(i => new RigaIntervento
        {
            Posizione = i,
            Tipo = "materiale",
            Codice = $"COD-{i:D3}",
            Descrizione = i % 7 == 0 ? "Raccordo di prova con una descrizione lunga che deve andare a capo nella colonna senza uscire dal margine" : "Raccordo di prova",
            Unita = "pz",
            Quantita = 1,
            PrezzoUnitario = 2.5m,
            AliquotaIva = i == 90 ? null : 22m,
            Importo = 2.5m,
        }).ToList();

        var pdf = new GeneratoreRapportino().Genera(s_azienda, Intervento(righe));
        Salva(pdf, "rapportino-lungo.pdf");

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 3, $"Pagine: {documento.NumberOfPages}");
        foreach (var pagina in documento.GetPages())
        {
            var testo = string.Join(" ", pagina.GetWords().Select(w => w.Text));
            Assert.Contains("responsabilità", testo, StringComparison.Ordinal);
            Assert.Contains($"{pagina.Number} / {documento.NumberOfPages}", testo, StringComparison.Ordinal);
            if (pagina.Number > 1 && testo.Contains("COD-", StringComparison.Ordinal))
            {
                Assert.Contains("Descrizione", testo, StringComparison.Ordinal);
            }
        }

        var tutto = string.Join(" ", documento.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
        Assert.Contains("COD-090", tutto, StringComparison.Ordinal);
        Assert.Contains("aliquota IVA non è indicata", tutto, StringComparison.Ordinal);
    }

    [Fact]
    public void Lo_stesso_intervento_produce_lo_stesso_file()
    {
        var generatore = new GeneratoreRapportino();
        var intervento = Intervento(s_righe);

        Assert.Equal(generatore.Genera(s_azienda, intervento), generatore.Genera(s_azienda, intervento));
    }

    [Fact]
    public void Una_parola_più_larga_della_colonna_si_spezza()
    {
        using var font = new SKFont(SKTypeface.Default, 10);

        var righe = Impaginatore.Spezza("CODICELUNGHISSIMOSENZASPAZI breve", font, 40);

        Assert.True(righe.Count > 2);
        Assert.All(righe, r => Assert.True(font.MeasureText(r) <= 40 || r.Length == 1, r));
        Assert.Equal("CODICELUNGHISSIMOSENZASPAZIbreve", string.Concat(righe).Replace(" ", "", StringComparison.Ordinal));
    }

    private static string Testo(byte[] pdf, out int pagine)
    {
        using var documento = PdfDocument.Open(pdf);
        pagine = documento.NumberOfPages;
        return string.Join(" ", documento.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static string SenzaSpazi(string testo) => string.Concat(testo.Where(c => !char.IsWhiteSpace(c)));

    private static void Salva(byte[] pdf, string nome)
    {
        var cartella = Path.Combine(AppContext.BaseDirectory, "schermate");
        Directory.CreateDirectory(cartella);
        File.WriteAllBytes(Path.Combine(cartella, nome), pdf);
    }
}

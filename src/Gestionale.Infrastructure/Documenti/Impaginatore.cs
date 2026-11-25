using System.Reflection;
using SkiaSharp;

namespace Gestionale.Infrastructure.Documenti;

/// <summary>Allineamento di una colonna di tabella.</summary>
public enum Allineamento
{
    Sinistra,
    Destra,
}

public sealed record Colonna(string Titolo, float Larghezza, Allineamento Allineamento = Allineamento.Sinistra);

/// <summary>
/// Impaginatore minimo per i documenti del gestionale (ADR 0005): pagine A4 in
/// punti tipografici, testo a capo, tabelle con l'intestazione ripetuta a ogni
/// pagina, blocchi che non si spezzano e un piè di pagina con disclaimer e numero
/// di pagina. Prima si impagina in memoria, poi si disegna: così il piè di pagina
/// conosce il numero totale di pagine.
/// </summary>
public sealed class Impaginatore
{
    public const float LarghezzaPagina = 595.28f;
    public const float AltezzaPagina = 841.89f;
    public const float Margine = 42f;

    private const float AltezzaPiede = 40f;
    private const float Interlinea = 1.35f;

    private static readonly Lazy<SKTypeface> s_normale = new(() => Carattere("Inter-Regular.ttf"));
    private static readonly Lazy<SKTypeface> s_grassetto = new(() => Carattere("Inter-SemiBold.ttf"));

    private static readonly SKColor s_testo = new(0x17, 0x20, 0x26);
    private static readonly SKColor s_secondario = new(0x55, 0x62, 0x6B);
    private static readonly SKColor s_linea = new(0xDC, 0xE1, 0xE4);
    private static readonly SKColor s_fondoIntestazione = new(0xEE, 0xF1, 0xF3);

    private readonly List<List<Action<SKCanvas>>> _pagine = [];
    private readonly string _piede;
    private float _y;

    public Impaginatore(string piede)
    {
        _piede = piede;
        NuovaPagina();
    }

    public static float LarghezzaUtile => LarghezzaPagina - (2 * Margine);

    private static float Fondo => AltezzaPagina - Margine - AltezzaPiede;

    private List<Action<SKCanvas>> Corrente => _pagine[^1];

    public void NuovaPagina()
    {
        _pagine.Add([]);
        _y = Margine;
    }

    public void Spazio(float punti) => _y += punti;

    /// <summary>Paragrafo a capo sulla larghezza indicata; va a pagina nuova se serve.</summary>
    public void Testo(string testo, float dimensione = 10, bool grassetto = false, bool secondario = false, float? larghezza = null, float rientro = 0)
    {
        using var font = Font(dimensione, grassetto);
        var righe = Spezza(testo, font, larghezza ?? LarghezzaUtile - rientro);
        var altezzaRiga = dimensione * Interlinea;
        foreach (var riga in righe)
        {
            if (_y + altezzaRiga > Fondo)
            {
                NuovaPagina();
            }

            var y = _y + dimensione;
            Disegna(c => ScriviTesto(c, riga, Margine + rientro, y, dimensione, grassetto, secondario ? s_secondario : s_testo));
            _y += altezzaRiga;
        }
    }

    /// <summary>Coppie etichetta e valore su due colonne affiancate, senza spezzarle tra due pagine.</summary>
    public void Campi(IReadOnlyList<(string Etichetta, string Valore)> campi)
    {
        const float dimensioneEtichetta = 8;
        const float dimensioneValore = 10;
        var larghezzaColonna = (LarghezzaUtile - 24) / 2;

        for (var i = 0; i < campi.Count; i += 2)
        {
            var coppia = campi.Skip(i).Take(2).ToList();
            using var font = Font(dimensioneValore, false);
            var righe = coppia.Select(c => Spezza(c.Valore, font, larghezzaColonna)).ToList();
            var altezza = (dimensioneEtichetta * Interlinea) + (righe.Max(r => r.Count) * dimensioneValore * Interlinea) + 6;
            Riserva(altezza);

            for (var k = 0; k < coppia.Count; k++)
            {
                var x = Margine + (k * (larghezzaColonna + 24));
                var y = _y;
                var etichetta = coppia[k].Etichetta.ToUpperInvariant();
                var valore = righe[k];
                Disegna(c =>
                {
                    ScriviTesto(c, etichetta, x, y + dimensioneEtichetta, dimensioneEtichetta, true, s_secondario);
                    for (var r = 0; r < valore.Count; r++)
                    {
                        ScriviTesto(c, valore[r], x, y + (dimensioneEtichetta * Interlinea) + dimensioneValore + (r * dimensioneValore * Interlinea), dimensioneValore, false, s_testo);
                    }
                });
            }

            _y += altezza;
        }
    }

    /// <summary>Titolo di sezione con una riga sotto; resta sulla stessa pagina del contenuto che segue.</summary>
    public void Sezione(string titolo)
    {
        Riserva(60);
        Spazio(6);
        Testo(titolo, 11, grassetto: true);
        var y = _y + 1;
        Disegna(c => Linea(c, Margine, y, Margine + LarghezzaUtile, y));
        Spazio(8);
    }

    /// <summary>Tabella: le celle vanno a capo, l'intestazione si ripete a ogni pagina.</summary>
    public void Tabella(IReadOnlyList<Colonna> colonne, IEnumerable<IReadOnlyList<string>> righe)
    {
        const float dimensione = 9;
        const float padding = 4;
        var altezzaRiga = dimensione * Interlinea;

        void Intestazione()
        {
            var altezza = altezzaRiga + (2 * padding);
            Riserva(altezza + altezzaRiga + (2 * padding));
            var y = _y;
            Disegna(c =>
            {
                using var fondo = new SKPaint { Color = s_fondoIntestazione };
                c.DrawRect(Margine, y, LarghezzaUtile, altezza, fondo);
                var x = Margine;
                foreach (var colonna in colonne)
                {
                    ScriviCella(c, colonna.Titolo, x, y + padding + dimensione, colonna, dimensione, true, s_secondario);
                    x += colonna.Larghezza;
                }
            });
            _y += altezza;
        }

        Intestazione();
        using var font = Font(dimensione, false);
        foreach (var riga in righe)
        {
            var celle = colonne.Select((col, i) => Spezza(i < riga.Count ? riga[i] : "", font, col.Larghezza - (2 * padding))).ToList();
            var altezza = (celle.Max(c => c.Count) * altezzaRiga) + (2 * padding);
            if (_y + altezza > Fondo)
            {
                NuovaPagina();
                Intestazione();
            }

            var y = _y;
            Disegna(c =>
            {
                var x = Margine;
                for (var i = 0; i < colonne.Count; i++)
                {
                    for (var r = 0; r < celle[i].Count; r++)
                    {
                        ScriviCella(c, celle[i][r], x, y + padding + dimensione + (r * altezzaRiga), colonne[i], dimensione, false, s_testo);
                    }

                    x += colonne[i].Larghezza;
                }

                Linea(c, Margine, y + altezza, Margine + LarghezzaUtile, y + altezza);
            });
            _y += altezza;
        }
    }

    /// <summary>Righe di totale allineate a destra; l'ultima in grassetto.</summary>
    public void Totali(IReadOnlyList<(string Etichetta, string Valore)> totali)
    {
        const float dimensione = 10;
        Riserva((totali.Count * dimensione * 1.6f) + 8);
        Spazio(6);
        for (var i = 0; i < totali.Count; i++)
        {
            var (etichetta, valore) = totali[i];
            var grassetto = i == totali.Count - 1;
            var y = _y + dimensione;
            Disegna(c =>
            {
                ScriviTesto(c, etichetta, Margine + LarghezzaUtile - 220, y, dimensione, grassetto, grassetto ? s_testo : s_secondario);
                ScriviDestra(c, valore, Margine + LarghezzaUtile, y, dimensione, grassetto, s_testo);
            });
            _y += dimensione * 1.6f;
        }
    }

    /// <summary>Riquadri affiancati per le firme a mano: il gestionale non firma nulla.</summary>
    public void Firme(params string[] etichette)
    {
        const float altezza = 70;
        Riserva(altezza + 16);
        Spazio(16);
        var larghezza = (LarghezzaUtile - ((etichette.Length - 1) * 24)) / etichette.Length;
        var y = _y;
        for (var i = 0; i < etichette.Length; i++)
        {
            var x = Margine + (i * (larghezza + 24));
            var etichetta = etichette[i];
            Disegna(c =>
            {
                ScriviTesto(c, etichetta, x, y + 8, 8, true, s_secondario);
                Linea(c, x, y + altezza, x + larghezza, y + altezza);
            });
        }

        _y += altezza;
    }

    /// <summary>Disegna le pagine e restituisce il PDF.</summary>
    public byte[] Pdf(string titolo, string autore)
    {
        using var flusso = new MemoryStream();
        var metadati = new SKDocumentPdfMetadata
        {
            Title = titolo,
            Author = autore,
            Creator = "Gestionale impianti",
            Producer = "Gestionale impianti (SkiaSharp)",
            // Data fissa: lo stesso intervento produce lo stesso file, e i test restano ripetibili.
            Creation = DateTime.UnixEpoch,
            Modified = DateTime.UnixEpoch,
        };

        using (var documento = SKDocument.CreatePdf(flusso, metadati))
        {
            for (var p = 0; p < _pagine.Count; p++)
            {
                var canvas = documento.BeginPage(LarghezzaPagina, AltezzaPagina);
                foreach (var disegno in _pagine[p])
                {
                    disegno(canvas);
                }

                Piede(canvas, p + 1, _pagine.Count);
                documento.EndPage();
            }

            documento.Close();
        }

        return flusso.ToArray();
    }

    /// <summary>Numero di pagine impaginate finora.</summary>
    public int Pagine => _pagine.Count;

    private void Piede(SKCanvas c, int pagina, int totale)
    {
        const float dimensione = 7.5f;
        var y = AltezzaPagina - Margine - AltezzaPiede + 12;
        Linea(c, Margine, y, Margine + LarghezzaUtile, y);
        using var font = Font(dimensione, false);
        var numero = $"{pagina} / {totale}";
        var righe = Spezza(_piede, font, LarghezzaUtile - 60);
        for (var r = 0; r < righe.Count; r++)
        {
            ScriviTesto(c, righe[r], Margine, y + 12 + (r * dimensione * Interlinea), dimensione, false, s_secondario);
        }

        ScriviDestra(c, numero, Margine + LarghezzaUtile, y + 12, dimensione, false, s_secondario);
    }

    // Se il blocco non entra in quello che resta della pagina, comincia su una nuova.
    private void Riserva(float altezza)
    {
        if (_y + altezza > Fondo && Corrente.Count > 0)
        {
            NuovaPagina();
        }
    }

    private void Disegna(Action<SKCanvas> disegno) => Corrente.Add(disegno);

    private static void ScriviCella(SKCanvas c, string testo, float x, float y, Colonna colonna, float dimensione, bool grassetto, SKColor colore)
    {
        const float padding = 4;
        if (colonna.Allineamento == Allineamento.Destra)
        {
            ScriviDestra(c, testo, x + colonna.Larghezza - padding, y, dimensione, grassetto, colore);
        }
        else
        {
            ScriviTesto(c, testo, x + padding, y, dimensione, grassetto, colore);
        }
    }

    private static void ScriviTesto(SKCanvas c, string testo, float x, float y, float dimensione, bool grassetto, SKColor colore)
    {
        using var font = Font(dimensione, grassetto);
        using var pennello = new SKPaint { Color = colore, IsAntialias = true };
        c.DrawText(testo, x, y, SKTextAlign.Left, font, pennello);
    }

    private static void ScriviDestra(SKCanvas c, string testo, float destra, float y, float dimensione, bool grassetto, SKColor colore)
    {
        using var font = Font(dimensione, grassetto);
        using var pennello = new SKPaint { Color = colore, IsAntialias = true };
        c.DrawText(testo, destra, y, SKTextAlign.Right, font, pennello);
    }

    private static void Linea(SKCanvas c, float x1, float y1, float x2, float y2)
    {
        using var pennello = new SKPaint { Color = s_linea, StrokeWidth = 0.75f, IsAntialias = true };
        c.DrawLine(x1, y1, x2, y2, pennello);
    }

    private static SKFont Font(float dimensione, bool grassetto) =>
        new(grassetto ? s_grassetto.Value : s_normale.Value, dimensione);

    /// <summary>Spezza il testo in righe che entrano nella larghezza; rispetta gli a capo e taglia le parole troppo lunghe.</summary>
    internal static List<string> Spezza(string testo, SKFont font, float larghezza)
    {
        var righe = new List<string>();
        foreach (var paragrafo in testo.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var riga = "";
            foreach (var parola in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidata = riga.Length == 0 ? parola : riga + " " + parola;
                if (font.MeasureText(candidata) <= larghezza)
                {
                    riga = candidata;
                    continue;
                }

                if (riga.Length > 0)
                {
                    righe.Add(riga);
                }

                // Una parola più larga della colonna (un codice lungo) si taglia a pezzi.
                riga = parola;
                while (font.MeasureText(riga) > larghezza && riga.Length > 1)
                {
                    var n = riga.Length - 1;
                    while (n > 1 && font.MeasureText(riga[..n]) > larghezza)
                    {
                        n--;
                    }

                    righe.Add(riga[..n]);
                    riga = riga[n..];
                }
            }

            righe.Add(riga);
        }

        return righe;
    }

    private static SKTypeface Carattere(string file)
    {
        using var risorsa = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Gestionale.Infrastructure.Documenti.{file}")
            ?? throw new InvalidOperationException($"Carattere {file} non incorporato.");
        using var dati = SKData.Create(risorsa);
        return SKTypeface.FromData(dati) ?? throw new InvalidOperationException($"Carattere {file} non leggibile.");
    }
}

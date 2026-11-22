namespace Gestionale.Domain.Anagrafica;

/// <summary>
/// Partita IVA: 11 cifre, l'ultima di controllo. Lo stesso algoritmo vale per
/// il codice fiscale numerico di società, enti e condomini.
/// </summary>
public static class PartitaIva
{
    public static bool Valida(string? valore)
    {
        if (valore is not { Length: 11 } || !valore.All(char.IsAsciiDigit))
        {
            return false;
        }

        var somma = 0;
        for (var i = 0; i < 10; i++)
        {
            var cifra = valore[i] - '0';
            if (i % 2 == 1)
            {
                cifra *= 2;
                if (cifra > 9)
                {
                    cifra -= 9;
                }
            }

            somma += cifra;
        }

        return (10 - (somma % 10)) % 10 == valore[10] - '0';
    }
}

/// <summary>
/// Codice fiscale: 16 caratteri per le persone fisiche (con l'eventuale
/// sostituzione delle cifre per omocodia), oppure 11 cifre per i soggetti diversi.
/// </summary>
public static class CodiceFiscale
{
    private const string Mesi = "ABCDEHLMPRST";
    private const string CifreOmocodia = "LMNPQRSTUV";

    // Posizioni (da 0) che contengono cifre, sostituibili con lettere in caso di omocodia.
    private static readonly int[] s_posizioniNumeriche = [6, 7, 9, 10, 12, 13, 14];

    // Valori dei caratteri in posizione dispari (1ª, 3ª, ...), per cifre e lettere dalla A alla Z.
    private static readonly int[] s_valoriDispari =
        [1, 0, 5, 7, 9, 13, 15, 17, 19, 21, 2, 4, 18, 20, 11, 3, 6, 8, 12, 14, 16, 10, 22, 25, 24, 23];

    /// <summary>Maiuscolo e senza spazi: la forma in cui si salva.</summary>
    public static string Normalizza(string valore)
    {
        ArgumentNullException.ThrowIfNull(valore);
        return new string([.. valore.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant)]);
    }

    public static bool Valido(string? valore) => valore switch
    {
        { Length: 11 } => PartitaIva.Valida(valore),
        { Length: 16 } => PersonaFisicaValida(valore),
        _ => false,
    };

    private static bool PersonaFisicaValida(string cf)
    {
        for (var i = 0; i < 15; i++)
        {
            var c = cf[i];
            var numerica = Array.IndexOf(s_posizioniNumeriche, i) >= 0;
            var ammesso = numerica ? char.IsAsciiDigit(c) || CifreOmocodia.Contains(c) : char.IsAsciiLetterUpper(c);
            if (!ammesso)
            {
                return false;
            }
        }

        if (!Mesi.Contains(cf[8]) || !char.IsAsciiLetterUpper(cf[15]))
        {
            return false;
        }

        // Giorno di nascita: 1-31 per gli uomini, 41-71 per le donne.
        var giorno = (Cifra(cf[9]) * 10) + Cifra(cf[10]);
        if (giorno is < 1 or > 71 || giorno is > 31 and < 41)
        {
            return false;
        }

        return CarattereDiControllo(cf) == cf[15];
    }

    private static int Cifra(char c) => char.IsAsciiDigit(c) ? c - '0' : CifreOmocodia.IndexOf(c, StringComparison.Ordinal);

    private static char CarattereDiControllo(string cf)
    {
        var somma = 0;
        for (var i = 0; i < 15; i++)
        {
            var c = cf[i];
            var indice = char.IsAsciiDigit(c) ? c - '0' : c - 'A';
            // Le posizioni si contano da 1: l'indice 0 è la prima posizione, dispari.
            somma += i % 2 == 0 ? s_valoriDispari[indice] : indice;
        }

        return (char)('A' + (somma % 26));
    }
}

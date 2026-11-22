namespace Gestionale.Domain.Sicurezza;

/// <summary>Motivi per cui una password non è accettata.</summary>
public enum ErrorePassword
{
    TroppoCorta,
    TroppoLunga,
    UgualeAllEmail,
    Nota,
}

/// <summary>
/// Regole del piano: almeno 12 caratteri, nessuna scadenza forzata, controllo
/// contro le password note. Il limite massimo evita che un input enorme renda
/// costoso l'hash.
/// </summary>
public static class PoliticaPassword
{
    public const int LunghezzaMinima = 12;
    public const int LunghezzaMassima = 128;

    public static IReadOnlyList<ErrorePassword> Valida(string password, string email, Func<string, bool> eNota)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(eNota);

        var errori = new List<ErrorePassword>();

        // Si contano i caratteri visibili, non le unità UTF-16.
        var lunghezza = password.EnumerateRunes().Count();
        if (lunghezza < LunghezzaMinima)
        {
            errori.Add(ErrorePassword.TroppoCorta);
        }

        if (lunghezza > LunghezzaMassima)
        {
            errori.Add(ErrorePassword.TroppoLunga);
        }

        var parteLocale = email.Split('@')[0];
        if (string.Equals(password, email, StringComparison.OrdinalIgnoreCase)
            || (parteLocale.Length > 0 && string.Equals(password, parteLocale, StringComparison.OrdinalIgnoreCase)))
        {
            errori.Add(ErrorePassword.UgualeAllEmail);
        }

        if (eNota(password))
        {
            errori.Add(ErrorePassword.Nota);
        }

        return errori;
    }
}

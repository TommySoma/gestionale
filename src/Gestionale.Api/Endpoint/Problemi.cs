using System.Globalization;
using System.Text.RegularExpressions;
using Gestionale.Api.Risorse;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Api.Endpoint;

/// <summary>Risposte di errore (RFC 9457) con testi presi dal file di traduzione.</summary>
internal static partial class Problemi
{
    public static IResult RichiestaNonValida(string dettaglio) =>
        Results.Problem(detail: dettaglio, statusCode: StatusCodes.Status400BadRequest, title: Messaggi.TitoloRichiestaNonValida);

    public static IResult AccessoNegato(string dettaglio) =>
        Results.Problem(detail: dettaglio, statusCode: StatusCodes.Status401Unauthorized, title: Messaggi.TitoloAccessoNegato);

    public static IResult Vietato(string dettaglio) =>
        Results.Problem(detail: dettaglio, statusCode: StatusCodes.Status403Forbidden, title: Messaggi.TitoloVietato);

    public static IResult Conflitto(string dettaglio) =>
        Results.Problem(detail: dettaglio, statusCode: StatusCodes.Status409Conflict, title: Messaggi.TitoloConflitto);

    public static string Formatta(string modello, params object[] valori) =>
        string.Format(CultureInfo.CurrentCulture, modello, valori);

    /// <summary>Elenco dei campi obbligatori vuoti, oppure null se sono tutti compilati.</summary>
    public static IResult? CampiMancanti(params (string Nome, string? Valore)[] campi)
    {
        var mancanti = campi.Where(c => string.IsNullOrWhiteSpace(c.Valore)).Select(c => c.Nome).ToList();
        return mancanti.Count == 0 ? null : RichiestaNonValida(Formatta(Messaggi.CampiMancanti, string.Join(", ", mancanti)));
    }

    public static IResult? EmailNonValida(string email) =>
        FormaEmail().IsMatch(email.Trim()) ? null : RichiestaNonValida(Formatta(Messaggi.EmailNonValida, email));

    public static IResult PasswordNonValida(string email, IReadOnlyList<ErrorePassword> errori) =>
        RichiestaNonValida(Formatta(Messaggi.PasswordNonValida, email, string.Join("; ", errori.Select(TestoErrore)) + "."));

    private static string TestoErrore(ErrorePassword errore) => errore switch
    {
        ErrorePassword.TroppoCorta => Messaggi.PasswordTroppoCorta,
        ErrorePassword.TroppoLunga => Messaggi.PasswordTroppoLunga,
        ErrorePassword.UgualeAllEmail => Messaggi.PasswordUgualeAllEmail,
        ErrorePassword.Nota => Messaggi.PasswordNota,
        _ => errore.ToString(),
    };

    // Stessa regola del vincolo ck_utenti_email nel database.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex FormaEmail();
}

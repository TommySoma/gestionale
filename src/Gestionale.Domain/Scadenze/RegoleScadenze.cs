namespace Gestionale.Domain.Scadenze;

/// <summary>Motivi per cui una richiesta sulle scadenze non è accettata; l'API li traduce in messaggi.</summary>
public enum ErroreScadenza
{
    DataScadenzaMancante,
    DataScadenzaFuoriIntervallo,
    CanaleRichiamoNonValido,
    EsitoRichiamoNonValido,
    RichiamareIlSoloDaRisentire,
    RichiamareIlNelPassato,
    InterventoSoloConAppuntamento,
    InterventoRichiamoNonValido,
    NoteRichiamoTroppoLunghe,
    GiorniNonValidi,
}

/// <summary>Tipi di scadenza (vincolo CHECK nel database, decisione del 25/11/2026).</summary>
public static class TipiScadenza
{
    public const string Manutenzione = "manutenzione";
    public const string ControlloEfficienza = "controllo_efficienza";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { Manutenzione, ControlloEfficienza };
}

public static class StatiScadenza
{
    public const string Aperta = "aperta";
    public const string Chiusa = "chiusa";
    public const string Annullata = "annullata";
}

public static class StatiRichiamo
{
    public const string NonContattato = "non_contattato";
    public const string InAttesa = "in_attesa";
    public const string Fissato = "fissato";
    public const string Rifiutato = "rifiutato";

    public static IReadOnlySet<string> Tutti { get; } =
        new HashSet<string>(StringComparer.Ordinal) { NonContattato, InAttesa, Fissato, Rifiutato };
}

public static class CanaliRichiamo
{
    public static IReadOnlySet<string> Tutti { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "telefono", "email", "sms", "whatsapp", "di_persona", "altro" };
}

public static class EsitiRichiamo
{
    public const string NessunaRisposta = "nessuna_risposta";
    public const string DaRichiamare = "da_richiamare";
    public const string Appuntamento = "appuntamento";
    public const string Rifiutato = "rifiutato";

    public static IReadOnlySet<string> Tutti { get; } =
        new HashSet<string>(StringComparer.Ordinal) { NessunaRisposta, DaRichiamare, Appuntamento, Rifiutato };
}

public sealed record DatiRichiamo(string? Canale, string? Esito, DateOnly? RichiamareIl, Guid? InterventoId, string? Note);

/// <summary>
/// Normalizzazione e validazione delle richieste sulle scadenze: le stesse regole
/// dei vincoli del database, con errori comprensibili e tutti insieme.
/// </summary>
public static class RegoleScadenze
{
    public const int GiorniMassimi = 3650;

    public const int LunghezzaMassimaNote = 2000;

    // Una data corretta a mano oltre questo margine è quasi sempre un errore di battitura.
    public const int AnniMassimi = 10;

    public static IReadOnlyList<ErroreScadenza> ValidaGiorni(int giorni) =>
        giorni is < 0 or > GiorniMassimi ? [ErroreScadenza.GiorniNonValidi] : [];

    /// <summary>Data corretta a mano: può essere passata (una scadenza già superata), ma non oltre dieci anni.</summary>
    public static IReadOnlyList<ErroreScadenza> ValidaData(DateOnly? data, DateOnly oggi) =>
        data switch
        {
            null => [ErroreScadenza.DataScadenzaMancante],
            { } d when d < oggi.AddYears(-AnniMassimi) || d > oggi.AddYears(AnniMassimi) => [ErroreScadenza.DataScadenzaFuoriIntervallo],
            _ => [],
        };

    public static (DatiRichiamo Dati, IReadOnlyList<ErroreScadenza> Errori) Valida(DatiRichiamo dati, DateOnly oggi)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            Canale = Pulisci(dati.Canale)?.ToLowerInvariant(),
            Esito = Pulisci(dati.Esito)?.ToLowerInvariant(),
            InterventoId = dati.InterventoId == Guid.Empty ? null : dati.InterventoId,
            Note = Pulisci(dati.Note),
        };

        var errori = new List<ErroreScadenza>();

        if (normalizzati.Canale is null || !CanaliRichiamo.Tutti.Contains(normalizzati.Canale))
        {
            errori.Add(ErroreScadenza.CanaleRichiamoNonValido);
        }

        if (normalizzati.Esito is null || !EsitiRichiamo.Tutti.Contains(normalizzati.Esito))
        {
            errori.Add(ErroreScadenza.EsitoRichiamoNonValido);
        }
        else
        {
            if (normalizzati.RichiamareIl is not null
                && normalizzati.Esito is not (EsitiRichiamo.NessunaRisposta or EsitiRichiamo.DaRichiamare))
            {
                errori.Add(ErroreScadenza.RichiamareIlSoloDaRisentire);
            }

            if (normalizzati.InterventoId is not null && normalizzati.Esito != EsitiRichiamo.Appuntamento)
            {
                errori.Add(ErroreScadenza.InterventoSoloConAppuntamento);
            }
        }

        if (normalizzati.RichiamareIl is { } quando && quando < oggi)
        {
            errori.Add(ErroreScadenza.RichiamareIlNelPassato);
        }

        if (normalizzati.Note is { Length: > LunghezzaMassimaNote })
        {
            errori.Add(ErroreScadenza.NoteRichiamoTroppoLunghe);
        }

        return (normalizzati, errori);
    }

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

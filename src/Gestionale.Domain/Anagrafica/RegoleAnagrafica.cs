using System.Text.RegularExpressions;

namespace Gestionale.Domain.Anagrafica;

/// <summary>Motivi per cui un dato anagrafico non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreAnagrafica
{
    TipoClienteNonValido,
    NomeCognomeMancanti,
    DenominazioneMancante,
    CodiceFiscaleNonValido,
    PartitaIvaNonValida,
    PecNonValida,
    IndirizzoMancante,
    ComuneMancante,
    CapNonValido,
    ProvinciaNonValida,
    CoordinateNonValide,
    TipoContattoNonValido,
    ValoreContattoNonValido,
    FinalitaSconosciuta,
    FonteConsensoNonValida,
    PartitaIvaInUso,
    ContattoPrincipaleGiaPresente,
}

public static class TipiCliente
{
    public const string Privato = "privato";
    public const string Condominio = "condominio";
    public const string Azienda = "azienda";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { Privato, Condominio, Azienda };
}

public static class TipiContatto
{
    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { "telefono", "cellulare", "email" };
}

public static class FontiConsenso
{
    public static IReadOnlySet<string> Tutte { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "modulo_firmato", "email", "telefono", "di_persona", "altro" };
}

public sealed record DatiCliente(
    string Tipo,
    string? Denominazione,
    string? Nome,
    string? Cognome,
    string? CodiceFiscale,
    string? PartitaIva,
    string? Pec,
    string? Note);

public sealed record DatiSede(
    string? Descrizione,
    string Indirizzo,
    string? Civico,
    string Cap,
    string Comune,
    string Provincia,
    string? NoteAccesso,
    decimal? Latitudine,
    decimal? Longitudine);

public sealed record DatiContatto(string Tipo, string Valore, string? Etichetta, bool Principale);

/// <summary>
/// Normalizzazione e validazione dei dati anagrafici. Le stesse regole di
/// formato sono anche vincoli del database; qui si aggiungono le cifre di
/// controllo e si producono errori comprensibili.
/// </summary>
public static partial class RegoleAnagrafica
{
    public static (DatiCliente Dati, IReadOnlyList<ErroreAnagrafica> Errori) Valida(DatiCliente dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = new DatiCliente(
            (dati.Tipo ?? "").Trim().ToLowerInvariant(),
            Pulisci(dati.Denominazione),
            Pulisci(dati.Nome),
            Pulisci(dati.Cognome),
            Pulisci(dati.CodiceFiscale) is { } cf ? CodiceFiscale.Normalizza(cf) : null,
            Pulisci(dati.PartitaIva)?.Replace(" ", "", StringComparison.Ordinal),
            Pulisci(dati.Pec)?.ToLowerInvariant(),
            Pulisci(dati.Note));

        var errori = new List<ErroreAnagrafica>();

        if (!TipiCliente.Tutti.Contains(normalizzati.Tipo))
        {
            errori.Add(ErroreAnagrafica.TipoClienteNonValido);
        }
        else if (normalizzati.Tipo == TipiCliente.Privato)
        {
            if (normalizzati.Nome is null || normalizzati.Cognome is null)
            {
                errori.Add(ErroreAnagrafica.NomeCognomeMancanti);
            }

            // Per un privato la denominazione non ha senso: si tiene solo nome e cognome.
            normalizzati = normalizzati with { Denominazione = null };
        }
        else
        {
            if (normalizzati.Denominazione is null)
            {
                errori.Add(ErroreAnagrafica.DenominazioneMancante);
            }

            normalizzati = normalizzati with { Nome = null, Cognome = null };
        }

        if (normalizzati.CodiceFiscale is not null && !CodiceFiscale.Valido(normalizzati.CodiceFiscale))
        {
            errori.Add(ErroreAnagrafica.CodiceFiscaleNonValido);
        }

        if (normalizzati.PartitaIva is not null && !PartitaIva.Valida(normalizzati.PartitaIva))
        {
            errori.Add(ErroreAnagrafica.PartitaIvaNonValida);
        }

        if (normalizzati.Pec is not null && !Email().IsMatch(normalizzati.Pec))
        {
            errori.Add(ErroreAnagrafica.PecNonValida);
        }

        return (normalizzati, errori);
    }

    public static (DatiSede Dati, IReadOnlyList<ErroreAnagrafica> Errori) Valida(DatiSede dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            Descrizione = Pulisci(dati.Descrizione),
            Indirizzo = Pulisci(dati.Indirizzo) ?? "",
            Civico = Pulisci(dati.Civico),
            Cap = Pulisci(dati.Cap) ?? "",
            Comune = Pulisci(dati.Comune) ?? "",
            Provincia = (Pulisci(dati.Provincia) ?? "").ToUpperInvariant(),
            NoteAccesso = Pulisci(dati.NoteAccesso),
        };

        var errori = new List<ErroreAnagrafica>();

        if (normalizzati.Indirizzo.Length == 0)
        {
            errori.Add(ErroreAnagrafica.IndirizzoMancante);
        }

        if (normalizzati.Comune.Length == 0)
        {
            errori.Add(ErroreAnagrafica.ComuneMancante);
        }

        if (!Cap().IsMatch(normalizzati.Cap))
        {
            errori.Add(ErroreAnagrafica.CapNonValido);
        }

        if (!Provincia().IsMatch(normalizzati.Provincia))
        {
            errori.Add(ErroreAnagrafica.ProvinciaNonValida);
        }

        var coordinateValide = (normalizzati.Latitudine, normalizzati.Longitudine) switch
        {
            (null, null) => true,
            ({ } lat, { } lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180,
            _ => false,
        };
        if (!coordinateValide)
        {
            errori.Add(ErroreAnagrafica.CoordinateNonValide);
        }

        return (normalizzati, errori);
    }

    public static (DatiContatto Dati, IReadOnlyList<ErroreAnagrafica> Errori) Valida(DatiContatto dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var tipo = (dati.Tipo ?? "").Trim().ToLowerInvariant();
        var valore = (dati.Valore ?? "").Trim();
        var normalizzati = dati with { Tipo = tipo, Valore = tipo == "email" ? valore.ToLowerInvariant() : valore, Etichetta = Pulisci(dati.Etichetta) };

        var errori = new List<ErroreAnagrafica>();
        if (!TipiContatto.Tutti.Contains(tipo))
        {
            errori.Add(ErroreAnagrafica.TipoContattoNonValido);
        }
        else if (!(tipo == "email" ? Email() : Telefono()).IsMatch(normalizzati.Valore))
        {
            errori.Add(ErroreAnagrafica.ValoreContattoNonValido);
        }

        return (normalizzati, errori);
    }

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();

    // Stesse regole dei vincoli CHECK del database.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex Email();

    [GeneratedRegex(@"^\+?[0-9][0-9 ./-]{3,19}$")]
    private static partial Regex Telefono();

    [GeneratedRegex(@"^[0-9]{5}$")]
    private static partial Regex Cap();

    [GeneratedRegex(@"^[A-Z]{2}$")]
    private static partial Regex Provincia();
}

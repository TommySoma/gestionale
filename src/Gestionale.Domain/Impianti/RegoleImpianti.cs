namespace Gestionale.Domain.Impianti;

/// <summary>Motivi per cui un dato di un impianto non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreImpianto
{
    SedeNonValida,
    TipoImpiantoNonValido,
    StatoImpiantoNonValido,
    DataDismissioneMancante,
    AnnoInstallazioneNonValido,
    PotenzaNonValida,
    CombustibileNonValido,
    ProprietarioNonValido,
    ResponsabileNonValido,
    PdrInUso,
    TipoGeneratoreNonValido,
    DateGeneratoreNonValide,
    RegioneNonValida,
    CodiceCatastoNonValido,
    CodiceCatastoInUso,
    RegioneGiaRegistrata,
}

/// <summary>Situazioni sospette che non bloccano il salvataggio: l'API le segnala.</summary>
public enum AvvisoImpianto
{
    /// <summary>Un altro impianto attivo dello stesso tipo è allo stesso indirizzo, come nei controlli del catasto.</summary>
    IndirizzoDuplicato,

    /// <summary>Un altro generatore ha la stessa matricola (marche diverse possono coincidere).</summary>
    MatricolaDuplicata,
}

public static class StatiImpianto
{
    public const string Attivo = "attivo";
    public const string Dismesso = "dismesso";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { Attivo, Dismesso };
}

/// <summary>Un sistema per regione: SIERT in Toscana, CURIT in Lombardia (vincolo anche nel database).</summary>
public static class CatastiRegionali
{
    public static IReadOnlyDictionary<string, string> SistemaPerRegione { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["toscana"] = "siert", ["lombardia"] = "curit" };
}

public sealed record DatiImpianto(
    Guid? SedeId,
    Guid? TipoImpiantoId,
    string? Stato,
    int? AnnoInstallazione,
    decimal? PotenzaUtileKw,
    Guid? CombustibileId,
    string? Pdr,
    Guid? ProprietarioClienteId,
    Guid? ResponsabileClienteId,
    string? Note,
    DateOnly? DismessoIl);

public sealed record DatiGeneratore(
    Guid? TipoGeneratoreId,
    string? Marca,
    string? Modello,
    string? Matricola,
    decimal? PotenzaKw,
    Guid? CombustibileId,
    DateOnly? InstallatoIl,
    DateOnly? DismessoIl,
    string? Note);

public sealed record DatiRiferimentoCatasto(string? Regione, string? Sistema, string? Codice, DateOnly? RegistratoIl);

/// <summary>
/// Normalizzazione e validazione dei dati degli impianti. Sono le stesse regole
/// dei vincoli del database, con errori comprensibili; le regole normative
/// (formato del PDR, del codice catasto) non sono verificate e non si inventano.
/// </summary>
public static class RegoleImpianti
{
    // numeric(9, 2): al massimo sette cifre intere.
    private const decimal PotenzaMassimaKw = 9_999_999.99m;

    public static (DatiImpianto Dati, IReadOnlyList<ErroreImpianto> Errori) Valida(DatiImpianto dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var stato = Pulisci(dati.Stato)?.ToLowerInvariant() ?? StatiImpianto.Attivo;
        var normalizzati = dati with
        {
            Stato = stato,
            // Il PDR si scrive spesso a gruppi di cifre: gli spazi non fanno parte del codice.
            Pdr = Pulisci(dati.Pdr) is { } pdr ? string.Concat(pdr.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant() : null,
            Note = Pulisci(dati.Note),
            // Un impianto attivo non ha data di dismissione.
            DismessoIl = stato == StatiImpianto.Attivo ? null : dati.DismessoIl,
        };

        var errori = new List<ErroreImpianto>();

        if (normalizzati.SedeId is null || normalizzati.SedeId == Guid.Empty)
        {
            errori.Add(ErroreImpianto.SedeNonValida);
        }

        if (normalizzati.TipoImpiantoId is null || normalizzati.TipoImpiantoId == Guid.Empty)
        {
            errori.Add(ErroreImpianto.TipoImpiantoNonValido);
        }

        if (!StatiImpianto.Tutti.Contains(stato))
        {
            errori.Add(ErroreImpianto.StatoImpiantoNonValido);
        }
        else if (stato == StatiImpianto.Dismesso && normalizzati.DismessoIl is null)
        {
            errori.Add(ErroreImpianto.DataDismissioneMancante);
        }

        if (normalizzati.AnnoInstallazione is { } anno && anno is < 1900 or > 2100)
        {
            errori.Add(ErroreImpianto.AnnoInstallazioneNonValido);
        }

        if (!PotenzaValida(normalizzati.PotenzaUtileKw))
        {
            errori.Add(ErroreImpianto.PotenzaNonValida);
        }

        return (normalizzati, errori);
    }

    public static (DatiGeneratore Dati, IReadOnlyList<ErroreImpianto> Errori) Valida(DatiGeneratore dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            Marca = Pulisci(dati.Marca),
            Modello = Pulisci(dati.Modello),
            Matricola = Pulisci(dati.Matricola),
            Note = Pulisci(dati.Note),
        };

        var errori = new List<ErroreImpianto>();

        if (normalizzati.TipoGeneratoreId is null || normalizzati.TipoGeneratoreId == Guid.Empty)
        {
            errori.Add(ErroreImpianto.TipoGeneratoreNonValido);
        }

        if (!PotenzaValida(normalizzati.PotenzaKw))
        {
            errori.Add(ErroreImpianto.PotenzaNonValida);
        }

        if (normalizzati is { InstallatoIl: { } installato, DismessoIl: { } dismesso } && dismesso < installato)
        {
            errori.Add(ErroreImpianto.DateGeneratoreNonValide);
        }

        return (normalizzati, errori);
    }

    public static (DatiRiferimentoCatasto Dati, IReadOnlyList<ErroreImpianto> Errori) Valida(DatiRiferimentoCatasto dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var regione = Pulisci(dati.Regione)?.ToLowerInvariant() ?? "";
        var codice = Pulisci(dati.Codice) ?? "";
        var normalizzati = dati with
        {
            Regione = regione,
            // Il sistema dipende solo dalla regione: non lo sceglie chi scrive.
            Sistema = CatastiRegionali.SistemaPerRegione.GetValueOrDefault(regione),
            Codice = codice,
        };

        var errori = new List<ErroreImpianto>();

        if (normalizzati.Sistema is null)
        {
            errori.Add(ErroreImpianto.RegioneNonValida);
        }

        if (codice.Length == 0 || codice.Any(char.IsWhiteSpace))
        {
            errori.Add(ErroreImpianto.CodiceCatastoNonValido);
        }

        return (normalizzati, errori);
    }

    private static bool PotenzaValida(decimal? potenza) => potenza is null or (> 0 and <= PotenzaMassimaKw);

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

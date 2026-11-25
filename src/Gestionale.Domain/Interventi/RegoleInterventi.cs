namespace Gestionale.Domain.Interventi;

/// <summary>Motivi per cui un dato di un intervento non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreIntervento
{
    ImpiantoInterventoNonValido,
    GeneratoreInterventoNonValido,
    TipoInterventoNonValido,
    FinestraIncompleta,
    FinestraNonValida,
    FinestraTroppoLunga,
    FinestraSenzaTecnico,
    TecnicoNonValido,
    EsitoInterventoNonValido,
    MotivoAnnullamentoMancante,
    TroppeRighe,
    ControlloSoloManutenzione,
    RigaTipoNonValido,
    RigaDescrizioneMancante,
    RigaUnitaMancante,
    RigaQuantitaNonValida,
    RigaPrezzoNonValido,
    RigaScontoNonValido,
    RigaIvaNonValida,
    ManodoperaNonInOre,
}

/// <summary>Stati interni, come nel vincolo ck_interventi_stato. Completato e annullato sono finali.</summary>
public static class StatiIntervento
{
    public const string DaPianificare = "da_pianificare";
    public const string Pianificato = "pianificato";
    public const string InCorso = "in_corso";
    public const string Completato = "completato";
    public const string Annullato = "annullato";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DaPianificare, Pianificato, InCorso, Completato, Annullato,
    };

    public static bool Finale(string stato) => stato is Completato or Annullato;
}

public static class TipiIntervento
{
    public const string Manutenzione = "manutenzione";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "manutenzione", "riparazione", "installazione", "sopralluogo", "altro",
    };
}

public static class EsitiIntervento
{
    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "risolto", "da_completare", "non_risolto",
    };
}

public static class TipiRiga
{
    public const string Materiale = "materiale";
    public const string Manodopera = "manodopera";

    /// <summary>La manodopera si conta in ore (vincolo ck_interventi_righe_manodopera).</summary>
    public const string UnitaOre = "h";
}

/// <summary>Dati di pianificazione: senza finestra l'intervento resta da pianificare.</summary>
public sealed record DatiIntervento(
    Guid? ImpiantoId,
    Guid? GeneratoreId,
    string? Tipo,
    string? Descrizione,
    DateTimeOffset? Inizio,
    DateTimeOffset? Fine,
    Guid? TecnicoUtenteId);

public sealed record DatiRiga(
    string? Tipo,
    string? Codice,
    string? Descrizione,
    string? Unita,
    decimal? Quantita,
    decimal? PrezzoUnitario,
    decimal? ScontoPercentuale,
    decimal? AliquotaIva);

/// <summary>ControlloEfficienza: in chiusura di una manutenzione è stato eseguito anche il controllo (decisione del 25/11/2026).</summary>
public sealed record DatiChiusura(string? Esito, string? Note, IReadOnlyList<DatiRiga>? Righe, bool ControlloEfficienza = false);

/// <summary>
/// Normalizzazione e validazione degli interventi: le stesse regole dei vincoli
/// del database, con errori comprensibili e tutti insieme.
/// </summary>
public static class RegoleInterventi
{
    public const int RigheMassime = 200;

    // Una finestra più lunga è quasi sempre un errore di battitura sulla data.
    public static readonly TimeSpan DurataMassima = TimeSpan.FromDays(31);

    // numeric(10, 3) e numeric(12, 2).
    private const decimal QuantitaMassima = 9_999_999.999m;
    private const decimal PrezzoMassimo = 9_999_999_999.99m;

    public static (DatiIntervento Dati, IReadOnlyList<ErroreIntervento> Errori) Valida(DatiIntervento dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            GeneratoreId = dati.GeneratoreId == Guid.Empty ? null : dati.GeneratoreId,
            Tipo = Pulisci(dati.Tipo)?.ToLowerInvariant(),
            Descrizione = Pulisci(dati.Descrizione),
            TecnicoUtenteId = dati.TecnicoUtenteId == Guid.Empty ? null : dati.TecnicoUtenteId,
        };

        var errori = new List<ErroreIntervento>();

        if (normalizzati.ImpiantoId is null || normalizzati.ImpiantoId == Guid.Empty)
        {
            errori.Add(ErroreIntervento.ImpiantoInterventoNonValido);
        }

        if (normalizzati.Tipo is null || !TipiIntervento.Tutti.Contains(normalizzati.Tipo))
        {
            errori.Add(ErroreIntervento.TipoInterventoNonValido);
        }

        switch (normalizzati)
        {
            case { Inizio: null, Fine: null }:
                break;
            case { Inizio: { } inizio, Fine: { } fine }:
                if (fine <= inizio)
                {
                    errori.Add(ErroreIntervento.FinestraNonValida);
                }
                else if (fine - inizio > DurataMassima)
                {
                    errori.Add(ErroreIntervento.FinestraTroppoLunga);
                }

                if (normalizzati.TecnicoUtenteId is null)
                {
                    errori.Add(ErroreIntervento.FinestraSenzaTecnico);
                }

                break;
            default:
                errori.Add(ErroreIntervento.FinestraIncompleta);
                break;
        }

        return (normalizzati, errori);
    }

    public static (DatiChiusura Dati, IReadOnlyList<ErroreIntervento> Errori) Valida(DatiChiusura dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var (righe, erroriRighe) = ValidaRighe(dati.Righe);
        var normalizzati = new DatiChiusura(Pulisci(dati.Esito)?.ToLowerInvariant(), Pulisci(dati.Note), righe, dati.ControlloEfficienza);

        var errori = new List<ErroreIntervento>();

        if (normalizzati.Esito is null || !EsitiIntervento.Tutti.Contains(normalizzati.Esito))
        {
            errori.Add(ErroreIntervento.EsitoInterventoNonValido);
        }

        errori.AddRange(erroriRighe);
        return (normalizzati, errori);
    }

    /// <summary>Righe di materiali e manodopera, le stesse per interventi e preventivi: un errore compare una volta sola anche se lo ripetono più righe.</summary>
    public static (IReadOnlyList<DatiRiga> Righe, IReadOnlyList<ErroreIntervento> Errori) ValidaRighe(IEnumerable<DatiRiga>? righe)
    {
        var normalizzate = (righe ?? []).Select(Normalizza).ToList();
        var errori = new List<ErroreIntervento>();

        if (normalizzate.Count > RigheMassime)
        {
            errori.Add(ErroreIntervento.TroppeRighe);
        }

        errori.AddRange(normalizzate.SelectMany(ErroriRiga).Distinct());
        return (normalizzate, errori);
    }

    public static string? MotivoAnnullamento(string? motivo) => Pulisci(motivo);

    private static DatiRiga Normalizza(DatiRiga riga)
    {
        var tipo = Pulisci(riga.Tipo)?.ToLowerInvariant();
        return riga with
        {
            Tipo = tipo,
            Codice = Pulisci(riga.Codice),
            Descrizione = Pulisci(riga.Descrizione),
            // Per la manodopera l'unità è sempre l'ora: se manca la si mette.
            Unita = Pulisci(riga.Unita)?.ToLowerInvariant() ?? (tipo == TipiRiga.Manodopera ? TipiRiga.UnitaOre : null),
            PrezzoUnitario = riga.PrezzoUnitario ?? 0,
            ScontoPercentuale = riga.ScontoPercentuale ?? 0,
        };
    }

    private static IEnumerable<ErroreIntervento> ErroriRiga(DatiRiga riga)
    {
        if (riga.Tipo is not (TipiRiga.Materiale or TipiRiga.Manodopera))
        {
            yield return ErroreIntervento.RigaTipoNonValido;
        }

        if (riga.Descrizione is null)
        {
            yield return ErroreIntervento.RigaDescrizioneMancante;
        }

        if (riga.Unita is null)
        {
            yield return ErroreIntervento.RigaUnitaMancante;
        }
        else if (riga.Tipo == TipiRiga.Manodopera && riga.Unita != TipiRiga.UnitaOre)
        {
            yield return ErroreIntervento.ManodoperaNonInOre;
        }

        if (riga.Quantita is not { } quantita || quantita <= 0 || quantita > QuantitaMassima || decimal.Round(quantita, 3) != quantita)
        {
            yield return ErroreIntervento.RigaQuantitaNonValida;
        }

        if (riga.PrezzoUnitario is < 0 or > PrezzoMassimo || (riga.PrezzoUnitario is { } prezzo && decimal.Round(prezzo, 2) != prezzo))
        {
            yield return ErroreIntervento.RigaPrezzoNonValido;
        }

        if (riga.ScontoPercentuale is < 0 or > 100)
        {
            yield return ErroreIntervento.RigaScontoNonValido;
        }

        if (riga.AliquotaIva is < 0 or > 100)
        {
            yield return ErroreIntervento.RigaIvaNonValida;
        }
    }

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

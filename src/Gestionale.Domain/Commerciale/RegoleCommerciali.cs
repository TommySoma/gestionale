using Gestionale.Domain.Interventi;

namespace Gestionale.Domain.Commerciale;

/// <summary>Motivi per cui un dato del listino o di un preventivo non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreCommerciale
{
    CodiceVoceNonValido,
    DescrizioneVoceMancante,
    TipoVoceNonValido,
    UnitaVoceMancante,
    ManodoperaVoceNonInOre,
    IvaVoceNonValida,
    PrezzoListinoNonValido,
    CodiceVoceInUso,
    PrezzoGiaPresente,
    ClientePreventivoNonValido,
    SedePreventivoNonValida,
    ImpiantoPreventivoNonValido,
    OggettoPreventivoMancante,
    ValiditaPreventivoNonValida,
    VoceRigaNonValida,
    PreventivoSenzaRighe,
    MotivoAnnullamentoPreventivoMancante,
    ImpiantoMancantePerIntervento,
    TipoInterventoPreventivoNonValido,
}

/// <summary>Stati dei preventivi (vincolo CHECK nel database, decisione del 25/11/2026). "Scaduto" non è uno stato: è un emesso oltre la validità.</summary>
public static class StatiPreventivo
{
    public const string Bozza = "bozza";
    public const string Emesso = "emesso";
    public const string Accettato = "accettato";
    public const string Rifiutato = "rifiutato";
    public const string Annullato = "annullato";

    /// <summary>Filtro di ricerca, non uno stato salvato.</summary>
    public const string Scaduto = "scaduto";
}

public sealed record DatiVoce(string? Codice, string? Descrizione, string? Tipo, string? Unita, decimal? AliquotaIva, bool? Attiva, string? Note);

/// <summary>Senza ValidoDal il prezzo vale da oggi.</summary>
public sealed record DatiPrezzo(decimal? Prezzo, DateOnly? ValidoDal);

public sealed record DatiPreventivo(Guid? ClienteId, Guid? SedeId, Guid? ImpiantoId, string? Oggetto, string? Note, int? ValiditaGiorni);

/// <summary>Riga di preventivo: le stesse regole delle righe degli interventi, più la voce del listino da cui viene.</summary>
public sealed record RigaPreventivo(Guid? VoceId, DatiRiga Riga);

/// <summary>
/// Normalizzazione e validazione del listino e dei preventivi: le stesse regole
/// dei vincoli del database, con errori comprensibili e tutti insieme. Gli
/// errori delle righe sono quelli degli interventi, con gli stessi testi.
/// </summary>
public static class RegoleCommerciali
{
    public const int ValiditaPredefinita = 30;

    // numeric(12, 2): al massimo dieci cifre intere.
    private const decimal PrezzoMassimo = 9_999_999_999.99m;

    public static (DatiVoce Dati, IReadOnlyList<Enum> Errori) Valida(DatiVoce dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var tipo = Pulisci(dati.Tipo)?.ToLowerInvariant();
        var normalizzati = dati with
        {
            Codice = Pulisci(dati.Codice)?.ToUpperInvariant(),
            Descrizione = Pulisci(dati.Descrizione),
            Tipo = tipo,
            Unita = Pulisci(dati.Unita)?.ToLowerInvariant() ?? (tipo == TipiRiga.Manodopera ? TipiRiga.UnitaOre : null),
            Attiva = dati.Attiva ?? true,
            Note = Pulisci(dati.Note),
        };

        var errori = new List<Enum>();

        if (normalizzati.Codice is null || normalizzati.Codice.Any(char.IsWhiteSpace))
        {
            errori.Add(ErroreCommerciale.CodiceVoceNonValido);
        }

        if (normalizzati.Descrizione is null)
        {
            errori.Add(ErroreCommerciale.DescrizioneVoceMancante);
        }

        if (tipo is not (TipiRiga.Materiale or TipiRiga.Manodopera))
        {
            errori.Add(ErroreCommerciale.TipoVoceNonValido);
        }

        if (normalizzati.Unita is null)
        {
            errori.Add(ErroreCommerciale.UnitaVoceMancante);
        }
        else if (tipo == TipiRiga.Manodopera && normalizzati.Unita != TipiRiga.UnitaOre)
        {
            errori.Add(ErroreCommerciale.ManodoperaVoceNonInOre);
        }

        if (normalizzati.AliquotaIva is < 0 or > 100)
        {
            errori.Add(ErroreCommerciale.IvaVoceNonValida);
        }

        return (normalizzati, errori);
    }

    public static IReadOnlyList<Enum> Valida(DatiPrezzo dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var errori = new List<Enum>();
        if (dati.Prezzo is not { } prezzo || prezzo < 0 || prezzo > PrezzoMassimo || decimal.Round(prezzo, 2) != prezzo)
        {
            errori.Add(ErroreCommerciale.PrezzoListinoNonValido);
        }

        return errori;
    }

    public static (DatiPreventivo Dati, IReadOnlyList<RigaPreventivo> Righe, IReadOnlyList<Enum> Errori) Valida(
        DatiPreventivo dati, IReadOnlyList<RigaPreventivo>? righe)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            SedeId = dati.SedeId == Guid.Empty ? null : dati.SedeId,
            ImpiantoId = dati.ImpiantoId == Guid.Empty ? null : dati.ImpiantoId,
            Oggetto = Pulisci(dati.Oggetto),
            Note = Pulisci(dati.Note),
            ValiditaGiorni = dati.ValiditaGiorni ?? ValiditaPredefinita,
        };

        var errori = new List<Enum>();

        if (normalizzati.ClienteId is null || normalizzati.ClienteId == Guid.Empty)
        {
            errori.Add(ErroreCommerciale.ClientePreventivoNonValido);
        }

        if (normalizzati.Oggetto is null)
        {
            errori.Add(ErroreCommerciale.OggettoPreventivoMancante);
        }

        if (normalizzati.ValiditaGiorni is < 1 or > 365)
        {
            errori.Add(ErroreCommerciale.ValiditaPreventivoNonValida);
        }

        var elenco = righe ?? [];
        var (validate, erroriRighe) = RegoleInterventi.ValidaRighe(elenco.Select(r => r.Riga));
        errori.AddRange(erroriRighe.Cast<Enum>());

        var normalizzate = elenco
            .Zip(validate, (r, v) => new RigaPreventivo(r.VoceId == Guid.Empty ? null : r.VoceId, v))
            .ToList();

        return (normalizzati, normalizzate, errori);
    }

    public static string? MotivoAnnullamento(string? motivo) => Pulisci(motivo);

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

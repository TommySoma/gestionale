namespace Gestionale.Domain.Fgas;

/// <summary>Motivi per cui un dato F-Gas non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreFgas
{
    GasNonValido,
    CaricaNonValida,
    CaricaInstallazioneNonValida,
    TipoOperazioneNonValido,
    DataOperazioneMancante,
    DataOperazioneFutura,
    EsitoControlloMancante,
    EsitoControlloNonPrevisto,
    QuantitaGasNonValida,
    ImpiantoRiciclaggioMancante,
    MisureSmaltimentoMancanti,
    MotivoAnnullamentoFgasMancante,
    DataComunicazioneNonValida,
    TipoCertificatoNonValido,
    NumeroCertificatoMancante,
    DateCertificatoNonValide,
    TecnicoSenzaCertificato,
    InterventoNonDellImpianto,
    ApparecchiaturaConOperazioni,
}

public static class TipiOperazioneFgas
{
    public const string Installazione = "installazione";
    public const string ControlloPerdite = "controllo_perdite";
    public const string Manutenzione = "manutenzione";
    public const string Riparazione = "riparazione";
    public const string Recupero = "recupero";
    public const string Smantellamento = "smantellamento";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Installazione, ControlloPerdite, Manutenzione, Riparazione, Recupero, Smantellamento,
    };
}

public static class TipiCertificato
{
    public const string Persona = "fgas_persona";
    public const string Impresa = "fgas_impresa";
}

/// <summary>Gas e carica di un generatore, con i dati per la Banca dati FGAS.</summary>
public sealed record DatiApparecchiatura(
    Guid? GasId,
    decimal? CaricaKg,
    decimal? CaricaInstallazioneKg,
    bool ErmeticaEtichettata,
    bool EdificioResidenziale,
    bool SistemaRilevamento,
    string? CodiceUnivoco,
    string? FatturaNumero,
    DateOnly? FatturaData);

/// <summary>Un'operazione del registro (Reg. UE 2024/573, art. 7).</summary>
public sealed record DatiOperazione(
    Guid? ApparecchiaturaId,
    Guid? InterventoId,
    string? Tipo,
    DateOnly? EseguitoIl,
    bool? PerditaRilevata,
    decimal? GasAggiuntoKg,
    decimal? GasRecuperatoKg,
    bool GasRiciclato,
    string? ImpiantoRiciclaggio,
    string? CertificatoRiciclaggio,
    string? MisureSmaltimento,
    Guid? TecnicoUtenteId,
    string? Note);

/// <summary>Certificato F-Gas: utente null per quello dell'impresa.</summary>
public sealed record DatiCertificato(Guid? UtenteId, string? Numero, string? Ente, DateOnly? RilasciataIl, DateOnly? ScadeIl);

/// <summary>
/// Regole di forma del registro F-Gas (ADR 0008). Soglie, frequenze ed esenzioni
/// del regolamento le applica il database, dove sono dati con la loro fonte.
/// </summary>
public static class RegoleFgas
{
    // numeric(10,3): al massimo sette cifre intere.
    public const decimal QuantitaMassimaKg = 9_999_999.999m;

    public static (DatiApparecchiatura Dati, IReadOnlyList<ErroreFgas> Errori) Valida(DatiApparecchiatura dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            CodiceUnivoco = Pulisci(dati.CodiceUnivoco),
            FatturaNumero = Pulisci(dati.FatturaNumero),
        };

        var errori = new List<ErroreFgas>();
        if (normalizzati.GasId is null || normalizzati.GasId == Guid.Empty)
        {
            errori.Add(ErroreFgas.GasNonValido);
        }

        if (normalizzati.CaricaKg is not (> 0 and <= QuantitaMassimaKg))
        {
            errori.Add(ErroreFgas.CaricaNonValida);
        }

        if (normalizzati.CaricaInstallazioneKg is < 0 or > QuantitaMassimaKg)
        {
            errori.Add(ErroreFgas.CaricaInstallazioneNonValida);
        }

        return (normalizzati, errori);
    }

    public static (DatiOperazione Dati, IReadOnlyList<ErroreFgas> Errori) Valida(DatiOperazione dati, DateOnly oggi)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var tipo = Pulisci(dati.Tipo)?.ToLowerInvariant();
        var normalizzati = dati with
        {
            Tipo = tipo,
            GasAggiuntoKg = dati.GasAggiuntoKg ?? 0,
            GasRecuperatoKg = dati.GasRecuperatoKg ?? 0,
            ImpiantoRiciclaggio = Pulisci(dati.ImpiantoRiciclaggio),
            CertificatoRiciclaggio = Pulisci(dati.CertificatoRiciclaggio),
            MisureSmaltimento = Pulisci(dati.MisureSmaltimento),
            Note = Pulisci(dati.Note),
            // L'esito vale solo per un controllo delle perdite.
            PerditaRilevata = tipo == TipiOperazioneFgas.ControlloPerdite ? dati.PerditaRilevata : null,
        };

        var errori = new List<ErroreFgas>();
        if (tipo is null || !TipiOperazioneFgas.Tutti.Contains(tipo))
        {
            errori.Add(ErroreFgas.TipoOperazioneNonValido);
        }

        if (normalizzati.EseguitoIl is not { } data)
        {
            errori.Add(ErroreFgas.DataOperazioneMancante);
        }
        else if (data > oggi)
        {
            errori.Add(ErroreFgas.DataOperazioneFutura);
        }

        if (tipo == TipiOperazioneFgas.ControlloPerdite && normalizzati.PerditaRilevata is null)
        {
            errori.Add(ErroreFgas.EsitoControlloMancante);
        }

        if (normalizzati.GasAggiuntoKg is < 0 or > QuantitaMassimaKg || normalizzati.GasRecuperatoKg is < 0 or > QuantitaMassimaKg)
        {
            errori.Add(ErroreFgas.QuantitaGasNonValida);
        }

        if (normalizzati.GasRiciclato && (normalizzati.ImpiantoRiciclaggio is null || normalizzati.GasAggiuntoKg is not > 0))
        {
            errori.Add(ErroreFgas.ImpiantoRiciclaggioMancante);
        }

        if (tipo == TipiOperazioneFgas.Smantellamento && normalizzati.MisureSmaltimento is null)
        {
            errori.Add(ErroreFgas.MisureSmaltimentoMancanti);
        }

        return (normalizzati, errori);
    }

    public static (DatiCertificato Dati, IReadOnlyList<ErroreFgas> Errori) Valida(DatiCertificato dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with { Numero = Pulisci(dati.Numero), Ente = Pulisci(dati.Ente) };
        var errori = new List<ErroreFgas>();
        if (normalizzati.Numero is null)
        {
            errori.Add(ErroreFgas.NumeroCertificatoMancante);
        }

        if (normalizzati is { RilasciataIl: { } rilascio, ScadeIl: { } scadenza } && scadenza < rilascio)
        {
            errori.Add(ErroreFgas.DateCertificatoNonValide);
        }

        return (normalizzati, errori);
    }

    private static string? Pulisci(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

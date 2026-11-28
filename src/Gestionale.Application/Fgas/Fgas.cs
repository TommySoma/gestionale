using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Fgas;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Fgas;

public sealed class GasFluorurato
{
    public Guid GasId { get; init; }

    public string Codice { get; init; } = "";

    public string Designazione { get; init; } = "";

    public string Allegato { get; init; } = "";

    public decimal Gwp { get; init; }

    public bool Miscela { get; init; }

    public bool DaVerificare { get; init; }
}

/// <summary>Apparecchiatura F-Gas di un generatore, con obbligo e frequenza del controllo calcolati dal database.</summary>
public sealed class ApparecchiaturaFgas
{
    public Guid ApparecchiaturaFgasId { get; init; }

    public Guid GeneratoreId { get; init; }

    public string Generatore { get; init; } = "";

    public Guid GasId { get; init; }

    public string Gas { get; init; } = "";

    public string Allegato { get; init; } = "";

    public decimal Gwp { get; init; }

    public bool GasDaVerificare { get; init; }

    public decimal CaricaKg { get; init; }

    public decimal? CaricaInstallazioneKg { get; init; }

    /// <summary>Tonnellate di CO2 equivalente (allegato I); per l'allegato II le soglie sono in kg.</summary>
    public decimal Co2eTonnellate { get; init; }

    /// <summary>Mesi tra due controlli delle perdite; null se non è soggetta.</summary>
    public int? FrequenzaMesi { get; init; }

    public bool ErmeticaEtichettata { get; init; }

    public bool EdificioResidenziale { get; init; }

    public bool SistemaRilevamento { get; init; }

    public string? CodiceUnivoco { get; init; }

    public string? FatturaNumero { get; init; }

    public DateOnly? FatturaData { get; init; }

    public int Versione { get; init; }

    public IReadOnlyList<OperazioneFgas> Operazioni { get; set; } = [];
}

public sealed class OperazioneFgas
{
    public Guid InterventoFgasId { get; init; }

    public Guid ApparecchiaturaFgasId { get; init; }

    public Guid InterventoId { get; init; }

    public string Tipo { get; init; } = "";

    public DateOnly EseguitoIl { get; init; }

    public bool? PerditaRilevata { get; init; }

    public decimal GasAggiuntoKg { get; init; }

    public decimal GasRecuperatoKg { get; init; }

    public bool GasRiciclato { get; init; }

    public string? ImpiantoRiciclaggio { get; init; }

    public string? CertificatoRiciclaggio { get; init; }

    public string? MisureSmaltimento { get; init; }

    public Guid TecnicoUtenteId { get; init; }

    public string Tecnico { get; init; } = "";

    public string CertificatoTecnico { get; init; } = "";

    public string? CertificatoImpresa { get; init; }

    public string? Note { get; init; }

    public DateOnly TermineComunicazione { get; init; }

    public DateOnly? ComunicatoIl { get; init; }

    public DateTime? AnnullatoIl { get; init; }

    public string? MotivoAnnullamento { get; init; }

    public int Versione { get; init; }
}

/// <summary>Un'operazione con i dati che la Banca dati FGAS chiede (DPR 146/2018, art. 16).</summary>
public sealed class ComunicazioneFgas
{
    public Guid InterventoFgasId { get; init; }

    public int Versione { get; init; }

    public string Tipo { get; init; } = "";

    public DateOnly EseguitoIl { get; init; }

    public DateOnly TermineComunicazione { get; init; }

    public DateOnly? ComunicatoIl { get; init; }

    public Guid ImpiantoId { get; init; }

    public string Operatore { get; init; } = "";

    public string? CodiceFiscale { get; init; }

    public string? PartitaIva { get; init; }

    public string Luogo { get; init; } = "";

    public string Tipologia { get; init; } = "";

    public string? CodiceUnivoco { get; init; }

    public string? Matricola { get; init; }

    public DateOnly? InstallatoIl { get; init; }

    public string? FatturaNumero { get; init; }

    public DateOnly? FatturaData { get; init; }

    public string Gas { get; init; } = "";

    public decimal CaricaKg { get; init; }

    public decimal GasAggiuntoKg { get; init; }

    public decimal GasRecuperatoKg { get; init; }

    public string? ImpiantoRiciclaggio { get; init; }

    public string? CertificatoRiciclaggio { get; init; }

    public string? MisureSmaltimento { get; init; }

    public string CertificatoTecnico { get; init; } = "";

    public string? CertificatoImpresa { get; init; }

    public string Tecnico { get; init; } = "";

    public string? Note { get; init; }
}

public sealed class Certificazione
{
    public Guid CertificazioneId { get; init; }

    public Guid? UtenteId { get; init; }

    public string? Utente { get; init; }

    public string Tipo { get; init; } = "";

    public string Numero { get; init; } = "";

    public string? Ente { get; init; }

    public DateOnly? RilasciataIl { get; init; }

    public DateOnly? ScadeIl { get; init; }

    public int Versione { get; init; }
}

public abstract record EsitoFgas
{
    public sealed record Riuscita(Guid Id, int Versione) : EsitoFgas;

    public sealed record NonValida(IReadOnlyList<ErroreFgas> Errori) : EsitoFgas;

    public sealed record NonTrovata : EsitoFgas;

    public sealed record VersioneSuperata : EsitoFgas;

    /// <summary>L'operazione è già comunicata alla Banca dati o annullata.</summary>
    public sealed record StatoNonConsentito : EsitoFgas;
}

public interface IRepositoryFgas
{
    Task<IReadOnlyList<GasFluorurato>> GasAsync(Contesto contesto, CancellationToken ct);

    /// <summary>Null se l'impianto non esiste o non è visibile (con soloTecnico: senza suoi interventi).</summary>
    Task<IReadOnlyList<ApparecchiaturaFgas>?> ApparecchiatureAsync(Contesto contesto, Guid impiantoId, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoFgas> SalvaApparecchiaturaAsync(
        Contesto contesto, Guid impiantoId, Guid generatoreId, int? versione, DatiApparecchiatura dati, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoFgas> EliminaApparecchiaturaAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, CancellationToken ct);

    Task<EsitoFgas> RegistraOperazioneAsync(Contesto contesto, DatiOperazione dati, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoFgas> AnnullaOperazioneAsync(Contesto contesto, Guid operazioneId, int versione, string motivo, CancellationToken ct);

    Task<EsitoFgas> SegnaComunicataAsync(Contesto contesto, Guid operazioneId, int versione, DateOnly comunicatoIl, CancellationToken ct);

    Task<IReadOnlyList<ComunicazioneFgas>> ComunicazioniAsync(Contesto contesto, bool soloDaComunicare, CancellationToken ct);

    Task<IReadOnlyList<Certificazione>> CertificazioniAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoFgas> SalvaCertificazioneAsync(Contesto contesto, Guid? certificazioneId, int? versione, DatiCertificato dati, CancellationToken ct);

    Task<EsitoFgas> EliminaCertificazioneAsync(Contesto contesto, Guid certificazioneId, int versione, CancellationToken ct);
}

/// <summary>
/// Registro F-Gas (ADR 0008): apparecchiature dei generatori, operazioni del registro,
/// comunicazioni assistite alla Banca dati FGAS e certificati. Con l'ambito "propri"
/// il Tecnico lavora sugli impianti e sugli interventi suoi.
/// </summary>
public sealed class ServizioFgas(IRepositoryFgas repository, TimeProvider orologio)
{
    public const int LunghezzaMassimaMotivo = 500;

    private static readonly TimeZoneInfo s_fuso = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    public Task<IReadOnlyList<GasFluorurato>> GasAsync(Contesto contesto, CancellationToken ct) => repository.GasAsync(contesto, ct);

    public Task<IReadOnlyList<ApparecchiaturaFgas>?> ApparecchiatureAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, CancellationToken ct) =>
        repository.ApparecchiatureAsync(contesto, impiantoId, SoloTecnico(contesto, ambito), ct);

    public async Task<EsitoFgas> SalvaApparecchiaturaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid generatoreId, int? versione, DatiApparecchiatura dati, CancellationToken ct)
    {
        var (normalizzati, errori) = RegoleFgas.Valida(dati);
        return errori.Count > 0
            ? new EsitoFgas.NonValida(errori)
            : await repository.SalvaApparecchiaturaAsync(contesto, impiantoId, generatoreId, versione, normalizzati, SoloTecnico(contesto, ambito), ct);
    }

    public Task<EsitoFgas> EliminaApparecchiaturaAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, CancellationToken ct) =>
        repository.EliminaApparecchiaturaAsync(contesto, impiantoId, generatoreId, versione, ct);

    /// <summary>Il Tecnico registra solo a proprio nome; l'ufficio sceglie il tecnico, o è chi registra.</summary>
    public async Task<EsitoFgas> RegistraOperazioneAsync(Contesto contesto, AmbitoPermesso ambito, DatiOperazione dati, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(dati);

        var tecnico = ambito == AmbitoPermesso.Propri ? contesto.UtenteId : dati.TecnicoUtenteId ?? contesto.UtenteId;
        var (normalizzati, errori) = RegoleFgas.Valida(dati with { TecnicoUtenteId = tecnico }, Oggi());
        return errori.Count > 0
            ? new EsitoFgas.NonValida(errori)
            : await repository.RegistraOperazioneAsync(contesto, normalizzati, SoloTecnico(contesto, ambito), ct);
    }

    public async Task<EsitoFgas> AnnullaOperazioneAsync(Contesto contesto, Guid operazioneId, int versione, string? motivo, CancellationToken ct)
    {
        var testo = motivo?.Trim();
        return string.IsNullOrEmpty(testo) || testo.Length > LunghezzaMassimaMotivo
            ? new EsitoFgas.NonValida([ErroreFgas.MotivoAnnullamentoFgasMancante])
            : await repository.AnnullaOperazioneAsync(contesto, operazioneId, versione, testo, ct);
    }

    /// <summary>Chi ha inserito l'operazione sul portale la segna: la data non è futura.</summary>
    public async Task<EsitoFgas> SegnaComunicataAsync(Contesto contesto, Guid operazioneId, int versione, DateOnly? comunicatoIl, CancellationToken ct)
    {
        var data = comunicatoIl ?? Oggi();
        return data > Oggi()
            ? new EsitoFgas.NonValida([ErroreFgas.DataComunicazioneNonValida])
            : await repository.SegnaComunicataAsync(contesto, operazioneId, versione, data, ct);
    }

    public Task<IReadOnlyList<ComunicazioneFgas>> ComunicazioniAsync(Contesto contesto, bool soloDaComunicare, CancellationToken ct) =>
        repository.ComunicazioniAsync(contesto, soloDaComunicare, ct);

    public Task<IReadOnlyList<Certificazione>> CertificazioniAsync(Contesto contesto, CancellationToken ct) => repository.CertificazioniAsync(contesto, ct);

    public async Task<EsitoFgas> SalvaCertificazioneAsync(Contesto contesto, Guid? certificazioneId, int? versione, DatiCertificato dati, CancellationToken ct)
    {
        var (normalizzati, errori) = RegoleFgas.Valida(dati);
        return errori.Count > 0
            ? new EsitoFgas.NonValida(errori)
            : await repository.SalvaCertificazioneAsync(contesto, certificazioneId, versione, normalizzati, ct);
    }

    public Task<EsitoFgas> EliminaCertificazioneAsync(Contesto contesto, Guid certificazioneId, int versione, CancellationToken ct) =>
        repository.EliminaCertificazioneAsync(contesto, certificazioneId, versione, ct);

    private DateOnly Oggi() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(orologio.GetUtcNow(), s_fuso).DateTime);

    private static Guid? SoloTecnico(Contesto contesto, AmbitoPermesso ambito) => ambito == AmbitoPermesso.Propri ? contesto.UtenteId : null;
}

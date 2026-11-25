using Gestionale.Application.Interventi;

namespace Gestionale.Application.Commerciale;

/// <summary>Voce del listino con il prezzo in vigore oggi (null se non ne ha ancora uno).</summary>
public sealed class VoceListino
{
    public Guid VoceId { get; init; }

    public string Codice { get; init; } = "";

    public string Descrizione { get; init; } = "";

    public string Tipo { get; init; } = "";

    public string Unita { get; init; } = "";

    public decimal? AliquotaIva { get; init; }

    public bool Attiva { get; init; }

    public string? Note { get; init; }

    public decimal? Prezzo { get; init; }

    public DateOnly? PrezzoDal { get; init; }

    public int Versione { get; init; }

    /// <summary>Storico dei prezzi, dal più recente; solo nella scheda della voce.</summary>
    public IReadOnlyList<PrezzoListino> Prezzi { get; set; } = [];
}

public sealed class PrezzoListino
{
    public Guid PrezzoId { get; init; }

    public decimal Prezzo { get; init; }

    public DateOnly ValidoDal { get; init; }

    public DateTime CreatoIl { get; init; }
}

/// <summary>Riga dell'elenco dei preventivi. Scaduto: emesso e oltre la validità.</summary>
public sealed class RiepilogoPreventivo
{
    public Guid PreventivoId { get; init; }

    public int? Anno { get; init; }

    public int? Numero { get; init; }

    public string Stato { get; init; } = "";

    public bool Scaduto { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string Oggetto { get; init; } = "";

    public DateOnly? EmessoIl { get; init; }

    public DateOnly? ScadeIl { get; init; }

    public decimal Totale { get; init; }

    public Guid? InterventoId { get; init; }

    public DateTime CreatoIl { get; init; }

    public int Versione { get; init; }
}

public sealed class Preventivo
{
    public Guid PreventivoId { get; init; }

    public int? Anno { get; init; }

    public int? Numero { get; init; }

    public string Stato { get; init; } = "";

    public bool Scaduto { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string? CodiceFiscale { get; init; }

    public string? PartitaIva { get; init; }

    public Guid? SedeId { get; init; }

    public string? Indirizzo { get; init; }

    public string? Civico { get; init; }

    public string? Cap { get; init; }

    public string? Comune { get; init; }

    public string? Provincia { get; init; }

    public Guid? ImpiantoId { get; init; }

    public string? TipoImpianto { get; init; }

    public string Oggetto { get; init; } = "";

    public string? Note { get; init; }

    public int ValiditaGiorni { get; init; }

    public DateOnly? EmessoIl { get; init; }

    public DateOnly? ScadeIl { get; init; }

    public decimal Imponibile { get; init; }

    public decimal Iva { get; init; }

    public decimal Totale { get; init; }

    public DateTime? AccettatoIl { get; init; }

    public DateTime? RifiutatoIl { get; init; }

    public DateTime? AnnullatoIl { get; init; }

    public string? MotivoAnnullamento { get; init; }

    public Guid? InterventoId { get; init; }

    public DateTime CreatoIl { get; init; }

    public int Versione { get; init; }

    public IReadOnlyList<RigaPreventivoLetta> Righe { get; set; } = [];
}

public sealed class RigaPreventivoLetta
{
    public int Posizione { get; init; }

    public Guid? VoceId { get; init; }

    public string Tipo { get; init; } = "";

    public string? Codice { get; init; }

    public string Descrizione { get; init; } = "";

    public string Unita { get; init; } = "";

    public decimal Quantita { get; init; }

    public decimal PrezzoUnitario { get; init; }

    public decimal ScontoPercentuale { get; init; }

    public decimal? AliquotaIva { get; init; }

    public decimal Importo { get; init; }
}

/// <summary>Stato accetta anche "scaduto": gli emessi oltre la validità.</summary>
public sealed record FiltroPreventivi(string? Testo = null, string? Stato = null, Guid? ClienteId = null, Guid? ImpiantoId = null);

/// <summary>Esito di una scrittura su listino o preventivi. InterventoId c'è dopo la conversione.</summary>
public abstract record EsitoCommerciale
{
    public sealed record Riuscita(Guid Id, int Versione, int? Anno = null, int? Numero = null, Guid? InterventoId = null) : EsitoCommerciale;

    public sealed record NonValida(IReadOnlyList<Enum> Errori) : EsitoCommerciale;

    public sealed record NonTrovata : EsitoCommerciale;

    public sealed record VersioneSuperata : EsitoCommerciale;

    /// <summary>Lo stato attuale non permette l'operazione (per esempio modificare un preventivo emesso).</summary>
    public sealed record StatoNonConsentito(string Stato) : EsitoCommerciale;

    /// <summary>Un codice o una data già usati.</summary>
    public sealed record Duplicata(Enum Motivo) : EsitoCommerciale;
}

/// <summary>Produce il PDF di un preventivo (ADR 0005), anche in bozza, con la dicitura che lo distingue.</summary>
public interface IGeneratorePreventivo
{
    byte[] Genera(IntestazioneAzienda azienda, Preventivo preventivo);
}

public abstract record EsitoPdfPreventivo
{
    public sealed record Pronto(byte[] Pdf, string NomeFile) : EsitoPdfPreventivo;

    public sealed record NonTrovato : EsitoPdfPreventivo;
}

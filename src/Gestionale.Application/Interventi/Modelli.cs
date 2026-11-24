using Gestionale.Domain.Interventi;

namespace Gestionale.Application.Interventi;

/// <summary>Riga di elenco e di calendario. Inizio e fine sono in UTC.</summary>
public sealed class RiepilogoIntervento
{
    public Guid InterventoId { get; init; }

    public int Anno { get; init; }

    public int Numero { get; init; }

    public string Tipo { get; init; } = "";

    public string Stato { get; init; } = "";

    public string? Esito { get; init; }

    public DateTime? Inizio { get; init; }

    public DateTime? Fine { get; init; }

    public Guid? TecnicoUtenteId { get; init; }

    public string? Tecnico { get; init; }

    public Guid ImpiantoId { get; init; }

    public string TipoImpianto { get; init; } = "";

    public string Cliente { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string? Civico { get; init; }

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public string? Descrizione { get; init; }

    public int Versione { get; init; }
}

public sealed class Intervento
{
    public Guid InterventoId { get; init; }

    public int Anno { get; init; }

    public int Numero { get; init; }

    public string Tipo { get; init; } = "";

    public string Stato { get; init; } = "";

    public string? Esito { get; init; }

    public DateTime? Inizio { get; init; }

    public DateTime? Fine { get; init; }

    public Guid? TecnicoUtenteId { get; init; }

    public string? Tecnico { get; init; }

    public Guid ImpiantoId { get; init; }

    public string TipoImpianto { get; init; } = "";

    public Guid? GeneratoreId { get; init; }

    public string? Generatore { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string? Civico { get; init; }

    public string Cap { get; init; } = "";

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public string? Descrizione { get; init; }

    public string? NoteChiusura { get; init; }

    public DateTime? ChiusoIl { get; init; }

    public DateTime? AnnullatoIl { get; init; }

    public string? MotivoAnnullamento { get; init; }

    public DateTime CreatoIl { get; init; }

    public int Versione { get; init; }

    public IReadOnlyList<RigaIntervento> Righe { get; set; } = [];

    public decimal Totale => Righe.Sum(r => r.Importo);
}

public sealed class RigaIntervento
{
    public int Posizione { get; init; }

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

/// <summary>Persona a cui si può assegnare un intervento: utente attivo con il permesso di scrittura sugli interventi.</summary>
public sealed class TecnicoAssegnabile
{
    public Guid UtenteId { get; init; }

    public string Nome { get; init; } = "";
}

/// <summary>Da e A delimitano il calendario: compaiono gli interventi la cui finestra lo tocca.</summary>
public sealed record FiltroInterventi(
    DateTimeOffset? Da = null,
    DateTimeOffset? A = null,
    Guid? TecnicoUtenteId = null,
    Guid? ImpiantoId = null,
    string? Stato = null);

/// <summary>Esito di una scrittura su un intervento.</summary>
public abstract record EsitoIntervento
{
    public sealed record Riuscita(Guid Id, int Versione, int Anno, int Numero) : EsitoIntervento;

    public sealed record NonValida(IReadOnlyList<ErroreIntervento> Errori) : EsitoIntervento;

    public sealed record NonTrovata : EsitoIntervento;

    public sealed record VersioneSuperata : EsitoIntervento;

    /// <summary>Lo stato attuale non permette l'operazione (per esempio chiudere un intervento annullato).</summary>
    public sealed record StatoNonConsentito(string Stato) : EsitoIntervento;

    /// <summary>Il tecnico ha già un intervento che si sovrappone alla finestra.</summary>
    public sealed record TecnicoOccupato : EsitoIntervento;

    /// <summary>Il permesso c'è, ma solo sui propri interventi: pianificare e annullare spetta all'ufficio.</summary>
    public sealed record Vietata : EsitoIntervento;
}

/// <summary>Dati dell'impresa per l'intestazione dei documenti.</summary>
public sealed class IntestazioneAzienda
{
    public string RagioneSociale { get; init; } = "";

    public string? PartitaIva { get; init; }

    public string? Indirizzo { get; init; }

    public string? Cap { get; init; }

    public string? Comune { get; init; }

    public string? Provincia { get; init; }
}

/// <summary>Produce il PDF del rapportino di un intervento chiuso (ADR 0005).</summary>
public interface IGeneratoreRapportino
{
    byte[] Genera(IntestazioneAzienda azienda, Intervento intervento);
}

/// <summary>Esito della richiesta del rapportino.</summary>
public abstract record EsitoRapportino
{
    public sealed record Pronto(byte[] Pdf, string NomeFile) : EsitoRapportino;

    public sealed record NonTrovato : EsitoRapportino;

    /// <summary>Il rapportino si fa solo per un intervento completato.</summary>
    public sealed record NonChiuso(string Stato) : EsitoRapportino;
}

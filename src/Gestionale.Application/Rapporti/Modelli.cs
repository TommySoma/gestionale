using System.Text.Json;
using System.Text.Json.Nodes;
using Gestionale.Domain.Rapporti;

namespace Gestionale.Application.Rapporti;

/// <summary>Riga dell'elenco dei rapporti di controllo.</summary>
public sealed class RiepilogoRapporto
{
    public Guid RapportoControlloId { get; init; }

    public Guid ImpiantoId { get; init; }

    public Guid GeneratoreId { get; init; }

    public Guid? InterventoId { get; init; }

    public string Modello { get; init; } = "";

    public DateOnly DataControllo { get; init; }

    public DateOnly? TermineTrasmissione { get; init; }

    public string Stato { get; init; } = "";

    public string? Cliente { get; init; }

    public string? Indirizzo { get; init; }

    public string? Comune { get; init; }

    public string? CodiceBollino { get; init; }

    public int Versione { get; init; }
}

/// <summary>Errore bloccante o avviso restituito da norma.valida_rapporto.</summary>
public sealed record ErroreValidazione(string? Campo, string Messaggio, bool Bloccante);

/// <summary>Scheda del rapporto: dati nella codifica del tracciato, bollino ed esito della validazione.</summary>
public sealed class RapportoControllo
{
    public Guid RapportoControlloId { get; init; }

    public Guid ImpiantoId { get; init; }

    public Guid GeneratoreId { get; init; }

    public Guid? InterventoId { get; init; }

    public string Modello { get; init; } = "";

    public Guid? TecnicoId { get; init; }

    public string? Tecnico { get; init; }

    public DateOnly DataControllo { get; init; }

    public DateOnly? TermineTrasmissione { get; init; }

    public string Stato { get; init; } = "";

    public JsonObject Dati { get; set; } = [];

    public Guid? BollinoId { get; init; }

    public string? CodiceBollino { get; init; }

    public string? TipoBollino { get; init; }

    public DateTime? ConvalidatoIl { get; init; }

    public DateTime? FirmatoIl { get; init; }

    public DateTime? AnnullatoIl { get; init; }

    public string? MotivoAnnullamento { get; init; }

    public DateTime CreatoIl { get; init; }

    public int Versione { get; init; }

    public IReadOnlyList<ErroreValidazione> Errori { get; set; } = [];
}

public sealed record FiltroRapporti(Guid? ImpiantoId, Guid? InterventoId, string? Stato, bool DaTrasmettere);

public sealed record DatiNuovoRapporto(Guid? ImpiantoId, Guid? GeneratoreId, Guid? InterventoId, string? Modello, DateOnly? DataControllo);

/// <summary>Data del controllo e dati della bozza: la PUT li sostituisce entrambi.</summary>
public sealed record DatiBozzaRapporto(DateOnly? DataControllo, JsonElement? Dati);

public abstract record EsitoRapporto
{
    public sealed record Riuscita(Guid Id, int Versione) : EsitoRapporto;

    /// <summary>Errori di forma della richiesta; Chiavi elenca i campi o i codici che li causano.</summary>
    public sealed record NonValida(IReadOnlyList<ErroreRapporto> Errori, IReadOnlyList<string> Chiavi) : EsitoRapporto
    {
        public NonValida(params ErroreRapporto[] errori)
            : this(errori, [])
        {
        }
    }

    public sealed record NonTrovata : EsitoRapporto;

    public sealed record VersioneSuperata : EsitoRapporto;

    public sealed record StatoNonConsentito(string Stato) : EsitoRapporto;

    /// <summary>La validazione contro il tracciato ha errori bloccanti.</summary>
    public sealed record Bloccata(IReadOnlyList<ErroreValidazione> Errori) : EsitoRapporto;

    /// <summary>File di invio preparato con le righe indicate; Esclusi sono i rapporti con errori bloccanti.</summary>
    public sealed record InvioPreparato(Guid Id, int Versione, int Righe, IReadOnlyList<Guid> Esclusi) : EsitoRapporto;

    public sealed record BolliniRegistrati(int Numero) : EsitoRapporto;
}

/// <summary>Passaggi di stato del rapporto, ognuno con la sua funzione SQL.</summary>
public enum PassaggioRapporto
{
    Convalida,
    Riapertura,
    Firma,
    Annullamento,
}

/// <summary>Tipo di bollino in vigore, con quanti l'azienda ne ha disponibili.</summary>
public sealed class TipoBollino
{
    public Guid TipoBollinoId { get; init; }

    public string Codice { get; init; } = "";

    public string Categoria { get; init; } = "";

    public string Nome { get; init; } = "";

    public decimal Importo { get; init; }

    public int Disponibili { get; init; }
}

public sealed class Bollino
{
    public Guid BollinoId { get; init; }

    public string Codice { get; init; } = "";

    public Guid TipoBollinoId { get; init; }

    public string TipoBollino { get; init; } = "";

    public string Categoria { get; init; } = "";

    public decimal Importo { get; init; }

    public DateOnly AcquistatoIl { get; init; }

    public string Stato { get; init; } = "";

    public Guid? RapportoControlloId { get; init; }

    public string? Note { get; init; }

    public int Versione { get; init; }
}

public sealed record FiltroBollini(string? Stato, Guid? TipoBollinoId, string? Codice);

public sealed record DatiAcquistoBollini(Guid? TipoBollinoId, DateOnly? AcquistatoIl, IReadOnlyList<string>? Codici, string? Note);

public sealed class RiepilogoInvio
{
    public Guid InvioCatastoId { get; init; }

    public string Modello { get; init; } = "";

    public string NomeFile { get; init; } = "";

    public string VersioneTracciato { get; init; } = "";

    public string Stato { get; init; } = "";

    public int Righe { get; init; }

    public DateTime? CaricatoIl { get; init; }

    public DateTime? ElaboratoIl { get; init; }

    public DateTime CreatoIl { get; init; }

    public int Versione { get; init; }
}

public sealed class InvioCatasto
{
    public RiepilogoInvio Invio { get; init; } = new();

    public IReadOnlyList<RigaInvio> Rapporti { get; set; } = [];
}

public sealed class RigaInvio
{
    public int Riga { get; init; }

    public Guid RapportoControlloId { get; init; }

    public Guid ImpiantoId { get; init; }

    public DateOnly DataControllo { get; init; }

    public string? Cliente { get; init; }

    public string Esito { get; init; } = "";

    public string? Causa { get; init; }
}

/// <summary>Esito di una riga letto sul report PDF del SIERT.</summary>
public sealed record EsitoRigaInvio(Guid? RapportoControlloId, string? Esito, string? Causa);

public sealed record FileInvio(string NomeFile, byte[] Contenuto);

/// <summary>Riga del file pronta da comporre: tracciato, dati e valori esterni ai dati.</summary>
public sealed record RapportoDaInviare(Guid RapportoControlloId, int Versione, DateOnly DataControllo, JsonObject Dati, string? CodiceBollino);

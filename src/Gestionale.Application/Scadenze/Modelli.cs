using Gestionale.Domain.Scadenze;

namespace Gestionale.Application.Scadenze;

/// <summary>
/// Riga della lista "da richiamare" (<c>lavoro.scadenze_in_arrivo</c>). Per il
/// Tecnico telefono e ultimo richiamo restano vuoti: i richiami sono dell'ufficio.
/// </summary>
public sealed class ScadenzaInArrivo
{
    public Guid ScadenzaId { get; init; }

    public Guid ImpiantoId { get; init; }

    public string Tipo { get; init; } = "";

    public DateOnly DataScadenza { get; init; }

    /// <summary>Negativo se la scadenza è già passata.</summary>
    public int GiorniMancanti { get; init; }

    public string StatoRichiamo { get; init; } = "";

    public int Versione { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public string? Telefono { get; init; }

    public DateTime? UltimoRichiamoIl { get; init; }

    public string? UltimoEsito { get; init; }

    public DateOnly? RichiamareIl { get; init; }
}

/// <summary>Scadenza di un impianto, aperta o nello storico, con i richiami.</summary>
public sealed class Scadenza
{
    public Guid ScadenzaId { get; init; }

    public Guid ImpiantoId { get; init; }

    public string Tipo { get; init; } = "";

    public DateOnly DataScadenza { get; init; }

    public string Stato { get; init; } = "";

    public int PeriodicitaMesi { get; init; }

    public string? Regola { get; init; }

    /// <summary>La regola di cadenza è una voce di prova, non verificata sui testi ufficiali.</summary>
    public bool RegolaDaVerificare { get; init; }

    public bool DataManuale { get; init; }

    public Guid? InterventoOrigineId { get; init; }

    public Guid? InterventoChiusuraId { get; init; }

    public DateTime? ChiusaIl { get; init; }

    public DateTime? AnnullataIl { get; init; }

    public string? MotivoAnnullamento { get; init; }

    public string StatoRichiamo { get; init; } = "";

    public int Versione { get; init; }

    public IReadOnlyList<Richiamo> Richiami { get; set; } = [];
}

public sealed class Richiamo
{
    public Guid RichiamoId { get; init; }

    public string Canale { get; init; } = "";

    public DateTime EffettuatoIl { get; init; }

    public string? EffettuatoDa { get; init; }

    public string Esito { get; init; } = "";

    public DateOnly? RichiamareIl { get; init; }

    public Guid? InterventoId { get; init; }

    public string? Note { get; init; }
}

/// <summary>Giorni comprende anche le scadute; TecnicoUtenteId limita agli impianti dei suoi interventi.</summary>
public sealed record FiltroScadenze(int Giorni, string? Tipo = null, string? StatoRichiamo = null, Guid? TecnicoUtenteId = null);

/// <summary>Esito di una scrittura su una scadenza.</summary>
public abstract record EsitoScadenza
{
    public sealed record Riuscita(Guid Id, int Versione) : EsitoScadenza;

    public sealed record NonValida(IReadOnlyList<ErroreScadenza> Errori) : EsitoScadenza;

    public sealed record NonTrovata : EsitoScadenza;

    public sealed record VersioneSuperata : EsitoScadenza;

    /// <summary>La scadenza è chiusa o annullata: non si corregge e non si richiama.</summary>
    public sealed record NonAperta(string Stato) : EsitoScadenza;
}

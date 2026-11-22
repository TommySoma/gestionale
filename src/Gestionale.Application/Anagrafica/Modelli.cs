using Gestionale.Domain.Anagrafica;

namespace Gestionale.Application.Anagrafica;

/// <summary>Chi sta lavorando: l'azienda e l'utente impostano la Row-Level Security.</summary>
public sealed record Contesto(Guid AziendaId, Guid UtenteId);

public sealed record Pagina<T>(IReadOnlyList<T> Elementi, int Totale, int NumeroPagina, int Dimensione);

public sealed class RiepilogoCliente
{
    public Guid ClienteId { get; init; }

    public string Tipo { get; init; } = "";

    public string NomeVisualizzato { get; init; } = "";

    public string? CodiceFiscale { get; init; }

    public string? PartitaIva { get; init; }

    public string? ComunePrincipale { get; init; }

    public int Versione { get; init; }
}

public sealed class Cliente
{
    public Guid ClienteId { get; init; }

    public string Tipo { get; init; } = "";

    public string? Denominazione { get; init; }

    public string? Nome { get; init; }

    public string? Cognome { get; init; }

    public string? CodiceFiscale { get; init; }

    public string? PartitaIva { get; init; }

    public string? Pec { get; init; }

    public string? Note { get; init; }

    public int Versione { get; init; }

    public IReadOnlyList<Sede> Sedi { get; set; } = [];

    public IReadOnlyList<Contatto> Contatti { get; set; } = [];

    public IReadOnlyList<ConsensoCorrente> Consensi { get; set; } = [];
}

public sealed class Sede
{
    public Guid SedeId { get; init; }

    public string? Descrizione { get; init; }

    public string Indirizzo { get; init; } = "";

    public string? Civico { get; init; }

    public string Cap { get; init; } = "";

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public string? NoteAccesso { get; init; }

    public decimal? Latitudine { get; init; }

    public decimal? Longitudine { get; init; }

    public int Versione { get; init; }
}

public sealed class Contatto
{
    public Guid ContattoId { get; init; }

    public string Tipo { get; init; } = "";

    public string Valore { get; init; } = "";

    public string? Etichetta { get; init; }

    public bool Principale { get; init; }

    public int Versione { get; init; }
}

public sealed class ConsensoCorrente
{
    public string Finalita { get; init; } = "";

    public bool Concesso { get; init; }

    public string Fonte { get; init; } = "";

    public DateTime RegistratoIl { get; init; }
}

public sealed class VoceStoricoConsenso
{
    public Guid ConsensoId { get; init; }

    public string Finalita { get; init; } = "";

    public bool Concesso { get; init; }

    public string Fonte { get; init; } = "";

    public string? Note { get; init; }

    public DateTime RegistratoIl { get; init; }
}

public sealed class FinalitaPrivacy
{
    public string Codice { get; init; } = "";

    public string Nome { get; init; } = "";

    public string Descrizione { get; init; } = "";
}

public sealed record DatiConsenso(string Finalita, bool Concesso, string Fonte, string? Note);

/// <summary>Esito di una scrittura con concorrenza ottimistica.</summary>
public abstract record EsitoScrittura
{
    public sealed record Riuscita(Guid Id, int Versione) : EsitoScrittura;

    public sealed record NonValida(IReadOnlyList<ErroreAnagrafica> Errori) : EsitoScrittura;

    public sealed record NonTrovata : EsitoScrittura;

    /// <summary>Qualcun altro ha modificato il dato: la versione inviata non è più quella corrente.</summary>
    public sealed record VersioneSuperata : EsitoScrittura;

    public sealed record Duplicata(ErroreAnagrafica Motivo) : EsitoScrittura;
}

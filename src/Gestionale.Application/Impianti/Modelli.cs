using Gestionale.Domain.Impianti;

namespace Gestionale.Application.Impianti;

public sealed class RiepilogoImpianto
{
    public Guid ImpiantoId { get; init; }

    public string TipoImpianto { get; init; } = "";

    public string Stato { get; init; } = "";

    public Guid SedeId { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string? Civico { get; init; }

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public string? Pdr { get; init; }

    public string? CodiceCatasto { get; init; }

    public int NumeroGeneratori { get; init; }

    public int Versione { get; init; }
}

public sealed class Impianto
{
    public Guid ImpiantoId { get; init; }

    public Guid SedeId { get; init; }

    public Guid ClienteId { get; init; }

    public string Cliente { get; init; } = "";

    public string Indirizzo { get; init; } = "";

    public string? Civico { get; init; }

    public string Cap { get; init; } = "";

    public string Comune { get; init; } = "";

    public string Provincia { get; init; } = "";

    public Guid TipoImpiantoId { get; init; }

    public string TipoImpianto { get; init; } = "";

    public bool TipoDaVerificare { get; init; }

    public string Stato { get; init; } = "";

    public int? AnnoInstallazione { get; init; }

    public decimal? PotenzaUtileKw { get; init; }

    public Guid? CombustibileId { get; init; }

    public string? Combustibile { get; init; }

    public string? Pdr { get; init; }

    public Guid? ProprietarioClienteId { get; init; }

    public string? Proprietario { get; init; }

    public Guid? ResponsabileClienteId { get; init; }

    public string? Responsabile { get; init; }

    public string? Note { get; init; }

    public DateOnly? DismessoIl { get; init; }

    public int Versione { get; init; }

    public bool IndirizzoDuplicato { get; init; }

    public IReadOnlyList<Generatore> Generatori { get; set; } = [];

    public IReadOnlyList<RiferimentoCatasto> RiferimentiCatasto { get; set; } = [];
}

public sealed class Generatore
{
    public Guid GeneratoreId { get; init; }

    public Guid TipoGeneratoreId { get; init; }

    public string TipoGeneratore { get; init; } = "";

    public string? Marca { get; init; }

    public string? Modello { get; init; }

    public string? Matricola { get; init; }

    public decimal? PotenzaKw { get; init; }

    public Guid? CombustibileId { get; init; }

    public string? Combustibile { get; init; }

    public DateOnly? InstallatoIl { get; init; }

    public DateOnly? DismessoIl { get; init; }

    public string? Note { get; init; }

    public int Versione { get; init; }

    public bool MatricolaDuplicata { get; init; }
}

public sealed class RiferimentoCatasto
{
    public Guid RiferimentoCatastoId { get; init; }

    public string Regione { get; init; } = "";

    public string Sistema { get; init; } = "";

    public string Codice { get; init; } = "";

    public DateOnly? RegistratoIl { get; init; }

    public int Versione { get; init; }
}

/// <summary>Voce di una lookup normativa. Quelle da verificare non vengono dai moduli ufficiali.</summary>
public sealed class VoceLookup
{
    public Guid Id { get; init; }

    public string Codice { get; init; } = "";

    public string Nome { get; init; } = "";

    public bool DaVerificare { get; init; }
}

public sealed record VociImpianti(IReadOnlyList<VoceLookup> TipiImpianto, IReadOnlyList<VoceLookup> TipiGeneratore, IReadOnlyList<VoceLookup> Combustibili);

public sealed record FiltroImpianti(string? Testo, Guid? ClienteId, string? Stato);

/// <summary>Esito di una scrittura su impianti, generatori o riferimenti al catasto.</summary>
public abstract record EsitoImpianto
{
    public sealed record Riuscita(Guid Id, int Versione, IReadOnlyList<AvvisoImpianto> Avvisi) : EsitoImpianto;

    public sealed record NonValida(IReadOnlyList<ErroreImpianto> Errori) : EsitoImpianto;

    public sealed record NonTrovata : EsitoImpianto;

    public sealed record VersioneSuperata : EsitoImpianto;

    public sealed record Duplicata(ErroreImpianto Motivo) : EsitoImpianto;

    /// <summary>Il permesso c'è, ma solo sui propri impianti: non basta per crearne di nuovi.</summary>
    public sealed record Vietata : EsitoImpianto;
}

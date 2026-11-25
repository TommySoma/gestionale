using Gestionale.Application.Anagrafica;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Commerciale;

namespace Gestionale.Application.Commerciale;

/// <summary>Persistenza del listino; ogni metodo è una transazione sotto Row-Level Security.</summary>
public interface IRepositoryListino
{
    Task<Pagina<VoceListino>> CercaAsync(Contesto contesto, string? testo, bool soloAttive, int numeroPagina, int dimensione, CancellationToken ct);

    Task<VoceListino?> VoceAsync(Contesto contesto, Guid voceId, CancellationToken ct);

    /// <summary>Crea la voce e, se indicato, il suo primo prezzo nella stessa transazione.</summary>
    Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiVoce dati, DatiPrezzo? prezzo, CancellationToken ct);

    Task<EsitoCommerciale> AggiornaAsync(Contesto contesto, Guid voceId, int versione, DatiVoce dati, CancellationToken ct);

    Task<EsitoCommerciale> AggiungiPrezzoAsync(Contesto contesto, Guid voceId, DatiPrezzo prezzo, CancellationToken ct);
}

/// <summary>Persistenza dei preventivi; ogni metodo è una transazione sotto Row-Level Security.</summary>
public interface IRepositoryPreventivi
{
    Task<Pagina<RiepilogoPreventivo>> CercaAsync(Contesto contesto, FiltroPreventivi filtro, int numeroPagina, int dimensione, CancellationToken ct);

    Task<Preventivo?> PreventivoAsync(Contesto contesto, Guid preventivoId, CancellationToken ct);

    Task<IntestazioneAzienda> AziendaAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct);

    /// <summary>Intestazione e righe di una bozza, insieme.</summary>
    Task<EsitoCommerciale> AggiornaAsync(Contesto contesto, Guid preventivoId, int versione, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct);

    Task<EsitoCommerciale> EliminaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct);

    Task<EsitoCommerciale> EmettiAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct);

    /// <summary>Accettazione o rifiuto di un preventivo emesso.</summary>
    Task<EsitoCommerciale> RispostaClienteAsync(Contesto contesto, Guid preventivoId, int versione, bool accettato, CancellationToken ct);

    Task<EsitoCommerciale> AnnullaAsync(Contesto contesto, Guid preventivoId, int versione, string motivo, CancellationToken ct);

    Task<EsitoCommerciale> ConvertiAsync(Contesto contesto, Guid preventivoId, int versione, string tipoIntervento, CancellationToken ct);
}

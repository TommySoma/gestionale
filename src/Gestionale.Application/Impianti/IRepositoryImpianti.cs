using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Impianti;

namespace Gestionale.Application.Impianti;

/// <summary>Persistenza degli impianti; ogni metodo è una transazione sotto Row-Level Security.</summary>
public interface IRepositoryImpianti
{
    Task<Pagina<RiepilogoImpianto>> CercaAsync(Contesto contesto, FiltroImpianti filtro, int numeroPagina, int dimensione, CancellationToken ct);

    Task<Impianto?> ImpiantoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct);

    Task<VociImpianti> VociAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoImpianto> CreaAsync(Contesto contesto, DatiImpianto dati, CancellationToken ct);

    Task<EsitoImpianto> AggiornaAsync(Contesto contesto, Guid impiantoId, int versione, DatiImpianto dati, CancellationToken ct);

    Task<EsitoImpianto> EliminaAsync(Contesto contesto, Guid impiantoId, int versione, CancellationToken ct);

    Task<EsitoImpianto> CreaGeneratoreAsync(Contesto contesto, Guid impiantoId, DatiGeneratore dati, CancellationToken ct);

    Task<EsitoImpianto> AggiornaGeneratoreAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, DatiGeneratore dati, CancellationToken ct);

    Task<EsitoImpianto> CreaRiferimentoAsync(Contesto contesto, Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct);

    Task<EsitoImpianto> AggiornaRiferimentoAsync(
        Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, DatiRiferimentoCatasto dati, CancellationToken ct);

    Task<EsitoImpianto> EliminaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct);
}

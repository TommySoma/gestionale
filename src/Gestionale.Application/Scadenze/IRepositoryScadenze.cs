using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Scadenze;

namespace Gestionale.Application.Scadenze;

/// <summary>
/// Persistenza delle scadenze; ogni metodo è una transazione sotto Row-Level Security.
/// Dove compare, <c>soloTecnico</c> limita agli impianti su cui quell'utente ha un intervento non annullato.
/// </summary>
public interface IRepositoryScadenze
{
    Task<Pagina<ScadenzaInArrivo>> InArrivoAsync(Contesto contesto, FiltroScadenze filtro, int numeroPagina, int dimensione, CancellationToken ct);

    Task<Scadenza?> ScadenzaAsync(Contesto contesto, Guid scadenzaId, Guid? soloTecnico, CancellationToken ct);

    /// <summary>Scadenze dell'impianto, prima le aperte; null se l'impianto non c'è o non è visibile.</summary>
    Task<IReadOnlyList<Scadenza>?> DellImpiantoAsync(Contesto contesto, Guid impiantoId, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoScadenza> CorreggiDataAsync(Contesto contesto, Guid scadenzaId, int versione, DateOnly data, CancellationToken ct);

    Task<EsitoScadenza> RegistraRichiamoAsync(Contesto contesto, Guid scadenzaId, int versione, DatiRichiamo dati, CancellationToken ct);
}

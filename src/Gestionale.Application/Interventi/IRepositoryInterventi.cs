using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Interventi;

namespace Gestionale.Application.Interventi;

/// <summary>
/// Persistenza degli interventi; ogni metodo è una transazione sotto Row-Level Security.
/// Dove compare, <c>soloTecnico</c> limita l'operazione agli interventi assegnati a quell'utente.
/// </summary>
public interface IRepositoryInterventi
{
    Task<Pagina<RiepilogoIntervento>> CercaAsync(Contesto contesto, FiltroInterventi filtro, int numeroPagina, int dimensione, CancellationToken ct);

    Task<Intervento?> InterventoAsync(Contesto contesto, Guid interventoId, Guid? soloTecnico, CancellationToken ct);

    Task<IReadOnlyList<TecnicoAssegnabile>> TecniciAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoIntervento> CreaAsync(Contesto contesto, DatiIntervento dati, CancellationToken ct);

    Task<EsitoIntervento> AggiornaAsync(Contesto contesto, Guid interventoId, int versione, DatiIntervento dati, CancellationToken ct);

    Task<EsitoIntervento> AvviaAsync(Contesto contesto, Guid interventoId, int versione, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoIntervento> ChiudiAsync(Contesto contesto, Guid interventoId, int versione, DatiChiusura dati, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoIntervento> AnnullaAsync(Contesto contesto, Guid interventoId, int versione, string motivo, CancellationToken ct);
}

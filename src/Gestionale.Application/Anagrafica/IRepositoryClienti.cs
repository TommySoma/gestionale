using Gestionale.Domain.Anagrafica;

namespace Gestionale.Application.Anagrafica;

/// <summary>Persistenza delle anagrafiche; ogni metodo è una transazione sotto Row-Level Security.</summary>
public interface IRepositoryClienti
{
    Task<Pagina<RiepilogoCliente>> CercaAsync(Contesto contesto, string? testo, string? tipo, int numeroPagina, int dimensione, CancellationToken ct);

    Task<Cliente?> ClienteAsync(Contesto contesto, Guid clienteId, CancellationToken ct);

    Task<EsitoScrittura> CreaAsync(Contesto contesto, DatiCliente dati, CancellationToken ct);

    Task<EsitoScrittura> AggiornaAsync(Contesto contesto, Guid clienteId, int versione, DatiCliente dati, CancellationToken ct);

    Task<EsitoScrittura> EliminaAsync(Contesto contesto, Guid clienteId, int versione, CancellationToken ct);

    Task<EsitoScrittura> AnonimizzaAsync(Contesto contesto, Guid clienteId, int versione, CancellationToken ct);

    Task<EsitoScrittura> CreaSedeAsync(Contesto contesto, Guid clienteId, DatiSede dati, CancellationToken ct);

    Task<EsitoScrittura> AggiornaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, DatiSede dati, CancellationToken ct);

    Task<EsitoScrittura> EliminaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, CancellationToken ct);

    Task<EsitoScrittura> CreaContattoAsync(Contesto contesto, Guid clienteId, DatiContatto dati, CancellationToken ct);

    Task<EsitoScrittura> AggiornaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, DatiContatto dati, CancellationToken ct);

    Task<EsitoScrittura> EliminaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, CancellationToken ct);

    Task<IReadOnlyList<FinalitaPrivacy>> FinalitaAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoScrittura> RegistraConsensoAsync(Contesto contesto, Guid clienteId, DatiConsenso dati, CancellationToken ct);

    Task<IReadOnlyList<VoceStoricoConsenso>?> StoricoConsensiAsync(Contesto contesto, Guid clienteId, CancellationToken ct);

    Task<Azienda> AziendaAsync(Contesto contesto, CancellationToken ct);

    Task<EsitoScrittura> AggiornaAziendaAsync(Contesto contesto, int versione, DatiAzienda dati, CancellationToken ct);
}

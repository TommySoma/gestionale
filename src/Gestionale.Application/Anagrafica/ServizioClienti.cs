using Gestionale.Domain.Anagrafica;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Anagrafica;

/// <summary>
/// Anagrafiche: validazione e normalizzazione prima di scrivere, filtro
/// dell'ambito "propri" in lettura.
/// </summary>
public sealed class ServizioClienti(IRepositoryClienti repository)
{
    public const int DimensioneMassimaPagina = 100;

    /// <summary>
    /// Con l'ambito "propri" (il Tecnico) si vedono solo i clienti delle attività
    /// assegnate all'utente. Gli interventi arrivano con M2: fino ad allora
    /// l'elenco è vuoto, per non mostrare più del dovuto.
    /// </summary>
    public async Task<Pagina<RiepilogoCliente>> CercaAsync(
        Contesto contesto, AmbitoPermesso ambito, string? testo, string? tipo, int numeroPagina, int dimensione, CancellationToken ct)
    {
        numeroPagina = Math.Max(1, numeroPagina);
        dimensione = Math.Clamp(dimensione, 1, DimensioneMassimaPagina);

        if (ambito == AmbitoPermesso.Propri)
        {
            return new Pagina<RiepilogoCliente>([], 0, numeroPagina, dimensione);
        }

        var tipoFiltro = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim().ToLowerInvariant();
        var testoFiltro = string.IsNullOrWhiteSpace(testo) ? null : testo.Trim();
        return await repository.CercaAsync(contesto, testoFiltro, tipoFiltro, numeroPagina, dimensione, ct);
    }

    public Task<Cliente?> ClienteAsync(Contesto contesto, AmbitoPermesso ambito, Guid clienteId, CancellationToken ct) =>
        ambito == AmbitoPermesso.Propri ? Task.FromResult<Cliente?>(null) : repository.ClienteAsync(contesto, clienteId, ct);

    public Task<EsitoScrittura> CreaAsync(Contesto contesto, DatiCliente dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.CreaAsync(contesto, d, ct));

    public Task<EsitoScrittura> AggiornaAsync(Contesto contesto, Guid clienteId, int versione, DatiCliente dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.AggiornaAsync(contesto, clienteId, versione, d, ct));

    public Task<EsitoScrittura> EliminaAsync(Contesto contesto, Guid clienteId, int versione, CancellationToken ct) =>
        repository.EliminaAsync(contesto, clienteId, versione, ct);

    public Task<EsitoScrittura> CreaSedeAsync(Contesto contesto, Guid clienteId, DatiSede dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.CreaSedeAsync(contesto, clienteId, d, ct));

    public Task<EsitoScrittura> AggiornaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, DatiSede dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.AggiornaSedeAsync(contesto, clienteId, sedeId, versione, d, ct));

    public Task<EsitoScrittura> EliminaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, CancellationToken ct) =>
        repository.EliminaSedeAsync(contesto, clienteId, sedeId, versione, ct);

    public Task<EsitoScrittura> CreaContattoAsync(Contesto contesto, Guid clienteId, DatiContatto dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.CreaContattoAsync(contesto, clienteId, d, ct));

    public Task<EsitoScrittura> AggiornaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, DatiContatto dati, CancellationToken ct) =>
        Valida(RegoleAnagrafica.Valida(dati), d => repository.AggiornaContattoAsync(contesto, clienteId, contattoId, versione, d, ct));

    public Task<EsitoScrittura> EliminaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, CancellationToken ct) =>
        repository.EliminaContattoAsync(contesto, clienteId, contattoId, versione, ct);

    public Task<IReadOnlyList<FinalitaPrivacy>> FinalitaAsync(Contesto contesto, CancellationToken ct) =>
        repository.FinalitaAsync(contesto, ct);

    public async Task<EsitoScrittura> RegistraConsensoAsync(Contesto contesto, Guid clienteId, DatiConsenso dati, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var normalizzati = dati with
        {
            Finalita = (dati.Finalita ?? "").Trim().ToLowerInvariant(),
            Fonte = (dati.Fonte ?? "").Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(dati.Note) ? null : dati.Note.Trim(),
        };

        if (!FontiConsenso.Tutte.Contains(normalizzati.Fonte))
        {
            return new EsitoScrittura.NonValida([ErroreAnagrafica.FonteConsensoNonValida]);
        }

        return await repository.RegistraConsensoAsync(contesto, clienteId, normalizzati, ct);
    }

    public Task<IReadOnlyList<VoceStoricoConsenso>?> StoricoConsensiAsync(Contesto contesto, AmbitoPermesso ambito, Guid clienteId, CancellationToken ct) =>
        ambito == AmbitoPermesso.Propri
            ? Task.FromResult<IReadOnlyList<VoceStoricoConsenso>?>(null)
            : repository.StoricoConsensiAsync(contesto, clienteId, ct);

    private static async Task<EsitoScrittura> Valida<T>((T Dati, IReadOnlyList<ErroreAnagrafica> Errori) validazione, Func<T, Task<EsitoScrittura>> scrivi) =>
        validazione.Errori.Count > 0
            ? new EsitoScrittura.NonValida(validazione.Errori)
            : await scrivi(validazione.Dati);
}

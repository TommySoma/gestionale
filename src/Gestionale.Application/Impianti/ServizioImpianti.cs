using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Impianti;

/// <summary>
/// Impianti, generatori e riferimenti al catasto: validazione prima di scrivere e
/// ambito "propri" per il Tecnico. Un impianto è "proprio" quando il Tecnico ha un
/// intervento assegnato su di esso: gli interventi arrivano nella prossima parte di
/// M2, quindi fino ad allora il Tecnico non vede né modifica alcun impianto e non
/// ne crea di nuovi.
/// </summary>
public sealed class ServizioImpianti(IRepositoryImpianti repository)
{
    public const int DimensioneMassimaPagina = 100;

    public async Task<Pagina<RiepilogoImpianto>> CercaAsync(
        Contesto contesto, AmbitoPermesso ambito, FiltroImpianti filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        numeroPagina = Math.Max(1, numeroPagina);
        dimensione = Math.Clamp(dimensione, 1, DimensioneMassimaPagina);

        if (ambito == AmbitoPermesso.Propri)
        {
            return new Pagina<RiepilogoImpianto>([], 0, numeroPagina, dimensione);
        }

        var normalizzato = new FiltroImpianti(
            string.IsNullOrWhiteSpace(filtro.Testo) ? null : filtro.Testo.Trim(),
            filtro.ClienteId,
            string.IsNullOrWhiteSpace(filtro.Stato) ? null : filtro.Stato.Trim().ToLowerInvariant());
        return await repository.CercaAsync(contesto, normalizzato, numeroPagina, dimensione, ct);
    }

    public Task<Impianto?> ImpiantoAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, CancellationToken ct) =>
        ambito == AmbitoPermesso.Propri ? Task.FromResult<Impianto?>(null) : repository.ImpiantoAsync(contesto, impiantoId, ct);

    public Task<VociImpianti> VociAsync(Contesto contesto, CancellationToken ct) => repository.VociAsync(contesto, ct);

    public Task<EsitoImpianto> CreaAsync(Contesto contesto, AmbitoPermesso ambito, DatiImpianto dati, CancellationToken ct) =>
        ambito == AmbitoPermesso.Propri
            ? Task.FromResult<EsitoImpianto>(new EsitoImpianto.Vietata())
            : Valida(RegoleImpianti.Valida(dati), d => repository.CreaAsync(contesto, d, ct));

    public Task<EsitoImpianto> AggiornaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, int versione, DatiImpianto dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(RegoleImpianti.Valida(dati), d => repository.AggiornaAsync(contesto, impiantoId, versione, d, ct)));

    public Task<EsitoImpianto> EliminaAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, int versione, CancellationToken ct) =>
        SoloTutti(ambito, () => repository.EliminaAsync(contesto, impiantoId, versione, ct));

    public Task<EsitoImpianto> CreaGeneratoreAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, DatiGeneratore dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(RegoleImpianti.Valida(dati), d => repository.CreaGeneratoreAsync(contesto, impiantoId, d, ct)));

    public Task<EsitoImpianto> AggiornaGeneratoreAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid generatoreId, int versione, DatiGeneratore dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(
            RegoleImpianti.Valida(dati), d => repository.AggiornaGeneratoreAsync(contesto, impiantoId, generatoreId, versione, d, ct)));

    public Task<EsitoImpianto> CreaRiferimentoAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(RegoleImpianti.Valida(dati), d => repository.CreaRiferimentoAsync(contesto, impiantoId, d, ct)));

    public Task<EsitoImpianto> AggiornaRiferimentoAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid riferimentoId, int versione, DatiRiferimentoCatasto dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(
            RegoleImpianti.Valida(dati), d => repository.AggiornaRiferimentoAsync(contesto, impiantoId, riferimentoId, versione, d, ct)));

    public Task<EsitoImpianto> EliminaRiferimentoAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct) =>
        SoloTutti(ambito, () => repository.EliminaRiferimentoAsync(contesto, impiantoId, riferimentoId, versione, ct));

    // Con l'ambito "propri" nessun impianto è ancora visibile: risponde come se non esistesse.
    private static Task<EsitoImpianto> SoloTutti(AmbitoPermesso ambito, Func<Task<EsitoImpianto>> scrivi) =>
        ambito == AmbitoPermesso.Propri ? Task.FromResult<EsitoImpianto>(new EsitoImpianto.NonTrovata()) : scrivi();

    private static async Task<EsitoImpianto> Valida<T>((T Dati, IReadOnlyList<ErroreImpianto> Errori) validazione, Func<T, Task<EsitoImpianto>> scrivi) =>
        validazione.Errori.Count > 0
            ? new EsitoImpianto.NonValida(validazione.Errori)
            : await scrivi(validazione.Dati);
}

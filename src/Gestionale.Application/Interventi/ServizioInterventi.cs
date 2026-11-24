using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Interventi;

/// <summary>
/// Interventi e calendario. Con l'ambito "propri" (il Tecnico) si vedono solo gli
/// interventi assegnati all'utente, e su quelli si può solo avviare e chiudere:
/// creare, pianificare, riassegnare e annullare spetta a chi ha l'ambito su tutto.
/// </summary>
public sealed class ServizioInterventi(IRepositoryInterventi repository)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<Pagina<RiepilogoIntervento>> CercaAsync(
        Contesto contesto, AmbitoPermesso ambito, FiltroInterventi filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(filtro);

        numeroPagina = Math.Max(1, numeroPagina);
        dimensione = Math.Clamp(dimensione, 1, DimensioneMassimaPagina);

        var normalizzato = filtro with
        {
            Stato = string.IsNullOrWhiteSpace(filtro.Stato) ? null : filtro.Stato.Trim().ToLowerInvariant(),
            // Il Tecnico vede solo i propri, qualunque filtro chieda.
            TecnicoUtenteId = ambito == AmbitoPermesso.Propri ? contesto.UtenteId : filtro.TecnicoUtenteId,
        };
        return repository.CercaAsync(contesto, normalizzato, numeroPagina, dimensione, ct);
    }

    public Task<Intervento?> InterventoAsync(Contesto contesto, AmbitoPermesso ambito, Guid interventoId, CancellationToken ct) =>
        repository.InterventoAsync(contesto, interventoId, SoloTecnico(contesto, ambito), ct);

    public Task<IReadOnlyList<TecnicoAssegnabile>> TecniciAsync(Contesto contesto, CancellationToken ct) =>
        repository.TecniciAsync(contesto, ct);

    public Task<EsitoIntervento> CreaAsync(Contesto contesto, AmbitoPermesso ambito, DatiIntervento dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(RegoleInterventi.Valida(dati), d => repository.CreaAsync(contesto, d, ct)));

    public Task<EsitoIntervento> AggiornaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid interventoId, int versione, DatiIntervento dati, CancellationToken ct) =>
        SoloTutti(ambito, () => Valida(RegoleInterventi.Valida(dati), d => repository.AggiornaAsync(contesto, interventoId, versione, d, ct)));

    public Task<EsitoIntervento> AvviaAsync(Contesto contesto, AmbitoPermesso ambito, Guid interventoId, int versione, CancellationToken ct) =>
        repository.AvviaAsync(contesto, interventoId, versione, SoloTecnico(contesto, ambito), ct);

    public Task<EsitoIntervento> ChiudiAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid interventoId, int versione, DatiChiusura dati, CancellationToken ct) =>
        Valida(RegoleInterventi.Valida(dati), d => repository.ChiudiAsync(contesto, interventoId, versione, d, SoloTecnico(contesto, ambito), ct));

    public Task<EsitoIntervento> AnnullaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid interventoId, int versione, string? motivo, CancellationToken ct) =>
        SoloTutti(ambito, () => RegoleInterventi.MotivoAnnullamento(motivo) is { } m
            ? repository.AnnullaAsync(contesto, interventoId, versione, m, ct)
            : Task.FromResult<EsitoIntervento>(new EsitoIntervento.NonValida([ErroreIntervento.MotivoAnnullamentoMancante])));

    private static Guid? SoloTecnico(Contesto contesto, AmbitoPermesso ambito) =>
        ambito == AmbitoPermesso.Propri ? contesto.UtenteId : null;

    private static Task<EsitoIntervento> SoloTutti(AmbitoPermesso ambito, Func<Task<EsitoIntervento>> scrivi) =>
        ambito == AmbitoPermesso.Propri ? Task.FromResult<EsitoIntervento>(new EsitoIntervento.Vietata()) : scrivi();

    private static async Task<EsitoIntervento> Valida<T>(
        (T Dati, IReadOnlyList<ErroreIntervento> Errori) validazione, Func<T, Task<EsitoIntervento>> scrivi) =>
        validazione.Errori.Count > 0
            ? new EsitoIntervento.NonValida(validazione.Errori)
            : await scrivi(validazione.Dati);
}

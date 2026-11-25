using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Commerciale;
using Gestionale.Domain.Interventi;

namespace Gestionale.Application.Commerciale;

/// <summary>Listino prezzi: voci che si disattivano e prezzi che si aggiungono con una data di validità.</summary>
public sealed class ServizioListino(IRepositoryListino repository)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<Pagina<VoceListino>> CercaAsync(Contesto contesto, string? testo, bool soloAttive, int numeroPagina, int dimensione, CancellationToken ct) =>
        repository.CercaAsync(
            contesto, string.IsNullOrWhiteSpace(testo) ? null : testo.Trim(), soloAttive,
            Math.Max(1, numeroPagina), Math.Clamp(dimensione, 1, DimensioneMassimaPagina), ct);

    public Task<VoceListino?> VoceAsync(Contesto contesto, Guid voceId, CancellationToken ct) => repository.VoceAsync(contesto, voceId, ct);

    public Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiVoce dati, DatiPrezzo? prezzo, CancellationToken ct)
    {
        var (voce, errori) = RegoleCommerciali.Valida(dati);
        var tutti = errori.Concat(prezzo is null ? [] : RegoleCommerciali.Valida(prezzo)).ToList();
        return tutti.Count > 0
            ? Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida(tutti))
            : repository.CreaAsync(contesto, voce, prezzo, ct);
    }

    public Task<EsitoCommerciale> AggiornaAsync(Contesto contesto, Guid voceId, int versione, DatiVoce dati, CancellationToken ct)
    {
        var (voce, errori) = RegoleCommerciali.Valida(dati);
        return errori.Count > 0
            ? Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida(errori))
            : repository.AggiornaAsync(contesto, voceId, versione, voce, ct);
    }

    public Task<EsitoCommerciale> AggiungiPrezzoAsync(Contesto contesto, Guid voceId, DatiPrezzo prezzo, CancellationToken ct) =>
        RegoleCommerciali.Valida(prezzo) is { Count: > 0 } errori
            ? Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida(errori))
            : repository.AggiungiPrezzoAsync(contesto, voceId, prezzo, ct);
}

/// <summary>
/// Preventivi: la bozza si modifica e si elimina; all'emissione prende il numero
/// e da lì cambia solo lo stato. Un accettato genera l'intervento da pianificare.
/// </summary>
public sealed class ServizioPreventivi(IRepositoryPreventivi repository, IGeneratorePreventivo generatore)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<Pagina<RiepilogoPreventivo>> CercaAsync(Contesto contesto, FiltroPreventivi filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var normalizzato = filtro with
        {
            Testo = string.IsNullOrWhiteSpace(filtro.Testo) ? null : filtro.Testo.Trim(),
            Stato = string.IsNullOrWhiteSpace(filtro.Stato) ? null : filtro.Stato.Trim().ToLowerInvariant(),
        };
        return repository.CercaAsync(contesto, normalizzato, Math.Max(1, numeroPagina), Math.Clamp(dimensione, 1, DimensioneMassimaPagina), ct);
    }

    public Task<Preventivo?> PreventivoAsync(Contesto contesto, Guid preventivoId, CancellationToken ct) =>
        repository.PreventivoAsync(contesto, preventivoId, ct);

    public async Task<EsitoPdfPreventivo> PdfAsync(Contesto contesto, Guid preventivoId, CancellationToken ct)
    {
        if (await repository.PreventivoAsync(contesto, preventivoId, ct) is not { } preventivo)
        {
            return new EsitoPdfPreventivo.NonTrovato();
        }

        var azienda = await repository.AziendaAsync(contesto, ct);
        var nome = preventivo.Numero is { } numero ? $"preventivo-{preventivo.Anno}-{numero:D4}.pdf" : "bozza-di-preventivo.pdf";
        return new EsitoPdfPreventivo.Pronto(generatore.Genera(azienda, preventivo), nome);
    }

    public Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiPreventivo dati, IReadOnlyList<RigaPreventivo>? righe, CancellationToken ct)
    {
        var (normalizzati, normalizzate, errori) = RegoleCommerciali.Valida(dati, righe);
        return errori.Count > 0
            ? Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida(errori))
            : repository.CreaAsync(contesto, normalizzati, normalizzate, ct);
    }

    public Task<EsitoCommerciale> AggiornaAsync(
        Contesto contesto, Guid preventivoId, int versione, DatiPreventivo dati, IReadOnlyList<RigaPreventivo>? righe, CancellationToken ct)
    {
        var (normalizzati, normalizzate, errori) = RegoleCommerciali.Valida(dati, righe);
        return errori.Count > 0
            ? Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida(errori))
            : repository.AggiornaAsync(contesto, preventivoId, versione, normalizzati, normalizzate, ct);
    }

    public Task<EsitoCommerciale> EliminaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        repository.EliminaAsync(contesto, preventivoId, versione, ct);

    public Task<EsitoCommerciale> EmettiAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        repository.EmettiAsync(contesto, preventivoId, versione, ct);

    public Task<EsitoCommerciale> AccettaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        repository.RispostaClienteAsync(contesto, preventivoId, versione, accettato: true, ct);

    public Task<EsitoCommerciale> RifiutaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        repository.RispostaClienteAsync(contesto, preventivoId, versione, accettato: false, ct);

    public Task<EsitoCommerciale> AnnullaAsync(Contesto contesto, Guid preventivoId, int versione, string? motivo, CancellationToken ct) =>
        RegoleCommerciali.MotivoAnnullamento(motivo) is { } m
            ? repository.AnnullaAsync(contesto, preventivoId, versione, m, ct)
            : Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida([ErroreCommerciale.MotivoAnnullamentoPreventivoMancante]));

    /// <summary>Il tipo dell'intervento lo sceglie chi converte: un preventivo può essere un'installazione come una riparazione.</summary>
    public Task<EsitoCommerciale> ConvertiAsync(Contesto contesto, Guid preventivoId, int versione, string? tipo, CancellationToken ct)
    {
        var normalizzato = tipo?.Trim().ToLowerInvariant();
        return normalizzato is not null && TipiIntervento.Tutti.Contains(normalizzato)
            ? repository.ConvertiAsync(contesto, preventivoId, versione, normalizzato, ct)
            : Task.FromResult<EsitoCommerciale>(new EsitoCommerciale.NonValida([ErroreCommerciale.TipoInterventoPreventivoNonValido]));
    }
}

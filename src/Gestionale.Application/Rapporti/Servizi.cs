using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Rapporti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Rapporti;

/// <summary>
/// Rapporti di controllo (ADR 0006). La bozza nasce precompilata dalle
/// anagrafiche e si modifica finché è in bozza; convalida e firma passano solo
/// senza errori bloccanti del tracciato. Con l'ambito "propri" il Tecnico vede e
/// compila solo i rapporti suoi o dei suoi interventi, e crea un rapporto solo
/// da un intervento che gli è assegnato.
/// </summary>
public sealed class ServizioRapporti(IRepositoryRapporti repository, TimeProvider orologio)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<Pagina<RiepilogoRapporto>> CercaAsync(
        Contesto contesto, AmbitoPermesso ambito, FiltroRapporti filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var normalizzato = filtro with { Stato = string.IsNullOrWhiteSpace(filtro.Stato) ? null : filtro.Stato.Trim().ToLowerInvariant() };
        return repository.CercaAsync(
            contesto, normalizzato, SoloTecnico(contesto, ambito),
            Math.Max(1, numeroPagina), Math.Clamp(dimensione, 1, DimensioneMassimaPagina), ct);
    }

    public Task<RapportoControllo?> RapportoAsync(Contesto contesto, AmbitoPermesso ambito, Guid rapportoId, CancellationToken ct) =>
        repository.RapportoAsync(contesto, rapportoId, SoloTecnico(contesto, ambito), ct);

    public async Task<IReadOnlyList<CampoTracciato>?> TracciatoAsync(Contesto contesto, string? modello, CancellationToken ct)
    {
        var normalizzato = modello?.Trim().ToLowerInvariant();
        return normalizzato is not null && ModelliRapporto.Tutti.Contains(normalizzato)
            ? await repository.TracciatoAsync(contesto, normalizzato, ct)
            : null;
    }

    public Task<EsitoRapporto> CreaAsync(Contesto contesto, AmbitoPermesso ambito, DatiNuovoRapporto dati, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(dati);

        var modello = dati.Modello?.Trim().ToLowerInvariant();
        var errori = new List<ErroreRapporto>();
        if (modello is null || !ModelliRapporto.Tutti.Contains(modello))
        {
            errori.Add(ErroreRapporto.ModelloRapportoNonValido);
        }

        if (dati.ImpiantoId is null)
        {
            errori.Add(ErroreRapporto.ImpiantoRapportoNonValido);
        }

        if (dati.GeneratoreId is null)
        {
            errori.Add(ErroreRapporto.GeneratoreRapportoNonValido);
        }

        if (ambito == AmbitoPermesso.Propri && dati.InterventoId is null)
        {
            errori.Add(ErroreRapporto.InterventoRapportoObbligatorio);
        }

        errori.AddRange(RegoleRapporti.ValidaDataControllo(dati.DataControllo, Oggi()));

        return errori.Count > 0
            ? Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida([.. errori], []))
            : repository.CreaAsync(contesto, dati with { Modello = modello }, SoloTecnico(contesto, ambito), ct);
    }

    public async Task<EsitoRapporto> AggiornaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid rapportoId, int versione, DatiBozzaRapporto bozza, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bozza);

        var soloTecnico = SoloTecnico(contesto, ambito);
        if (await repository.ModelloAsync(contesto, rapportoId, soloTecnico, ct) is not { } modello)
        {
            return new EsitoRapporto.NonTrovata();
        }

        var campi = await repository.TracciatoAsync(contesto, modello, ct);
        var (dati, errori, chiavi) = RegoleRapporti.NormalizzaDati(bozza.Dati, campi);
        var tutti = errori.Concat(RegoleRapporti.ValidaDataControllo(bozza.DataControllo, Oggi())).ToList();

        return tutti.Count > 0
            ? new EsitoRapporto.NonValida(tutti, chiavi)
            : await repository.AggiornaAsync(contesto, rapportoId, versione, bozza.DataControllo!.Value, dati, soloTecnico, ct);
    }

    public Task<EsitoRapporto> AssegnaBollinoAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid rapportoId, int versione, Guid? tipoBollinoId, CancellationToken ct) =>
        tipoBollinoId is { } tipo
            ? repository.AssegnaBollinoAsync(contesto, rapportoId, versione, tipo, SoloTecnico(contesto, ambito), ct)
            : Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida(ErroreRapporto.TipoBollinoNonValido));

    public Task<EsitoRapporto> LiberaBollinoAsync(Contesto contesto, AmbitoPermesso ambito, Guid rapportoId, int versione, CancellationToken ct) =>
        repository.LiberaBollinoAsync(contesto, rapportoId, versione, SoloTecnico(contesto, ambito), ct);

    /// <summary>
    /// Un rapporto firmato è un documento con valore legale: lo annulla solo chi
    /// ha il permesso di firma (puoFirmare); gli altri annullano bozze e convalidati.
    /// </summary>
    public Task<EsitoRapporto> CambiaStatoAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid rapportoId, int versione, PassaggioRapporto passaggio, string? motivo, bool puoFirmare,
        CancellationToken ct)
    {
        string? normalizzato = null;
        if (passaggio == PassaggioRapporto.Annullamento && (normalizzato = RegoleRapporti.MotivoAnnullamento(motivo)) is null)
        {
            return Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida(ErroreRapporto.MotivoAnnullamentoRapportoMancante));
        }

        return repository.CambiaStatoAsync(
            contesto, rapportoId, versione, passaggio, normalizzato, ancheFirmati: puoFirmare, SoloTecnico(contesto, ambito), ct);
    }

    /// <summary>La data di oggi in Italia, come la calcola il database.</summary>
    public DateOnly Oggi() => FusoItaliano.Oggi(orologio);

    private static Guid? SoloTecnico(Contesto contesto, AmbitoPermesso ambito) =>
        ambito == AmbitoPermesso.Propri ? contesto.UtenteId : null;
}

/// <summary>Registro dei bollini acquistati sul portale SIERT.</summary>
public sealed class ServizioBollini(IRepositoryBollini repository, TimeProvider orologio)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<IReadOnlyList<TipoBollino>> TipiAsync(Contesto contesto, CancellationToken ct) => repository.TipiAsync(contesto, ct);

    public Task<Pagina<Bollino>> CercaAsync(
        Contesto contesto, AmbitoPermesso ambito, FiltroBollini filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(filtro);

        var normalizzato = filtro with
        {
            Stato = string.IsNullOrWhiteSpace(filtro.Stato) ? null : filtro.Stato.Trim().ToLowerInvariant(),
            Codice = string.IsNullOrWhiteSpace(filtro.Codice) ? null : filtro.Codice.Trim(),
        };
        return repository.CercaAsync(
            contesto, normalizzato, ambito == AmbitoPermesso.Propri ? contesto.UtenteId : null,
            Math.Max(1, numeroPagina), Math.Clamp(dimensione, 1, DimensioneMassimaPagina), ct);
    }

    public Task<EsitoRapporto> RegistraAsync(Contesto contesto, DatiAcquistoBollini dati, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var (codici, errori, nonValidi) = RegoleRapporti.NormalizzaCodiciBollini(dati.Codici);
        var tutti = errori.ToList();
        if (dati.TipoBollinoId is null)
        {
            tutti.Add(ErroreRapporto.TipoBollinoNonValido);
        }

        if (dati.AcquistatoIl is not { } data || data > FusoItaliano.Oggi(orologio))
        {
            tutti.Add(ErroreRapporto.DataAcquistoBollinoNonValida);
        }

        var note = string.IsNullOrWhiteSpace(dati.Note) ? null : dati.Note.Trim();
        if (note?.Length > RegoleRapporti.LunghezzaMassimaNote)
        {
            tutti.Add(ErroreRapporto.NoteBollinoTroppoLunghe);
        }

        return tutti.Count > 0
            ? Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida(tutti, nonValidi))
            : repository.RegistraAsync(contesto, dati.TipoBollinoId!.Value, dati.AcquistatoIl!.Value, codici, note, ct);
    }

    public Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid bollinoId, int versione, string? motivo, CancellationToken ct) =>
        RegoleRapporti.MotivoAnnullamento(motivo) is { } m
            ? repository.AnnullaAsync(contesto, bollinoId, versione, m, ct)
            : Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida(ErroreRapporto.MotivoAnnullamentoRapportoMancante));
}

/// <summary>
/// Invii al SIERT: l'invio è assistito (nessuna API pubblica nota). Si prepara il
/// file, l'utente lo carica sul portale, poi registra gli esiti del report PDF.
/// </summary>
public sealed class ServizioInvii(IRepositoryInvii repository, TimeProvider orologio)
{
    public const int DimensioneMassimaPagina = 200;

    public Task<Pagina<RiepilogoInvio>> CercaAsync(Contesto contesto, int numeroPagina, int dimensione, CancellationToken ct) =>
        repository.CercaAsync(contesto, Math.Max(1, numeroPagina), Math.Clamp(dimensione, 1, DimensioneMassimaPagina), ct);

    public Task<InvioCatasto?> InvioAsync(Contesto contesto, Guid invioId, CancellationToken ct) => repository.InvioAsync(contesto, invioId, ct);

    public Task<FileInvio?> FileAsync(Contesto contesto, Guid invioId, CancellationToken ct) => repository.FileAsync(contesto, invioId, ct);

    public Task<EsitoRapporto> PreparaAsync(Contesto contesto, string? modello, CancellationToken ct)
    {
        var normalizzato = modello?.Trim().ToLowerInvariant();
        return normalizzato is not null && ModelliRapporto.Tutti.Contains(normalizzato)
            ? repository.PreparaAsync(contesto, normalizzato, orologio.GetUtcNow(), ct)
            : Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida(ErroreRapporto.ModelloRapportoNonValido));
    }

    public Task<EsitoRapporto> SegnaCaricatoAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct) =>
        repository.SegnaCaricatoAsync(contesto, invioId, versione, ct);

    public Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct) =>
        repository.AnnullaAsync(contesto, invioId, versione, ct);

    public Task<EsitoRapporto> RegistraEsitiAsync(
        Contesto contesto, Guid invioId, int versione, IReadOnlyList<EsitoRigaInvio>? esiti, CancellationToken ct)
    {
        var normalizzati = (esiti ?? []).Select(e => e with
        {
            Esito = e.Esito?.Trim().ToLowerInvariant(),
            Causa = string.IsNullOrWhiteSpace(e.Causa) ? null : e.Causa.Trim(),
        }).ToList();

        var errori = new List<ErroreRapporto>();
        if (normalizzati.Count == 0 || normalizzati.Any(e => e.RapportoControlloId is null || e.Esito is null || !EsitiRigaInvio.Registrabili.Contains(e.Esito)))
        {
            errori.Add(ErroreRapporto.EsitoInvioNonValido);
        }

        if (normalizzati.Any(e => e.Esito == EsitiRigaInvio.Scartato && e.Causa is null))
        {
            errori.Add(ErroreRapporto.CausaScartoMancante);
        }

        return errori.Count > 0
            ? Task.FromResult<EsitoRapporto>(new EsitoRapporto.NonValida([.. errori], []))
            : repository.RegistraEsitiAsync(contesto, invioId, versione, normalizzati, ct);
    }
}

/// <summary>Data di oggi nel fuso italiano, come la calcola il database.</summary>
internal static class FusoItaliano
{
    // Nome IANA su Linux e macOS, nome Windows come riserva.
    private static readonly TimeZoneInfo s_fuso = Trova();

    public static DateOnly Oggi(TimeProvider orologio) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(orologio.GetUtcNow(), s_fuso).DateTime);

    private static TimeZoneInfo Trova()
    {
        foreach (var id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var fuso))
            {
                return fuso;
            }
        }

        return TimeZoneInfo.Utc;
    }
}

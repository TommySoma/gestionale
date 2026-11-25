using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Scadenze;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Scadenze;

/// <summary>
/// Scadenze e richiami. Le scadenze le calcola il database; qui si leggono, si
/// corregge la data e si registrano i richiami. Con l'ambito "propri" (il
/// Tecnico, decisione del 25/11/2026) si leggono solo le scadenze degli impianti
/// dei propri interventi, senza telefono né richiami.
/// </summary>
public sealed class ServizioScadenze(IRepositoryScadenze repository, TimeProvider orologio)
{
    public const int DimensioneMassimaPagina = 200;

    public async Task<(Pagina<ScadenzaInArrivo>? Pagina, IReadOnlyList<ErroreScadenza> Errori)> InArrivoAsync(
        Contesto contesto, AmbitoPermesso ambito, FiltroScadenze filtro, int numeroPagina, int dimensione, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(filtro);

        if (RegoleScadenze.ValidaGiorni(filtro.Giorni) is { Count: > 0 } errori)
        {
            return (null, errori);
        }

        numeroPagina = Math.Max(1, numeroPagina);
        dimensione = Math.Clamp(dimensione, 1, DimensioneMassimaPagina);

        var normalizzato = filtro with
        {
            Tipo = Normalizza(filtro.Tipo),
            StatoRichiamo = Normalizza(filtro.StatoRichiamo),
            TecnicoUtenteId = SoloTecnico(contesto, ambito),
        };

        var pagina = await repository.InArrivoAsync(contesto, normalizzato, numeroPagina, dimensione, ct);
        return (ambito == AmbitoPermesso.Propri ? SenzaRichiami(pagina) : pagina, []);
    }

    public async Task<Scadenza?> ScadenzaAsync(Contesto contesto, AmbitoPermesso ambito, Guid scadenzaId, CancellationToken ct)
    {
        var scadenza = await repository.ScadenzaAsync(contesto, scadenzaId, SoloTecnico(contesto, ambito), ct);
        if (scadenza is not null && ambito == AmbitoPermesso.Propri)
        {
            scadenza.Richiami = [];
        }

        return scadenza;
    }

    public async Task<IReadOnlyList<Scadenza>?> DellImpiantoAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, CancellationToken ct)
    {
        var scadenze = await repository.DellImpiantoAsync(contesto, impiantoId, SoloTecnico(contesto, ambito), ct);
        if (scadenze is not null && ambito == AmbitoPermesso.Propri)
        {
            foreach (var scadenza in scadenze)
            {
                scadenza.Richiami = [];
            }
        }

        return scadenze;
    }

    public Task<EsitoScadenza> CorreggiDataAsync(Contesto contesto, Guid scadenzaId, int versione, DateOnly? data, CancellationToken ct) =>
        RegoleScadenze.ValidaData(data, Oggi()) is { Count: > 0 } errori
            ? Task.FromResult<EsitoScadenza>(new EsitoScadenza.NonValida(errori))
            : repository.CorreggiDataAsync(contesto, scadenzaId, versione, data!.Value, ct);

    public Task<EsitoScadenza> RegistraRichiamoAsync(Contesto contesto, Guid scadenzaId, int versione, DatiRichiamo dati, CancellationToken ct)
    {
        var (normalizzati, errori) = RegoleScadenze.Valida(dati, Oggi());
        return errori.Count > 0
            ? Task.FromResult<EsitoScadenza>(new EsitoScadenza.NonValida(errori))
            : repository.RegistraRichiamoAsync(contesto, scadenzaId, versione, normalizzati, ct);
    }

    /// <summary>La data di oggi in Italia, come la calcola il database.</summary>
    public DateOnly Oggi() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(orologio.GetUtcNow(), FusoItaliano.Valore).DateTime);

    private static Guid? SoloTecnico(Contesto contesto, AmbitoPermesso ambito) =>
        ambito == AmbitoPermesso.Propri ? contesto.UtenteId : null;

    private static string? Normalizza(string? valore) =>
        string.IsNullOrWhiteSpace(valore) ? null : valore.Trim().ToLowerInvariant();

    private static Pagina<ScadenzaInArrivo> SenzaRichiami(Pagina<ScadenzaInArrivo> pagina) =>
        pagina with
        {
            Elementi = [.. pagina.Elementi.Select(s => new ScadenzaInArrivo
            {
                ScadenzaId = s.ScadenzaId,
                ImpiantoId = s.ImpiantoId,
                Tipo = s.Tipo,
                DataScadenza = s.DataScadenza,
                GiorniMancanti = s.GiorniMancanti,
                StatoRichiamo = s.StatoRichiamo,
                Versione = s.Versione,
                ClienteId = s.ClienteId,
                Cliente = s.Cliente,
                Indirizzo = s.Indirizzo,
                Comune = s.Comune,
                Provincia = s.Provincia,
            })],
        };

    private static class FusoItaliano
    {
        // Nome IANA su Linux e macOS, nome Windows come riserva.
        public static TimeZoneInfo Valore { get; } = Trova();

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
}

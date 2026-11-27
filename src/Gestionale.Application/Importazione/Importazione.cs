using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Importazione;

namespace Gestionale.Application.Importazione;

/// <summary>Conteggi e segnalazioni di un'importazione; Importata dice se i dati sono stati scritti.</summary>
public sealed record EsitoImportazione(
    bool Importata,
    int Righe,
    int ClientiNuovi,
    int ClientiEsistenti,
    int SediNuove,
    int ImpiantiNuovi,
    int ImpiantiSaltati,
    int Generatori,
    IReadOnlyList<SegnalazioneRiga> Errori,
    IReadOnlyList<SegnalazioneRiga> Avvisi)
{
    public static EsitoImportazione SoloErrori(IReadOnlyList<SegnalazioneRiga> errori, IReadOnlyList<SegnalazioneRiga> avvisi) =>
        new(false, 0, 0, 0, 0, 0, 0, 0, errori, avvisi);
}

public interface IRepositoryImportazione
{
    /// <summary>
    /// Risolve le voci, riconosce i dati già presenti e scrive tutto in una transazione;
    /// conferma la transazione solo con conferma = true e nessun errore.
    /// </summary>
    Task<EsitoImportazione> ImportaAsync(Contesto contesto, IReadOnlyList<RigaImportata> righe, bool conferma, CancellationToken ct);
}

/// <summary>
/// Importazione di clienti, sedi e impianti da CSV (ADR 0007): tutto o niente.
/// L'anteprima fa lo stesso lavoro dell'importazione e poi lo annulla, così mostra
/// esattamente cosa succederà.
/// </summary>
public sealed class ServizioImportazione(IRepositoryImportazione repository)
{
    public async Task<EsitoImportazione> EseguiAsync(Contesto contesto, byte[] contenuto, bool conferma, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        var analisi = RegoleImportazione.Analizza(contenuto);

        // Errori di forma su tutto il file (intestazione, dimensione): non c'è nulla da controllare nel database.
        if (analisi.Errori.Any(e => e.Riga == 0))
        {
            return EsitoImportazione.SoloErrori(analisi.Errori, analisi.Avvisi);
        }

        // Anche con errori di riga si controllano le altre contro il database, così l'anteprima li mostra tutti insieme.
        var esito = await repository.ImportaAsync(contesto, analisi.Righe, conferma && analisi.Errori.Count == 0, ct);
        return esito with
        {
            Importata = esito.Importata && analisi.Errori.Count == 0,
            Righe = analisi.Righe.Count + analisi.Errori.Select(e => e.Riga).Distinct().Count(),
            Errori = [.. analisi.Errori.Concat(esito.Errori).OrderBy(e => e.Riga)],
            Avvisi = [.. analisi.Avvisi.Concat(esito.Avvisi).OrderBy(e => e.Riga)],
        };
    }
}

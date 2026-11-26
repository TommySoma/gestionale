using System.Text.Json.Nodes;
using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Rapporti;

namespace Gestionale.Application.Rapporti;

/// <summary>
/// Persistenza dei rapporti di controllo; ogni metodo è una transazione sotto
/// Row-Level Security. Con soloTecnico valorizzato si vedono e si toccano solo i
/// rapporti di quel tecnico o dei suoi interventi.
/// </summary>
public interface IRepositoryRapporti
{
    Task<Pagina<RiepilogoRapporto>> CercaAsync(
        Contesto contesto, FiltroRapporti filtro, Guid? soloTecnico, int numeroPagina, int dimensione, CancellationToken ct);

    /// <summary>La scheda con l'esito di norma.valida_rapporto.</summary>
    Task<RapportoControllo?> RapportoAsync(Contesto contesto, Guid rapportoId, Guid? soloTecnico, CancellationToken ct);

    Task<string?> ModelloAsync(Contesto contesto, Guid rapportoId, Guid? soloTecnico, CancellationToken ct);

    /// <summary>Colonne del tracciato SIERT in vigore oggi per il modello.</summary>
    Task<IReadOnlyList<CampoTracciato>> TracciatoAsync(Contesto contesto, string modello, CancellationToken ct);

    /// <summary>Bozza precompilata dalle anagrafiche; con soloTecnico l'intervento è obbligatorio e deve essere suo.</summary>
    Task<EsitoRapporto> CreaAsync(Contesto contesto, DatiNuovoRapporto dati, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoRapporto> AggiornaAsync(
        Contesto contesto, Guid rapportoId, int versione, DateOnly dataControllo, JsonObject dati, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoRapporto> AssegnaBollinoAsync(Contesto contesto, Guid rapportoId, int versione, Guid tipoBollinoId, Guid? soloTecnico, CancellationToken ct);

    Task<EsitoRapporto> LiberaBollinoAsync(Contesto contesto, Guid rapportoId, int versione, Guid? soloTecnico, CancellationToken ct);

    /// <summary>Con ancheFirmati l'annullamento vale anche per i rapporti firmati o scartati.</summary>
    Task<EsitoRapporto> CambiaStatoAsync(
        Contesto contesto, Guid rapportoId, int versione, PassaggioRapporto passaggio, string? motivo, bool ancheFirmati, Guid? soloTecnico,
        CancellationToken ct);
}

/// <summary>Registro dei bollini; con soloTecnico solo quelli sui rapporti del tecnico.</summary>
public interface IRepositoryBollini
{
    Task<IReadOnlyList<TipoBollino>> TipiAsync(Contesto contesto, CancellationToken ct);

    Task<Pagina<Bollino>> CercaAsync(Contesto contesto, FiltroBollini filtro, Guid? soloTecnico, int numeroPagina, int dimensione, CancellationToken ct);

    /// <summary>Registra in una transazione tutti i codici di un acquisto, o nessuno.</summary>
    Task<EsitoRapporto> RegistraAsync(
        Contesto contesto, Guid tipoBollinoId, DateOnly acquistatoIl, IReadOnlyList<string> codici, string? note, CancellationToken ct);

    /// <summary>Annulla un bollino disponibile (smarrito o registrato per errore).</summary>
    Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid bollinoId, int versione, string motivo, CancellationToken ct);
}

/// <summary>Invii al catasto: file di import massivo, caricamento sul portale ed esiti del report.</summary>
public interface IRepositoryInvii
{
    Task<Pagina<RiepilogoInvio>> CercaAsync(Contesto contesto, int numeroPagina, int dimensione, CancellationToken ct);

    Task<InvioCatasto?> InvioAsync(Contesto contesto, Guid invioId, CancellationToken ct);

    Task<FileInvio?> FileAsync(Contesto contesto, Guid invioId, CancellationToken ct);

    /// <summary>
    /// Mette nel file i rapporti firmati o scartati del modello che non sono già in
    /// un invio vivo e non hanno errori bloccanti; gli altri li elenca come esclusi.
    /// </summary>
    Task<EsitoRapporto> PreparaAsync(Contesto contesto, string modello, DateTimeOffset momento, CancellationToken ct);

    Task<EsitoRapporto> SegnaCaricatoAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct);

    Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct);

    Task<EsitoRapporto> RegistraEsitiAsync(Contesto contesto, Guid invioId, int versione, IReadOnlyList<EsitoRigaInvio> esiti, CancellationToken ct);
}

using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Domain.Documenti;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Documenti;

public sealed class Documento
{
    public Guid DocumentoId { get; init; }

    public string Categoria { get; init; } = "";

    public string NomeOriginale { get; init; } = "";

    public string TipoMime { get; init; } = "";

    public long Dimensione { get; init; }

    public string? Descrizione { get; init; }

    public DateTime CreatoIl { get; init; }

    public string? CreatoDa { get; init; }

    public int Versione { get; init; }
}

/// <summary>Riga da salvare dopo che il file è già sul disco.</summary>
public sealed record NuovoDocumento(
    Guid DocumentoId, string Entita, Guid EntitaId, string Categoria, string NomeOriginale, string TipoMime,
    long Dimensione, byte[] Sha256, string Percorso, string? Descrizione);

/// <summary>Esito del salvataggio sul disco: dimensione, impronta e primi byte per riconoscere il tipo.</summary>
public sealed record FileSalvato(string Percorso, long Dimensione, byte[] Sha256, byte[] Inizio);

/// <summary>File aperto per lo scaricamento.</summary>
public sealed record ContenutoDocumento(Stream Flusso, string TipoMime, string NomeOriginale);

/// <summary>Dove stanno i file: una cartella sul disco del server, in un volume.</summary>
public interface IArchivioDocumenti
{
    /// <summary>
    /// Copia il flusso nel file del documento calcolando dimensione e SHA-256. Se supera
    /// <paramref name="massimo"/> byte si ferma, cancella quanto scritto e restituisce null.
    /// </summary>
    Task<FileSalvato?> SalvaAsync(Guid aziendaId, Guid documentoId, Stream contenuto, long massimo, CancellationToken ct);

    Stream Apri(string percorso);

    /// <summary>Toglie un file appena salvato quando il documento non viene registrato.</summary>
    void Scarta(string percorso);
}

/// <summary>Metadati dei documenti; ogni metodo è una transazione sotto Row-Level Security.</summary>
public interface IRepositoryDocumenti
{
    Task<IReadOnlyList<Documento>> ElencoAsync(Contesto contesto, string entita, Guid entitaId, CancellationToken ct);

    /// <summary>Documento non eliminato dell'entità indicata, con il percorso del file.</summary>
    Task<(Documento Documento, string Percorso)?> DocumentoAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, CancellationToken ct);

    Task<Documento> CreaAsync(Contesto contesto, NuovoDocumento documento, CancellationToken ct);

    /// <summary>Eliminazione logica; false se non esiste o la versione non coincide.</summary>
    Task<bool?> EliminaAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, int versione, CancellationToken ct);
}

public abstract record EsitoDocumento
{
    public sealed record Caricato(Documento Documento) : EsitoDocumento;

    public sealed record NonValido(ErroreDocumento Errore) : EsitoDocumento;

    public sealed record NonTrovato : EsitoDocumento;

    public sealed record VersioneSuperata : EsitoDocumento;

    /// <summary>Il permesso c'è solo sui propri impianti: eliminare spetta all'ufficio.</summary>
    public sealed record Vietato : EsitoDocumento;

    public sealed record Eliminato : EsitoDocumento;
}

/// <summary>
/// Foto e documenti degli impianti. Si vedono e si caricano sugli impianti che
/// l'utente vede: con l'ambito "propri" (il Tecnico) quelli dei propri interventi.
/// Eliminare, solo logicamente, spetta a chi ha l'ambito su tutto.
/// </summary>
public sealed class ServizioDocumenti(IRepositoryDocumenti repository, IArchivioDocumenti archivio, IRepositoryImpianti impianti)
{
    public const string EntitaImpianto = "impianto";

    public async Task<IReadOnlyList<Documento>?> ElencoAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, CancellationToken ct) =>
        await ImpiantoVisibileAsync(contesto, ambito, impiantoId, ct)
            ? await repository.ElencoAsync(contesto, EntitaImpianto, impiantoId, ct)
            : null;

    public async Task<ContenutoDocumento?> ContenutoAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid documentoId, CancellationToken ct)
    {
        if (!await ImpiantoVisibileAsync(contesto, ambito, impiantoId, ct)
            || await repository.DocumentoAsync(contesto, EntitaImpianto, impiantoId, documentoId, ct) is not { } trovato)
        {
            return null;
        }

        return new ContenutoDocumento(archivio.Apri(trovato.Percorso), trovato.Documento.TipoMime, trovato.Documento.NomeOriginale);
    }

    public async Task<EsitoDocumento> CaricaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, string? nome, string? descrizione, Stream contenuto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var descrizionePulita = string.IsNullOrWhiteSpace(descrizione) ? null : descrizione.Trim();
        if (descrizionePulita?.Length > RegoleDocumenti.LunghezzaMassimaDescrizione)
        {
            return new EsitoDocumento.NonValido(ErroreDocumento.DescrizioneTroppoLunga);
        }

        if (!await ImpiantoVisibileAsync(contesto, ambito, impiantoId, ct))
        {
            return new EsitoDocumento.NonTrovato();
        }

        var documentoId = Guid.CreateVersion7();
        if (await archivio.SalvaAsync(contesto.AziendaId, documentoId, contenuto, RegoleDocumenti.DimensioneMassima, ct) is not { } file)
        {
            return new EsitoDocumento.NonValido(ErroreDocumento.FileTroppoGrande);
        }

        var errore = file.Dimensione == 0 ? ErroreDocumento.FileVuoto
            : RegoleDocumenti.TipoDaFirma(file.Inizio) is null ? ErroreDocumento.TipoNonAmmesso
            : (ErroreDocumento?)null;
        if (errore is { } e)
        {
            archivio.Scarta(file.Percorso);
            return new EsitoDocumento.NonValido(e);
        }

        var tipo = RegoleDocumenti.TipoDaFirma(file.Inizio)!;
        try
        {
            var documento = await repository.CreaAsync(contesto, new NuovoDocumento(
                documentoId, EntitaImpianto, impiantoId, RegoleDocumenti.Categoria(tipo), RegoleDocumenti.NomeSicuro(nome, tipo), tipo,
                file.Dimensione, file.Sha256, file.Percorso, descrizionePulita), ct);
            return new EsitoDocumento.Caricato(documento);
        }
        catch
        {
            // Senza la riga nel database il file non serve: niente file orfani.
            archivio.Scarta(file.Percorso);
            throw;
        }
    }

    public async Task<EsitoDocumento> EliminaAsync(
        Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, Guid documentoId, int versione, CancellationToken ct)
    {
        if (ambito == AmbitoPermesso.Propri)
        {
            return new EsitoDocumento.Vietato();
        }

        return await repository.EliminaAsync(contesto, EntitaImpianto, impiantoId, documentoId, versione, ct) switch
        {
            null => new EsitoDocumento.NonTrovato(),
            false => new EsitoDocumento.VersioneSuperata(),
            true => new EsitoDocumento.Eliminato(),
        };
    }

    // Con l'ambito "propri" l'impianto deve avere un intervento dell'utente; in ogni caso deve essere visibile.
    private async Task<bool> ImpiantoVisibileAsync(Contesto contesto, AmbitoPermesso ambito, Guid impiantoId, CancellationToken ct) =>
        (ambito == AmbitoPermesso.Tutti || await impianti.AssegnatoAsync(contesto, impiantoId, ct))
        && await impianti.ImpiantoAsync(contesto, impiantoId, ct) is not null;
}

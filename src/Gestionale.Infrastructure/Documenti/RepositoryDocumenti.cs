using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Documenti;
using Npgsql;

namespace Gestionale.Infrastructure.Documenti;

/// <summary>Metadati dei documenti con Dapper (ADR 0003), sotto Row-Level Security.</summary>
public sealed class RepositoryDocumenti(Database.Database db) : IRepositoryDocumenti
{
    private const string Campi = """
        d.documento_id, d.categoria, d.nome_originale, d.tipo_mime, d.dimensione, d.descrizione,
        d.creato_il, u.nome || ' ' || u.cognome AS creato_da, d.versione
        """;

    public Task<IReadOnlyList<Documento>> ElencoAsync(Contesto contesto, string entita, Guid entitaId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) => (IReadOnlyList<Documento>)[.. await c.QueryAsync<Documento>(Comando(
            $"""
            SELECT {Campi}
            FROM documenti.documenti d
            LEFT JOIN sec.utenti u ON u.utente_id = d.creato_da
            WHERE d.entita = @entita AND d.entita_id = @entitaId AND d.eliminato_il IS NULL
            ORDER BY d.creato_il DESC, d.documento_id DESC
            """,
            new { entita, entitaId }, t, ct))], ct);

    public Task<(Documento Documento, string Percorso)?> DocumentoAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var riga = await c.QuerySingleOrDefaultAsync<RigaConPercorso>(Comando(
                $"""
                SELECT {Campi}, d.percorso
                FROM documenti.documenti d
                LEFT JOIN sec.utenti u ON u.utente_id = d.creato_da
                WHERE d.documento_id = @documentoId AND d.entita = @entita AND d.entita_id = @entitaId AND d.eliminato_il IS NULL
                """,
                new { documentoId, entita, entitaId }, t, ct));
            return riga is null ? ((Documento, string)?)null : (riga.Documento(), riga.Percorso);
        }, ct);

    public Task<Documento> CreaAsync(Contesto contesto, NuovoDocumento documento, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            await c.ExecuteAsync(Comando(
                """
                INSERT INTO documenti.documenti (documento_id, azienda_id, entita, entita_id, categoria, nome_originale, tipo_mime,
                                                 dimensione, sha256, percorso, descrizione, creato_da)
                VALUES (@DocumentoId, @aziendaId, @Entita, @EntitaId, @Categoria, @NomeOriginale, @TipoMime,
                        @Dimensione, @Sha256, @Percorso, @Descrizione, @utenteId)
                """,
                new
                {
                    documento.DocumentoId,
                    aziendaId = contesto.AziendaId,
                    documento.Entita,
                    documento.EntitaId,
                    documento.Categoria,
                    documento.NomeOriginale,
                    documento.TipoMime,
                    documento.Dimensione,
                    documento.Sha256,
                    documento.Percorso,
                    documento.Descrizione,
                    utenteId = contesto.UtenteId,
                },
                t, ct));

            return await c.QuerySingleAsync<Documento>(Comando(
                $"SELECT {Campi} FROM documenti.documenti d LEFT JOIN sec.utenti u ON u.utente_id = d.creato_da WHERE d.documento_id = @id",
                new { id = documento.DocumentoId }, t, ct));
        }, ct);

    public Task<bool?> EliminaAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, int versione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var attuale = await c.QuerySingleOrDefaultAsync<int?>(Comando(
                """
                SELECT versione FROM documenti.documenti
                WHERE documento_id = @documentoId AND entita = @entita AND entita_id = @entitaId AND eliminato_il IS NULL
                FOR UPDATE
                """,
                new { documentoId, entita, entitaId }, t, ct));
            if (attuale is null)
            {
                return (bool?)null;
            }

            if (attuale != versione)
            {
                return false;
            }

            await c.ExecuteAsync(Comando(
                """
                UPDATE documenti.documenti
                SET eliminato_il = now(), eliminato_da = @utenteId, modificato_da = @utenteId
                WHERE documento_id = @documentoId
                """,
                new { documentoId, utenteId = contesto.UtenteId }, t, ct));
            return true;
        }, ct);

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class RigaConPercorso
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

        public string Percorso { get; init; } = "";

        public Documento Documento() => new()
        {
            DocumentoId = DocumentoId,
            Categoria = Categoria,
            NomeOriginale = NomeOriginale,
            TipoMime = TipoMime,
            Dimensione = Dimensione,
            Descrizione = Descrizione,
            CreatoIl = CreatoIl,
            CreatoDa = CreatoDa,
            Versione = Versione,
        };
    }
}

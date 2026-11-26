using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Rapporti;
using Gestionale.Domain.Rapporti;
using Npgsql;

namespace Gestionale.Infrastructure.Rapporti;

/// <summary>Registro dei bollini con Dapper; i passaggi di stato li controlla il trigger della migrazione 0010.</summary>
public sealed class RepositoryBollini(Database.Database db) : IRepositoryBollini
{
    private const string Oggi = "(now() AT TIME ZONE 'Europe/Rome')::date";

    public Task<IReadOnlyList<TipoBollino>> TipiAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, async (c, t) => (IReadOnlyList<TipoBollino>)[.. await c.QueryAsync<TipoBollino>(Comando(
            $"""
            SELECT t.tipo_bollino_id, t.codice, t.categoria, t.nome, t.importo,
                   (SELECT count(*) FROM norma.bollini b
                    WHERE b.tipo_bollino_id = t.tipo_bollino_id AND b.stato = 'disponibile')::integer AS disponibili
            FROM norma.tipi_bollino t
            JOIN sec.aziende a ON a.regione = t.regione AND a.azienda_id = @aziendaId
            WHERE t.valido_dal <= {Oggi} AND (t.valido_al IS NULL OR t.valido_al >= {Oggi})
            ORDER BY t.categoria, t.fascia_da NULLS FIRST
            """,
            new { aziendaId = contesto.AziendaId }, t, ct))], ct);

    public Task<Pagina<Bollino>> CercaAsync(
        Contesto contesto, FiltroBollini filtro, Guid? soloTecnico, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            // Il Tecnico vede i bollini dei rapporti suoi o dei suoi interventi.
            const string Condizioni = """
                FROM norma.bollini b
                JOIN norma.tipi_bollino t ON t.tipo_bollino_id = b.tipo_bollino_id
                LEFT JOIN norma.rapporti_controllo r ON r.bollino_id = b.bollino_id
                WHERE (@stato::text IS NULL OR b.stato = @stato)
                  AND (@tipo::uuid IS NULL OR b.tipo_bollino_id = @tipo)
                  AND (@codice::text IS NULL OR b.codice LIKE @codice)
                  AND (@soloTecnico::uuid IS NULL OR r.tecnico_id = @soloTecnico
                       OR EXISTS (SELECT 1 FROM lavoro.interventi x WHERE x.intervento_id = r.intervento_id AND x.tecnico_utente_id = @soloTecnico))
                """;

            var parametri = new
            {
                stato = filtro.Stato,
                tipo = filtro.TipoBollinoId,
                // Ricerca per inizio del codice.
                codice = filtro.Codice is null ? null : Commerciale.RepositoryListino.EscapeLike(filtro.Codice) + "%",
                soloTecnico,
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + Condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<Bollino>(Comando(
                $"""
                SELECT b.bollino_id, b.codice, b.tipo_bollino_id, t.nome AS tipo_bollino, t.categoria, t.importo,
                       b.acquistato_il, b.stato, r.rapporto_controllo_id, b.note, b.versione
                {Condizioni}
                ORDER BY b.acquistato_il, b.codice
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<Bollino>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<EsitoRapporto> RegistraAsync(
        Contesto contesto, Guid tipoBollinoId, DateOnly acquistatoIl, IReadOnlyList<string> codici, string? note, CancellationToken ct) =>
        Esegui<EsitoRapporto>(contesto, async (c, t) =>
        {
            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    $"""
                    SELECT EXISTS (
                        SELECT 1 FROM norma.tipi_bollino t
                        JOIN sec.aziende a ON a.regione = t.regione AND a.azienda_id = @aziendaId
                        WHERE t.tipo_bollino_id = @tipoBollinoId AND t.valido_dal <= {Oggi} AND (t.valido_al IS NULL OR t.valido_al >= {Oggi}))
                    """,
                    new { tipoBollinoId, aziendaId = contesto.AziendaId }, t, ct)))
            {
                return new EsitoRapporto.NonValida(ErroreRapporto.TipoBollinoNonValido);
            }

            var array = codici.ToArray();
            var esistenti = (await c.QueryAsync<string>(Comando(
                "SELECT codice FROM norma.bollini WHERE codice = ANY (@array) ORDER BY codice", new { array }, t, ct))).ToList();
            if (esistenti.Count > 0)
            {
                return new EsitoRapporto.NonValida([ErroreRapporto.CodiceBollinoGiaRegistrato], esistenti);
            }

            var numero = await c.ExecuteAsync(Comando(
                """
                INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il, note, creato_da)
                SELECT @aziendaId, @tipoBollinoId, codice, @acquistatoIl, @note, @utenteId
                FROM unnest(@array) AS codice
                """,
                new { aziendaId = contesto.AziendaId, tipoBollinoId, acquistatoIl, note, array, utenteId = contesto.UtenteId }, t, ct));

            return new EsitoRapporto.BolliniRegistrati(numero);
        }, ct);

    public Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid bollinoId, int versione, string motivo, CancellationToken ct) =>
        Esegui<EsitoRapporto>(contesto, async (c, t) =>
        {
            var attuale = await c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
                "SELECT stato, versione FROM norma.bollini WHERE bollino_id = @bollinoId FOR UPDATE", new { bollinoId }, t, ct));

            if (attuale is null)
            {
                return new EsitoRapporto.NonTrovata();
            }

            if (attuale.Versione != versione)
            {
                return new EsitoRapporto.VersioneSuperata();
            }

            if (attuale.Stato != StatiBollino.Disponibile)
            {
                return new EsitoRapporto.StatoNonConsentito("bollino_" + attuale.Stato);
            }

            var nuova = await c.ExecuteScalarAsync<int>(Comando(
                """
                UPDATE norma.bollini SET stato = 'annullato', note = @motivo, modificato_da = @utenteId
                WHERE bollino_id = @bollinoId
                RETURNING versione
                """,
                new { bollinoId, motivo, utenteId = contesto.UtenteId }, t, ct));

            return new EsitoRapporto.Riuscita(bollinoId, nuova);
        }, ct);

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }
    }
}

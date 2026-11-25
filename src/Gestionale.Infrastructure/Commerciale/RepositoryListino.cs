using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Commerciale;
using Gestionale.Domain.Commerciale;
using Gestionale.Infrastructure.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Commerciale;

/// <summary>
/// Listino con Dapper (ADR 0003). Il prezzo in vigore è quello con la validità più
/// recente non successiva a oggi (nel fuso italiano); lo storico è in sola aggiunta.
/// </summary>
public sealed class RepositoryListino(Database.Database db) : IRepositoryListino
{
    private const string ViolazioneUnicita = "23505";

    private const string Oggi = "(now() AT TIME ZONE 'Europe/Rome')::date";

    private const string Colonne = $"""
        v.voce_id, v.codice, v.descrizione, v.tipo, v.unita, v.aliquota_iva, v.attiva, v.note, v.versione,
        p.prezzo, p.valido_dal AS prezzo_dal
        FROM commerciale.listino_voci v
        LEFT JOIN LATERAL (
            SELECT lp.prezzo, lp.valido_dal FROM commerciale.listino_prezzi lp
            WHERE lp.voce_id = v.voce_id AND lp.valido_dal <= {Oggi}
            ORDER BY lp.valido_dal DESC LIMIT 1
        ) p ON true
        """;

    public Task<Pagina<VoceListino>> CercaAsync(
        Contesto contesto, string? testo, bool soloAttive, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            const string Condizioni = """
                WHERE (NOT @soloAttive OR v.attiva)
                  AND (@modello::text IS NULL OR lower(v.codice) LIKE @modello OR lower(v.descrizione) LIKE @modello)
                """;

            var parametri = new
            {
                soloAttive,
                modello = testo is null ? null : "%" + EscapeLike(testo.ToLowerInvariant()) + "%",
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando(
                "SELECT count(*) FROM commerciale.listino_voci v " + Condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<VoceListino>(Comando(
                $"""
                SELECT {Colonne}
                {Condizioni}
                ORDER BY lower(v.codice), v.voce_id
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<VoceListino>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<VoceListino?> VoceAsync(Contesto contesto, Guid voceId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var voce = await c.QuerySingleOrDefaultAsync<VoceListino>(Comando(
                $"SELECT {Colonne} WHERE v.voce_id = @voceId", new { voceId }, t, ct));
            if (voce is not null)
            {
                voce.Prezzi = [.. await c.QueryAsync<PrezzoListino>(Comando(
                    """
                    SELECT prezzo_id, prezzo, valido_dal, creato_il FROM commerciale.listino_prezzi
                    WHERE voce_id = @voceId ORDER BY valido_dal DESC
                    """,
                    new { voceId }, t, ct))];
            }

            return voce;
        }, ct);

    public Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiVoce dati, DatiPrezzo? prezzo, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO commerciale.listino_voci (azienda_id, codice, descrizione, tipo, unita, aliquota_iva, attiva, note, creato_da)
                VALUES (@aziendaId, @Codice, @Descrizione, @Tipo, @Unita, @AliquotaIva, @Attiva, @Note, @utenteId)
                RETURNING voce_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, utenteId = contesto.UtenteId }), t, ct));

            if (prezzo is not null)
            {
                await InserisciPrezzoAsync(c, t, contesto, riga.Id, prezzo, ct);
            }

            return new EsitoCommerciale.Riuscita(riga.Id, riga.Versione);
        }, ct);

    public Task<EsitoCommerciale> AggiornaAsync(Contesto contesto, Guid voceId, int versione, DatiVoce dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE commerciale.listino_voci
                SET codice = @Codice, descrizione = @Descrizione, tipo = @Tipo, unita = @Unita,
                    aliquota_iva = @AliquotaIva, attiva = @Attiva, note = @Note, modificato_da = @utenteId
                WHERE voce_id = @voceId AND versione = @versione
                RETURNING voce_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { voceId, versione, utenteId = contesto.UtenteId }), t, ct));

            if (riga is not null)
            {
                return new EsitoCommerciale.Riuscita(riga.Id, riga.Versione);
            }

            return await EsisteAsync(c, t, voceId, ct) ? new EsitoCommerciale.VersioneSuperata() : new EsitoCommerciale.NonTrovata();
        }, ct);

    public Task<EsitoCommerciale> AggiungiPrezzoAsync(Contesto contesto, Guid voceId, DatiPrezzo prezzo, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var versione = await c.QuerySingleOrDefaultAsync<int?>(Comando(
                "SELECT versione FROM commerciale.listino_voci WHERE voce_id = @voceId", new { voceId }, t, ct));
            if (versione is not { } v)
            {
                return new EsitoCommerciale.NonTrovata();
            }

            await InserisciPrezzoAsync(c, t, contesto, voceId, prezzo, ct);
            return new EsitoCommerciale.Riuscita(voceId, v);
        }, ct);

    private static Task InserisciPrezzoAsync(NpgsqlConnection c, NpgsqlTransaction t, Contesto contesto, Guid voceId, DatiPrezzo prezzo, CancellationToken ct) =>
        c.ExecuteAsync(Comando(
            """
            INSERT INTO commerciale.listino_prezzi (azienda_id, voce_id, prezzo, valido_dal, creato_da)
            VALUES (@aziendaId, @voceId, @prezzo, coalesce(@validoDal::date, (now() AT TIME ZONE 'Europe/Rome')::date), @utenteId)
            """,
            new { aziendaId = contesto.AziendaId, voceId, prezzo = prezzo.Prezzo, validoDal = prezzo.ValidoDal, utenteId = contesto.UtenteId }, t, ct));

    private static Task<bool> EsisteAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid voceId, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(Comando(
            "SELECT EXISTS (SELECT 1 FROM commerciale.listino_voci WHERE voce_id = @voceId)", new { voceId }, t, ct));

    private async Task<EsitoCommerciale> Scrivi(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<EsitoCommerciale>> lavoro, CancellationToken ct)
    {
        try
        {
            return await Esegui(contesto, lavoro, ct);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita && e.ConstraintName == "uq_listino_voci_codice")
        {
            return new EsitoCommerciale.Duplicata(ErroreCommerciale.CodiceVoceInUso);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita && e.ConstraintName == "uq_listino_prezzi_voce_data")
        {
            return new EsitoCommerciale.Duplicata(ErroreCommerciale.PrezzoGiaPresente);
        }
    }

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    internal static string EscapeLike(string testo) =>
        testo.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class RigaScritta
    {
        public Guid Id { get; init; }

        public int Versione { get; init; }
    }
}

using System.Text.Json;
using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Commerciale;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Commerciale;
using Gestionale.Infrastructure.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Commerciale;

/// <summary>
/// Preventivi con Dapper (ADR 0003). Numerazione, totali, blocco dopo l'emissione
/// e conversione stanno nel database (migrazione 0009); qui si blocca la riga, si
/// controllano versione, stato e riferimenti e si risponde con un esito chiaro.
/// </summary>
public sealed class RepositoryPreventivi(Database.Database db) : IRepositoryPreventivi
{
    private const string NomeCliente = "CASE WHEN c.tipo = 'privato' THEN c.cognome || ' ' || c.nome ELSE c.denominazione END";

    private const string Scaduto = "(p.stato = 'emesso' AND p.scade_il < (now() AT TIME ZONE 'Europe/Rome')::date)";

    private const string Origine = """
        FROM commerciale.preventivi p
        JOIN anagrafica.clienti c ON c.cliente_id = p.cliente_id
        LEFT JOIN anagrafica.sedi s ON s.sede_id = p.sede_id
        LEFT JOIN impianti.impianti i ON i.impianto_id = p.impianto_id
        LEFT JOIN impianti.tipi_impianto ti ON ti.tipo_impianto_id = i.tipo_impianto_id
        """;

    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public Task<Pagina<RiepilogoPreventivo>> CercaAsync(
        Contesto contesto, FiltroPreventivi filtro, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var condizioni = $"""
                {Origine}
                WHERE (@stato::text IS NULL OR (@stato = 'scaduto' AND {Scaduto}) OR p.stato = @stato)
                  AND (@cliente::uuid IS NULL OR p.cliente_id = @cliente)
                  AND (@impianto::uuid IS NULL OR p.impianto_id = @impianto)
                  AND (@modello::text IS NULL OR c.nome_ricerca LIKE @modello OR lower(p.oggetto) LIKE @modello
                       OR p.numero::text = @testo)
                """;

            var parametri = new
            {
                stato = filtro.Stato,
                cliente = filtro.ClienteId,
                impianto = filtro.ImpiantoId,
                testo = filtro.Testo,
                modello = filtro.Testo is null ? null : "%" + RepositoryListino.EscapeLike(filtro.Testo.ToLowerInvariant()) + "%",
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<RiepilogoPreventivo>(Comando(
                $"""
                SELECT p.preventivo_id, p.anno, p.numero, p.stato, {Scaduto} AS scaduto, p.cliente_id, {NomeCliente} AS cliente,
                       p.oggetto, p.emesso_il, p.scade_il, p.totale, p.intervento_id, p.creato_il, p.versione
                {condizioni}
                ORDER BY p.creato_il DESC, p.preventivo_id DESC
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<RiepilogoPreventivo>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<Preventivo?> PreventivoAsync(Contesto contesto, Guid preventivoId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var preventivo = await c.QuerySingleOrDefaultAsync<Preventivo>(Comando(
                $"""
                SELECT p.preventivo_id, p.anno, p.numero, p.stato, {Scaduto} AS scaduto,
                       p.cliente_id, {NomeCliente} AS cliente, c.codice_fiscale, c.partita_iva,
                       p.sede_id, s.indirizzo, s.civico, s.cap, s.comune, s.provincia,
                       p.impianto_id, ti.nome AS tipo_impianto,
                       p.oggetto, p.note, p.validita_giorni, p.emesso_il, p.scade_il,
                       p.imponibile, p.iva, p.totale, p.accettato_il, p.rifiutato_il, p.annullato_il, p.motivo_annullamento,
                       p.intervento_id, p.creato_il, p.versione
                {Origine}
                WHERE p.preventivo_id = @preventivoId
                """,
                new { preventivoId }, t, ct));

            if (preventivo is not null)
            {
                preventivo.Righe = [.. await c.QueryAsync<RigaPreventivoLetta>(Comando(
                    """
                    SELECT posizione, voce_id, tipo, codice, descrizione, unita, quantita, prezzo_unitario,
                           sconto_percentuale, aliquota_iva, importo
                    FROM commerciale.preventivi_righe
                    WHERE preventivo_id = @preventivoId
                    ORDER BY posizione
                    """,
                    new { preventivoId }, t, ct))];
            }

            return preventivo;
        }, ct);

    public Task<IntestazioneAzienda> AziendaAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.QuerySingleAsync<IntestazioneAzienda>(Comando(
            "SELECT ragione_sociale, partita_iva, indirizzo, cap, comune, provincia FROM sec.aziende WHERE azienda_id = @aziendaId",
            new { aziendaId = contesto.AziendaId }, t, ct)), ct);

    public Task<EsitoCommerciale> CreaAsync(Contesto contesto, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (await ErroriRiferimentiAsync(c, t, dati, righe, ct) is { Count: > 0 } errori)
            {
                return new EsitoCommerciale.NonValida(errori);
            }

            var id = await c.ExecuteScalarAsync<Guid>(Comando(
                """
                INSERT INTO commerciale.preventivi (azienda_id, cliente_id, sede_id, impianto_id, oggetto, note, validita_giorni, creato_da)
                VALUES (@aziendaId, @ClienteId, @SedeId, @ImpiantoId, @Oggetto, @Note, @ValiditaGiorni, @utenteId)
                RETURNING preventivo_id
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, utenteId = contesto.UtenteId }), t, ct));

            return await SalvaRigheAsync(c, t, id, righe, ct);
        }, ct);

    public Task<EsitoCommerciale> AggiornaAsync(
        Contesto contesto, Guid preventivoId, int versione, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, preventivoId, ct), versione, StatiPreventivo.Bozza) is { } esito)
            {
                return esito;
            }

            if (await ErroriRiferimentiAsync(c, t, dati, righe, ct) is { Count: > 0 } errori)
            {
                return new EsitoCommerciale.NonValida(errori);
            }

            await c.ExecuteAsync(Comando(
                """
                UPDATE commerciale.preventivi
                SET cliente_id = @ClienteId, sede_id = @SedeId, impianto_id = @ImpiantoId, oggetto = @Oggetto,
                    note = @Note, validita_giorni = @ValiditaGiorni, modificato_da = @utenteId
                WHERE preventivo_id = @preventivoId
                """,
                new DynamicParameters(dati).Con(new { preventivoId, utenteId = contesto.UtenteId }), t, ct));

            return await SalvaRigheAsync(c, t, preventivoId, righe, ct);
        }, ct);

    public Task<EsitoCommerciale> EliminaAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, preventivoId, ct), versione, StatiPreventivo.Bozza) is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando("DELETE FROM commerciale.preventivi WHERE preventivo_id = @preventivoId", new { preventivoId }, t, ct));
            return new EsitoCommerciale.Riuscita(preventivoId, versione);
        }, ct);

    public Task<EsitoCommerciale> EmettiAsync(Contesto contesto, Guid preventivoId, int versione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, preventivoId, ct), versione, StatiPreventivo.Bozza) is { } esito)
            {
                return esito;
            }

            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    "SELECT EXISTS (SELECT 1 FROM commerciale.preventivi_righe WHERE preventivo_id = @preventivoId)", new { preventivoId }, t, ct)))
            {
                return new EsitoCommerciale.NonValida([ErroreCommerciale.PreventivoSenzaRighe]);
            }

            await c.ExecuteAsync(Comando("SELECT commerciale.emetti_preventivo(@preventivoId, @versione)", new { preventivoId, versione }, t, ct));
            return await RiuscitaAsync(c, t, preventivoId, ct);
        }, ct);

    public Task<EsitoCommerciale> RispostaClienteAsync(Contesto contesto, Guid preventivoId, int versione, bool accettato, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, preventivoId, ct), versione, StatiPreventivo.Emesso) is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando(
                accettato
                    ? "UPDATE commerciale.preventivi SET stato = 'accettato', accettato_il = now(), modificato_da = @utenteId WHERE preventivo_id = @preventivoId"
                    : "UPDATE commerciale.preventivi SET stato = 'rifiutato', rifiutato_il = now(), modificato_da = @utenteId WHERE preventivo_id = @preventivoId",
                new { preventivoId, utenteId = contesto.UtenteId }, t, ct));
            return await RiuscitaAsync(c, t, preventivoId, ct);
        }, ct);

    public Task<EsitoCommerciale> AnnullaAsync(Contesto contesto, Guid preventivoId, int versione, string motivo, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, preventivoId, ct);
            // Un accettato si annulla solo finché non ha generato l'intervento.
            var annullabile = attuale is { Stato: StatiPreventivo.Emesso } or { Stato: StatiPreventivo.Accettato, InterventoId: null };
            if (Controlla(attuale, versione, annullabile ? attuale!.Stato : StatiPreventivo.Emesso) is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando(
                """
                UPDATE commerciale.preventivi
                SET stato = 'annullato', annullato_il = now(), motivo_annullamento = @motivo, modificato_da = @utenteId
                WHERE preventivo_id = @preventivoId
                """,
                new { preventivoId, motivo, utenteId = contesto.UtenteId }, t, ct));
            return await RiuscitaAsync(c, t, preventivoId, ct);
        }, ct);

    public Task<EsitoCommerciale> ConvertiAsync(Contesto contesto, Guid preventivoId, int versione, string tipoIntervento, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, preventivoId, ct);
            if (Controlla(attuale, versione, StatiPreventivo.Accettato) is { } esito)
            {
                return esito;
            }

            if (attuale!.InterventoId is not null)
            {
                return new EsitoCommerciale.StatoNonConsentito("convertito");
            }

            // L'intervento vuole un impianto attivo e ancora visibile, come quando lo si crea dal calendario.
            if (attuale.ImpiantoId is null)
            {
                return new EsitoCommerciale.NonValida([ErroreCommerciale.ImpiantoMancantePerIntervento]);
            }

            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    """
                    SELECT EXISTS (SELECT 1 FROM impianti.impianti i
                                   WHERE i.impianto_id = @impianto AND i.stato = 'attivo' AND i.eliminato_il IS NULL)
                    """,
                    new { impianto = attuale.ImpiantoId }, t, ct)))
            {
                return new EsitoCommerciale.NonValida([ErroreCommerciale.ImpiantoPreventivoNonValido]);
            }

            var interventoId = await c.ExecuteScalarAsync<Guid>(Comando(
                "SELECT commerciale.converti_preventivo(@preventivoId, @versione, @tipoIntervento)",
                new { preventivoId, versione, tipoIntervento }, t, ct));

            var riuscita = (EsitoCommerciale.Riuscita)await RiuscitaAsync(c, t, preventivoId, ct);
            return riuscita with { InterventoId = interventoId };
        }, ct);

    // Cliente attivo, sede e impianto suoi, voci del listino esistenti.
    private static async Task<IReadOnlyList<Enum>> ErroriRiferimentiAsync(
        NpgsqlConnection c, NpgsqlTransaction t, DatiPreventivo dati, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct)
    {
        var voci = righe.Where(r => r.VoceId is not null).Select(r => r.VoceId!.Value).Distinct().ToArray();
        var controllo = await c.QuerySingleAsync<ControlloRiferimenti>(Comando(
            """
            SELECT
                EXISTS (SELECT 1 FROM anagrafica.clienti c
                        WHERE c.cliente_id = @cliente AND c.eliminato_il IS NULL AND c.anonimizzato_il IS NULL) AS cliente,
                (@sede::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM anagrafica.sedi s WHERE s.sede_id = @sede AND s.cliente_id = @cliente AND s.eliminata_il IS NULL)) AS sede,
                (@impianto::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM impianti.impianti i JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
                    WHERE i.impianto_id = @impianto AND s.cliente_id = @cliente AND i.eliminato_il IS NULL
                      AND (@sede::uuid IS NULL OR i.sede_id = @sede))) AS impianto,
                NOT EXISTS (SELECT 1 FROM unnest(@voci::uuid[]) AS v (id)
                            WHERE NOT EXISTS (SELECT 1 FROM commerciale.listino_voci l WHERE l.voce_id = v.id)) AS voci
            """,
            new { cliente = dati.ClienteId, sede = dati.SedeId, impianto = dati.ImpiantoId, voci }, t, ct));

        var errori = new List<Enum>();
        if (!controllo.Cliente)
        {
            errori.Add(ErroreCommerciale.ClientePreventivoNonValido);
        }

        if (!controllo.Sede)
        {
            errori.Add(ErroreCommerciale.SedePreventivoNonValida);
        }

        if (!controllo.Impianto)
        {
            errori.Add(ErroreCommerciale.ImpiantoPreventivoNonValido);
        }

        if (!controllo.Voci)
        {
            errori.Add(ErroreCommerciale.VoceRigaNonValida);
        }

        return errori;
    }

    private static async Task<EsitoCommerciale> SalvaRigheAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid preventivoId, IReadOnlyList<RigaPreventivo> righe, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            righe.Select(r => new
            {
                r.VoceId,
                r.Riga.Tipo,
                r.Riga.Codice,
                r.Riga.Descrizione,
                r.Riga.Unita,
                r.Riga.Quantita,
                r.Riga.PrezzoUnitario,
                r.Riga.ScontoPercentuale,
                r.Riga.AliquotaIva,
            }),
            s_json);

        var versione = await c.ExecuteScalarAsync<int>(Comando(
            "SELECT versione FROM commerciale.preventivi WHERE preventivo_id = @preventivoId", new { preventivoId }, t, ct));
        await c.ExecuteAsync(Comando(
            "SELECT commerciale.salva_righe_preventivo(@preventivoId, @versione, @json::jsonb)", new { preventivoId, versione, json }, t, ct));

        return await RiuscitaAsync(c, t, preventivoId, ct);
    }

    private static async Task<EsitoCommerciale> RiuscitaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid preventivoId, CancellationToken ct)
    {
        var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
            "SELECT preventivo_id AS id, versione, anno, numero FROM commerciale.preventivi WHERE preventivo_id = @preventivoId",
            new { preventivoId }, t, ct));
        return new EsitoCommerciale.Riuscita(riga.Id, riga.Versione, riga.Anno, riga.Numero);
    }

    private static Task<StatoAttuale?> BloccaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid preventivoId, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
            "SELECT stato, versione, impianto_id, intervento_id FROM commerciale.preventivi WHERE preventivo_id = @preventivoId FOR UPDATE",
            new { preventivoId }, t, ct));

    private static EsitoCommerciale? Controlla(StatoAttuale? attuale, int versione, string statoAmmesso) =>
        attuale switch
        {
            null => new EsitoCommerciale.NonTrovata(),
            _ when attuale.Versione != versione => new EsitoCommerciale.VersioneSuperata(),
            _ when attuale.Stato != statoAmmesso => new EsitoCommerciale.StatoNonConsentito(attuale.Stato),
            _ => null,
        };

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class RigaScritta
    {
        public Guid Id { get; init; }

        public int Versione { get; init; }

        public int? Anno { get; init; }

        public int? Numero { get; init; }
    }

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }

        public Guid? ImpiantoId { get; init; }

        public Guid? InterventoId { get; init; }
    }

    private sealed class ControlloRiferimenti
    {
        public bool Cliente { get; init; }

        public bool Sede { get; init; }

        public bool Impianto { get; init; }

        public bool Voci { get; init; }
    }
}

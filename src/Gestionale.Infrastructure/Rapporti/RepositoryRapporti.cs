using System.Text.Json.Nodes;
using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Rapporti;
using Gestionale.Domain.Rapporti;
using Npgsql;

namespace Gestionale.Infrastructure.Rapporti;

/// <summary>
/// Rapporti di controllo con Dapper (ADR 0003, ADR 0006). Precompilazione,
/// validazione contro il tracciato, bollino e passaggi di stato stanno nelle
/// funzioni delle migrazioni 0010 e 0011; qui si blocca la riga, si controllano
/// versione, stato e ambito, e si risponde con un esito chiaro.
/// </summary>
public sealed class RepositoryRapporti(Database.Database db) : IRepositoryRapporti
{
    private const string ViolazioneUnicita = "23505";
    private const string NessunDato = "P0002";

    // Il Tecnico vede i rapporti suoi o dei suoi interventi.
    private const string Propri = """
        (@soloTecnico::uuid IS NULL OR r.tecnico_id = @soloTecnico
         OR EXISTS (SELECT 1 FROM lavoro.interventi x WHERE x.intervento_id = r.intervento_id AND x.tecnico_utente_id = @soloTecnico))
        """;

    private const string NomeCliente = "CASE WHEN c.tipo = 'privato' THEN c.cognome || ' ' || c.nome ELSE c.denominazione END";

    private const string Origine = """
        FROM norma.rapporti_controllo r
        JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = r.modello_rapporto_id
        JOIN impianti.impianti i ON i.impianto_id = r.impianto_id
        JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
        JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
        LEFT JOIN norma.bollini b ON b.bollino_id = r.bollino_id
        """;

    public Task<Pagina<RiepilogoRapporto>> CercaAsync(
        Contesto contesto, FiltroRapporti filtro, Guid? soloTecnico, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var condizioni = $"""
                {Origine}
                WHERE {Propri}
                  AND (@impianto::uuid IS NULL OR r.impianto_id = @impianto)
                  AND (@intervento::uuid IS NULL OR r.intervento_id = @intervento)
                  AND (@stato::text IS NULL OR r.stato = @stato)
                  AND (NOT @daTrasmettere OR r.stato IN ('bozza', 'convalidato', 'firmato', 'scartato'))
                """;

            var parametri = new
            {
                soloTecnico,
                impianto = filtro.ImpiantoId,
                intervento = filtro.InterventoId,
                stato = filtro.Stato,
                daTrasmettere = filtro.DaTrasmettere,
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<RiepilogoRapporto>(Comando(
                $"""
                SELECT r.rapporto_controllo_id, r.impianto_id, r.generatore_id, r.intervento_id, m.codice AS modello,
                       r.data_controllo, r.termine_trasmissione, r.stato, {NomeCliente} AS cliente,
                       concat_ws(' ', s.indirizzo, s.civico) AS indirizzo, s.comune, b.codice AS codice_bollino, r.versione
                {condizioni}
                ORDER BY r.data_controllo DESC, r.rapporto_controllo_id DESC
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<RiepilogoRapporto>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<RapportoControllo?> RapportoAsync(Contesto contesto, Guid rapportoId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var rapporto = await c.QuerySingleOrDefaultAsync<RapportoControllo>(Comando(
                $"""
                SELECT r.rapporto_controllo_id, r.impianto_id, r.generatore_id, r.intervento_id, m.codice AS modello,
                       r.tecnico_id, (SELECT u.cognome || ' ' || u.nome FROM sec.utenti u WHERE u.utente_id = r.tecnico_id) AS tecnico,
                       r.data_controllo, r.termine_trasmissione, r.stato, r.bollino_id, b.codice AS codice_bollino,
                       (SELECT tb.nome FROM norma.tipi_bollino tb WHERE tb.tipo_bollino_id = b.tipo_bollino_id) AS tipo_bollino,
                       r.convalidato_il, r.firmato_il, r.annullato_il, r.motivo_annullamento, r.creato_il, r.versione
                {Origine}
                WHERE r.rapporto_controllo_id = @rapportoId AND {Propri}
                """,
                new { rapportoId, soloTecnico }, t, ct));

            if (rapporto is null)
            {
                return null;
            }

            var dati = await c.ExecuteScalarAsync<string>(Comando(
                "SELECT dati::text FROM norma.rapporti_controllo WHERE rapporto_controllo_id = @rapportoId", new { rapportoId }, t, ct));
            rapporto.Dati = JsonNode.Parse(dati!)!.AsObject();
            rapporto.Errori = [.. await c.QueryAsync<ErroreValidazione>(Comando(
                "SELECT campo, messaggio, bloccante FROM norma.valida_rapporto(@rapportoId)", new { rapportoId }, t, ct))];

            return rapporto;
        }, ct);

    public Task<string?> ModelloAsync(Contesto contesto, Guid rapportoId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.ExecuteScalarAsync<string?>(Comando(
            $"""
            SELECT m.codice
            FROM norma.rapporti_controllo r
            JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = r.modello_rapporto_id
            WHERE r.rapporto_controllo_id = @rapportoId AND {Propri}
            """,
            new { rapportoId, soloTecnico }, t, ct)), ct);

    public Task<IReadOnlyList<CampoTracciato>> TracciatoAsync(Contesto contesto, string modello, CancellationToken ct) =>
        Esegui(contesto, async (c, t) => (await LetturaTracciato.LeggiAsync(c, t, modello, ct)).Campi, ct);

    public Task<EsitoRapporto> CreaAsync(Contesto contesto, DatiNuovoRapporto dati, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var controllo = await c.QuerySingleAsync<ControlloRiferimenti>(Comando(
                """
                SELECT
                    EXISTS (SELECT 1 FROM impianti.impianti i
                            WHERE i.impianto_id = @impianto AND i.stato = 'attivo' AND i.eliminato_il IS NULL) AS impianto,
                    EXISTS (SELECT 1 FROM impianti.generatori g
                            WHERE g.generatore_id = @generatore AND g.impianto_id = @impianto AND g.dismesso_il IS NULL) AS generatore,
                    (@intervento::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM lavoro.interventi x
                        WHERE x.intervento_id = @intervento AND x.impianto_id = @impianto AND x.stato <> 'annullato'
                          AND (@soloTecnico::uuid IS NULL OR x.tecnico_utente_id = @soloTecnico))) AS intervento,
                    (SELECT x.tecnico_utente_id FROM lavoro.interventi x WHERE x.intervento_id = @intervento) AS tecnico_intervento
                """,
                new { impianto = dati.ImpiantoId, generatore = dati.GeneratoreId, intervento = dati.InterventoId, soloTecnico }, t, ct));

            var errori = new List<ErroreRapporto>();
            if (!controllo.Impianto)
            {
                errori.Add(ErroreRapporto.ImpiantoRapportoNonValido);
            }

            if (!controllo.Generatore)
            {
                errori.Add(ErroreRapporto.GeneratoreRapportoNonValido);
            }

            if (!controllo.Intervento)
            {
                errori.Add(ErroreRapporto.InterventoRapportoNonValido);
            }

            if (errori.Count > 0)
            {
                return new EsitoRapporto.NonValida(errori, []);
            }

            var id = await c.ExecuteScalarAsync<Guid>(Comando(
                "SELECT norma.crea_rapporto(@impianto, @generatore, @intervento, @modello, @data, @tecnico)",
                new
                {
                    impianto = dati.ImpiantoId,
                    generatore = dati.GeneratoreId,
                    intervento = dati.InterventoId,
                    modello = dati.Modello,
                    data = dati.DataControllo,
                    tecnico = controllo.TecnicoIntervento ?? soloTecnico,
                },
                t, ct));

            return new EsitoRapporto.Riuscita(id, 1);
        }, ct);

    public Task<EsitoRapporto> AggiornaAsync(
        Contesto contesto, Guid rapportoId, int versione, DateOnly dataControllo, JsonObject dati, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, rapportoId, soloTecnico, ct), versione, StatiRapporto.Bozza) is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando(
                """
                UPDATE norma.rapporti_controllo
                SET data_controllo = @dataControllo, dati = @dati::jsonb, modificato_da = @utenteId
                WHERE rapporto_controllo_id = @rapportoId
                """,
                new { rapportoId, dataControllo, dati = dati.ToJsonString(), utenteId = contesto.UtenteId }, t, ct));

            return await RiuscitaAsync(c, t, rapportoId, ct);
        }, ct);

    public Task<EsitoRapporto> AssegnaBollinoAsync(
        Contesto contesto, Guid rapportoId, int versione, Guid tipoBollinoId, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, rapportoId, soloTecnico, ct);
            if (Controlla(attuale, versione, StatiRapporto.Bozza, StatiRapporto.Convalidato) is { } esito)
            {
                return esito;
            }

            if (attuale!.BollinoId is not null)
            {
                return new EsitoRapporto.StatoNonConsentito("con_bollino");
            }

            var categoria = await c.ExecuteScalarAsync<string?>(Comando(
                """
                SELECT t.categoria
                FROM norma.tipi_bollino t
                JOIN sec.aziende a ON a.regione = t.regione AND a.azienda_id = @aziendaId
                WHERE t.tipo_bollino_id = @tipoBollinoId
                  AND t.valido_dal <= (now() AT TIME ZONE 'Europe/Rome')::date
                  AND (t.valido_al IS NULL OR t.valido_al >= (now() AT TIME ZONE 'Europe/Rome')::date)
                """,
                new { tipoBollinoId, aziendaId = contesto.AziendaId }, t, ct));

            if (categoria is null)
            {
                return new EsitoRapporto.NonValida(ErroreRapporto.TipoBollinoNonValido);
            }

            if (categoria != attuale.CategoriaBollino)
            {
                return new EsitoRapporto.NonValida(ErroreRapporto.TipoBollinoNonDelModello);
            }

            await c.ExecuteAsync(Comando(
                "SELECT norma.assegna_bollino(@rapportoId, @versione, @tipoBollinoId)", new { rapportoId, versione, tipoBollinoId }, t, ct));
            return await RiuscitaAsync(c, t, rapportoId, ct);
        }, ct);

    public Task<EsitoRapporto> LiberaBollinoAsync(Contesto contesto, Guid rapportoId, int versione, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, rapportoId, soloTecnico, ct);
            if (Controlla(attuale, versione, StatiRapporto.Bozza, StatiRapporto.Convalidato) is { } esito)
            {
                return esito;
            }

            if (attuale!.BollinoId is null)
            {
                return new EsitoRapporto.StatoNonConsentito("senza_bollino");
            }

            await c.ExecuteAsync(Comando("SELECT norma.libera_bollino(@rapportoId, @versione)", new { rapportoId, versione }, t, ct));
            return await RiuscitaAsync(c, t, rapportoId, ct);
        }, ct);

    public Task<EsitoRapporto> CambiaStatoAsync(
        Contesto contesto, Guid rapportoId, int versione, PassaggioRapporto passaggio, string? motivo, bool ancheFirmati, Guid? soloTecnico,
        CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            string[] ammessi = passaggio switch
            {
                PassaggioRapporto.Convalida => [StatiRapporto.Bozza],
                PassaggioRapporto.Riapertura or PassaggioRapporto.Firma => [StatiRapporto.Convalidato],
                _ when ancheFirmati => [StatiRapporto.Bozza, StatiRapporto.Convalidato, StatiRapporto.Firmato, StatiRapporto.Scartato],
                _ => [StatiRapporto.Bozza, StatiRapporto.Convalidato],
            };

            if (Controlla(await BloccaAsync(c, t, rapportoId, soloTecnico, ct), versione, ammessi) is { } esito)
            {
                return esito;
            }

            if (passaggio is PassaggioRapporto.Convalida or PassaggioRapporto.Firma)
            {
                var bloccanti = (await c.QueryAsync<ErroreValidazione>(Comando(
                    "SELECT campo, messaggio, bloccante FROM norma.valida_rapporto(@rapportoId) WHERE bloccante",
                    new { rapportoId }, t, ct))).ToList();
                if (bloccanti.Count > 0)
                {
                    return new EsitoRapporto.Bloccata(bloccanti);
                }
            }

            var funzione = passaggio switch
            {
                PassaggioRapporto.Convalida => "SELECT norma.convalida_rapporto(@rapportoId, @versione)",
                PassaggioRapporto.Riapertura => "SELECT norma.riapri_rapporto(@rapportoId, @versione)",
                PassaggioRapporto.Firma => "SELECT norma.firma_rapporto(@rapportoId, @versione)",
                _ => "SELECT norma.annulla_rapporto(@rapportoId, @versione, @motivo)",
            };

            await c.ExecuteAsync(Comando(funzione, new { rapportoId, versione, motivo }, t, ct));
            return await RiuscitaAsync(c, t, rapportoId, ct);
        }, ct);

    private static Task<StatoAttuale?> BloccaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid rapportoId, Guid? soloTecnico, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
            $"""
            SELECT r.stato, r.versione, r.bollino_id, m.categoria_bollino
            FROM norma.rapporti_controllo r
            JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = r.modello_rapporto_id
            WHERE r.rapporto_controllo_id = @rapportoId AND {Propri}
            FOR UPDATE OF r
            """,
            new { rapportoId, soloTecnico }, t, ct));

    private static EsitoRapporto? Controlla(StatoAttuale? attuale, int versione, params string[] statiAmmessi) =>
        attuale switch
        {
            null => new EsitoRapporto.NonTrovata(),
            _ when attuale.Versione != versione => new EsitoRapporto.VersioneSuperata(),
            _ when !statiAmmessi.Contains(attuale.Stato) => new EsitoRapporto.StatoNonConsentito(attuale.Stato),
            _ => null,
        };

    private static async Task<EsitoRapporto> RiuscitaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid rapportoId, CancellationToken ct) =>
        new EsitoRapporto.Riuscita(rapportoId, await c.ExecuteScalarAsync<int>(Comando(
            "SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = @rapportoId", new { rapportoId }, t, ct)));

    // Le eccezioni attese arrivano dopo il rollback della transazione.
    private async Task<EsitoRapporto> Scrivi(
        Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<EsitoRapporto>> lavoro, CancellationToken ct)
    {
        try
        {
            return await Esegui(contesto, lavoro, ct);
        }
        catch (PostgresException e) when (e.SqlState == NessunDato)
        {
            return new EsitoRapporto.NonValida(ErroreRapporto.BollinoNonDisponibile);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita && e.ConstraintName == "uq_rapporti_controllo_intervento_generatore")
        {
            return new EsitoRapporto.NonValida(ErroreRapporto.RapportoGiaPresente);
        }
    }

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }

        public Guid? BollinoId { get; init; }

        public string CategoriaBollino { get; init; } = "";
    }

    private sealed class ControlloRiferimenti
    {
        public bool Impianto { get; init; }

        public bool Generatore { get; init; }

        public bool Intervento { get; init; }

        public Guid? TecnicoIntervento { get; init; }
    }
}

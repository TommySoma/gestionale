using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Scadenze;
using Gestionale.Domain.Scadenze;
using Gestionale.Infrastructure.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Scadenze;

/// <summary>
/// Scadenze con Dapper (ADR 0003). Il calcolo è nel database (migrazione 0008):
/// qui si leggono, si corregge la data e si registrano i richiami. Ogni metodo è
/// una transazione sotto Row-Level Security; le scritture bloccano la scadenza e
/// controllano versione e stato prima di toccarla.
/// </summary>
public sealed class RepositoryScadenze(Database.Database db) : IRepositoryScadenze
{
    // Come per gli impianti: il Tecnico vede quelli su cui ha un intervento non annullato.
    private const string AssegnatoAlTecnico = """
        (@soloTecnico::uuid IS NULL OR EXISTS (
            SELECT 1 FROM lavoro.interventi ti
            WHERE ti.impianto_id = {0} AND ti.tecnico_utente_id = @soloTecnico AND ti.stato <> 'annullato'))
        """;

    // Un impianto si vede se non è eliminato e la sua sede e il suo cliente esistono ancora.
    private const string ImpiantoVisibile = """
        EXISTS (SELECT 1 FROM impianti.impianti vi
                JOIN anagrafica.sedi vs ON vs.sede_id = vi.sede_id
                JOIN anagrafica.clienti vc ON vc.cliente_id = vs.cliente_id
                WHERE vi.impianto_id = {0} AND vi.eliminato_il IS NULL
                  AND vs.eliminata_il IS NULL AND vc.eliminato_il IS NULL)
        """;

    private const string ColonneScadenza = """
        s.scadenza_id, s.impianto_id, s.tipo, s.data_scadenza, s.stato, s.periodicita_mesi,
        r.nome AS regola, coalesce(r.da_verificare, false) AS regola_da_verificare, s.data_manuale,
        s.intervento_origine_id, s.intervento_chiusura_id, s.chiusa_il, s.annullata_il, s.motivo_annullamento,
        s.stato_richiamo, s.versione
        """;

    public Task<Pagina<ScadenzaInArrivo>> InArrivoAsync(
        Contesto contesto, FiltroScadenze filtro, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var condizioni = $"""
                FROM lavoro.scadenze_in_arrivo(@giorni) s
                WHERE (@tipo::text IS NULL OR s.tipo = @tipo)
                  AND (@richiamo::text IS NULL OR s.stato_richiamo = @richiamo)
                  AND {Sostituisci(AssegnatoAlTecnico, "s.impianto_id")}
                """;

            var parametri = new
            {
                giorni = filtro.Giorni,
                tipo = filtro.Tipo,
                richiamo = filtro.StatoRichiamo,
                soloTecnico = filtro.TecnicoUtenteId,
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<ScadenzaInArrivo>(Comando(
                $"""
                SELECT s.scadenza_id, s.impianto_id, s.tipo, s.data_scadenza, s.giorni_mancanti, s.stato_richiamo, s.versione,
                       s.cliente_id, s.cliente, s.indirizzo, s.comune, s.provincia, s.telefono,
                       s.ultimo_richiamo_il, s.ultimo_esito, s.richiamare_il
                {condizioni}
                ORDER BY s.data_scadenza, s.scadenza_id
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<ScadenzaInArrivo>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<Scadenza?> ScadenzaAsync(Contesto contesto, Guid scadenzaId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var scadenza = await c.QuerySingleOrDefaultAsync<Scadenza>(Comando(
                $"""
                SELECT {ColonneScadenza}
                FROM lavoro.scadenze s
                LEFT JOIN norma.regole_cadenza r ON r.regola_cadenza_id = s.regola_cadenza_id
                WHERE s.scadenza_id = @scadenzaId
                  AND {Sostituisci(ImpiantoVisibile, "s.impianto_id")}
                  AND {Sostituisci(AssegnatoAlTecnico, "s.impianto_id")}
                """,
                new { scadenzaId, soloTecnico }, t, ct));

            if (scadenza is not null)
            {
                scadenza.Richiami = (await RichiamiAsync(c, t, [scadenzaId], ct)).GetValueOrDefault(scadenzaId, []);
            }

            return scadenza;
        }, ct);

    public Task<IReadOnlyList<Scadenza>?> DellImpiantoAsync(Contesto contesto, Guid impiantoId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var visibile = await c.ExecuteScalarAsync<bool>(Comando(
                $"SELECT {Sostituisci(ImpiantoVisibile, "@impiantoId")} AND {Sostituisci(AssegnatoAlTecnico, "@impiantoId")}",
                new { impiantoId, soloTecnico }, t, ct));
            if (!visibile)
            {
                return null;
            }

            var scadenze = (await c.QueryAsync<Scadenza>(Comando(
                $"""
                SELECT {ColonneScadenza}
                FROM lavoro.scadenze s
                LEFT JOIN norma.regole_cadenza r ON r.regola_cadenza_id = s.regola_cadenza_id
                WHERE s.impianto_id = @impiantoId
                ORDER BY s.stato <> 'aperta', s.data_scadenza DESC, s.scadenza_id DESC
                """,
                new { impiantoId }, t, ct))).ToList();

            var richiami = await RichiamiAsync(c, t, [.. scadenze.Select(s => s.ScadenzaId)], ct);
            foreach (var scadenza in scadenze)
            {
                scadenza.Richiami = richiami.GetValueOrDefault(scadenza.ScadenzaId, []);
            }

            return (IReadOnlyList<Scadenza>?)scadenze;
        }, ct);

    public Task<EsitoScadenza> CorreggiDataAsync(Contesto contesto, Guid scadenzaId, int versione, DateOnly data, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, scadenzaId, ct), versione) is { } esito)
            {
                return esito;
            }

            var nuovaVersione = await c.ExecuteScalarAsync<int>(Comando(
                """
                UPDATE lavoro.scadenze
                SET data_scadenza = @data, data_manuale = true, modificato_da = @utenteId
                WHERE scadenza_id = @scadenzaId
                RETURNING versione
                """,
                new { scadenzaId, data, utenteId = contesto.UtenteId }, t, ct));

            return new EsitoScadenza.Riuscita(scadenzaId, nuovaVersione);
        }, ct);

    public Task<EsitoScadenza> RegistraRichiamoAsync(Contesto contesto, Guid scadenzaId, int versione, DatiRichiamo dati, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, scadenzaId, ct);
            if (Controlla(attuale, versione) is { } esito)
            {
                return esito;
            }

            // L'appuntamento è un intervento non annullato sullo stesso impianto.
            if (dati.InterventoId is { } interventoId && !await c.ExecuteScalarAsync<bool>(Comando(
                    """
                    SELECT EXISTS (SELECT 1 FROM lavoro.interventi
                                   WHERE intervento_id = @interventoId AND impianto_id = @impiantoId AND stato <> 'annullato')
                    """,
                    new { interventoId, impiantoId = attuale!.ImpiantoId }, t, ct)))
            {
                return new EsitoScadenza.NonValida([ErroreScadenza.InterventoRichiamoNonValido]);
            }

            // Il trigger aggiorna lo stato del richiamo, e con esso la versione della scadenza.
            await c.ExecuteAsync(Comando(
                """
                INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito, richiamare_il, intervento_id, note, creato_da)
                VALUES (@aziendaId, @scadenzaId, @Canale, @Esito, @RichiamareIl, @InterventoId, @Note, @utenteId)
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, scadenzaId, utenteId = contesto.UtenteId }), t, ct));

            var nuovaVersione = await c.ExecuteScalarAsync<int>(Comando(
                "SELECT versione FROM lavoro.scadenze WHERE scadenza_id = @scadenzaId", new { scadenzaId }, t, ct));

            return new EsitoScadenza.Riuscita(scadenzaId, nuovaVersione);
        }, ct);

    private static async Task<Dictionary<Guid, IReadOnlyList<Richiamo>>> RichiamiAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid[] scadenze, CancellationToken ct)
    {
        if (scadenze.Length == 0)
        {
            return new();
        }

        var righe = await c.QueryAsync<RigaRichiamo>(Comando(
            """
            SELECT ri.scadenza_id, ri.richiamo_id, ri.canale, ri.effettuato_il, u.nome || ' ' || u.cognome AS effettuato_da,
                   ri.esito, ri.richiamare_il, ri.intervento_id, ri.note
            FROM lavoro.richiami ri
            LEFT JOIN sec.utenti u ON u.utente_id = ri.creato_da
            WHERE ri.scadenza_id = ANY(@scadenze)
            ORDER BY ri.effettuato_il DESC, ri.richiamo_id DESC
            """,
            new { scadenze }, t, ct));

        return righe
            .GroupBy(r => r.ScadenzaId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Richiamo>)[.. g.Select(r => r.Richiamo())]);
    }

    private static Task<StatoAttuale?> BloccaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid scadenzaId, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
            $"""
            SELECT s.stato, s.versione, s.impianto_id FROM lavoro.scadenze s
            WHERE s.scadenza_id = @scadenzaId AND {Sostituisci(ImpiantoVisibile, "s.impianto_id")}
            FOR UPDATE OF s
            """,
            new { scadenzaId }, t, ct));

    private static EsitoScadenza? Controlla(StatoAttuale? attuale, int versione) =>
        attuale switch
        {
            null => new EsitoScadenza.NonTrovata(),
            _ when attuale.Versione != versione => new EsitoScadenza.VersioneSuperata(),
            _ when attuale.Stato != StatiScadenza.Aperta => new EsitoScadenza.NonAperta(attuale.Stato),
            _ => null,
        };

    private static string Sostituisci(string modello, string impianto) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, modello, impianto);

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }

        public Guid ImpiantoId { get; init; }
    }

    private sealed class RigaRichiamo
    {
        public Guid ScadenzaId { get; init; }

        public Guid RichiamoId { get; init; }

        public string Canale { get; init; } = "";

        public DateTime EffettuatoIl { get; init; }

        public string? EffettuatoDa { get; init; }

        public string Esito { get; init; } = "";

        public DateOnly? RichiamareIl { get; init; }

        public Guid? InterventoId { get; init; }

        public string? Note { get; init; }

        public Richiamo Richiamo() => new()
        {
            RichiamoId = RichiamoId,
            Canale = Canale,
            EffettuatoIl = EffettuatoIl,
            EffettuatoDa = EffettuatoDa,
            Esito = Esito,
            RichiamareIl = RichiamareIl,
            InterventoId = InterventoId,
            Note = Note,
        };
    }
}

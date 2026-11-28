using System.Text.Json;
using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Interventi;
using Gestionale.Domain.Interventi;
using Gestionale.Domain.Sicurezza;
using Npgsql;

namespace Gestionale.Infrastructure.Interventi;

/// <summary>
/// Interventi con Dapper (ADR 0003). Ogni metodo è una transazione con azienda e
/// utente impostati, sotto Row-Level Security. Le scritture bloccano la riga
/// (FOR UPDATE) e controllano versione e stato prima di toccarla, per rispondere
/// con un esito chiaro; numerazione, sovrapposizioni e blocco dopo la chiusura
/// restano comunque garantiti dal database.
/// </summary>
public sealed class RepositoryInterventi(Database.Database db) : IRepositoryInterventi
{
    private const string ViolazioneEsclusione = "23P01";
    private const string SenzaCertificatoFgas = "GF001";

    private const string NomeTecnico = "u.nome || ' ' || u.cognome";

    private const string NomeCliente = "CASE WHEN c.tipo = 'privato' THEN c.cognome || ' ' || c.nome ELSE c.denominazione END";

    // Si assegna a chi è attivo e ha un ruolo con il permesso di scrivere gli interventi.
    private static readonly string TecnicoAssegnabile = $"""
        u.stato = 'attivo' AND EXISTS (
            SELECT 1 FROM sec.utenti_ruoli ur
            JOIN sec.ruoli_permessi rp ON rp.ruolo_id = ur.ruolo_id
            JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
            WHERE ur.utente_id = u.utente_id AND p.codice = '{CodiciPermesso.InterventiScrittura}')
        """;

    private const string Origine = """
        FROM lavoro.interventi x
        JOIN impianti.impianti i ON i.impianto_id = x.impianto_id
        JOIN impianti.tipi_impianto ti ON ti.tipo_impianto_id = i.tipo_impianto_id
        JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
        JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
        LEFT JOIN sec.utenti u ON u.utente_id = x.tecnico_utente_id
        """;

    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public Task<Pagina<RiepilogoIntervento>> CercaAsync(
        Contesto contesto, FiltroInterventi filtro, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var calendario = filtro.Da is not null || filtro.A is not null;
            var condizioni = $"""
                {Origine}
                WHERE (@tecnico::uuid IS NULL OR x.tecnico_utente_id = @tecnico)
                  AND (@impianto::uuid IS NULL OR x.impianto_id = @impianto)
                  AND (@stato::text IS NULL OR x.stato = @stato)
                  {(calendario ? "AND x.finestra && tstzrange(@da::timestamptz, @a::timestamptz, '[)')" : "")}
                """;

            var parametri = new
            {
                tecnico = filtro.TecnicoUtenteId,
                impianto = filtro.ImpiantoId,
                stato = filtro.Stato,
                da = filtro.Da?.UtcDateTime,
                a = filtro.A?.UtcDateTime,
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            // Il calendario va in ordine di orario; la timeline e l'elenco dal più recente.
            var ordine = calendario
                ? "lower(x.finestra), u.cognome, x.intervento_id"
                : "coalesce(lower(x.finestra), x.creato_il) DESC, x.intervento_id DESC";

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<RiepilogoIntervento>(Comando(
                $"""
                SELECT x.intervento_id, x.anno, x.numero, x.tipo, x.stato, x.esito,
                       lower(x.finestra) AS inizio, upper(x.finestra) AS fine,
                       x.tecnico_utente_id, {NomeTecnico} AS tecnico,
                       x.impianto_id, ti.nome AS tipo_impianto, {NomeCliente} AS cliente,
                       s.indirizzo, s.civico, s.comune, s.provincia, x.descrizione, x.versione
                {condizioni}
                ORDER BY {ordine}
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<RiepilogoIntervento>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<Intervento?> InterventoAsync(Contesto contesto, Guid interventoId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var intervento = await c.QuerySingleOrDefaultAsync<Intervento>(Comando(
                $"""
                SELECT x.intervento_id, x.anno, x.numero, x.tipo, x.stato, x.esito,
                       lower(x.finestra) AS inizio, upper(x.finestra) AS fine,
                       x.tecnico_utente_id, {NomeTecnico} AS tecnico,
                       x.impianto_id, ti.nome AS tipo_impianto,
                       x.generatore_id,
                       concat_ws(' ', tg.nome, g.marca, g.modello, '(' || g.matricola || ')') AS generatore,
                       c.cliente_id, {NomeCliente} AS cliente,
                       s.indirizzo, s.civico, s.cap, s.comune, s.provincia,
                       x.descrizione, x.note_chiusura, x.controllo_efficienza, x.chiuso_il, x.annullato_il, x.motivo_annullamento,
                       x.creato_il, x.versione
                {Origine}
                LEFT JOIN impianti.generatori g ON g.generatore_id = x.generatore_id
                LEFT JOIN impianti.tipi_generatore tg ON tg.tipo_generatore_id = g.tipo_generatore_id
                WHERE x.intervento_id = @interventoId
                  AND (@soloTecnico::uuid IS NULL OR x.tecnico_utente_id = @soloTecnico)
                """,
                new { interventoId, soloTecnico }, t, ct));

            if (intervento is null)
            {
                return null;
            }

            intervento.Righe = [.. await c.QueryAsync<RigaIntervento>(Comando(
                """
                SELECT posizione, tipo, codice, descrizione, unita, quantita, prezzo_unitario,
                       sconto_percentuale, aliquota_iva, importo
                FROM lavoro.interventi_righe
                WHERE intervento_id = @interventoId
                ORDER BY posizione
                """,
                new { interventoId }, t, ct))];

            return intervento;
        }, ct);

    public Task<IntestazioneAzienda> AziendaAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.QuerySingleAsync<IntestazioneAzienda>(Comando(
            "SELECT ragione_sociale, partita_iva, indirizzo, cap, comune, provincia FROM sec.aziende WHERE azienda_id = @aziendaId",
            new { aziendaId = contesto.AziendaId }, t, ct)), ct);

    public Task<IReadOnlyList<TecnicoAssegnabile>> TecniciAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
            (IReadOnlyList<TecnicoAssegnabile>)[.. await c.QueryAsync<TecnicoAssegnabile>(Comando(
                $"""
                SELECT u.utente_id, {NomeTecnico} AS nome
                FROM sec.utenti u
                WHERE {TecnicoAssegnabile}
                ORDER BY u.cognome, u.nome, u.utente_id
                """,
                null, t, ct))], ct);

    public Task<EsitoIntervento> CreaAsync(Contesto contesto, DatiIntervento dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (await ErroriRiferimentiAsync(c, t, dati, null, ct) is { Count: > 0 } errori)
            {
                return new EsitoIntervento.NonValida(errori);
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO lavoro.interventi (azienda_id, impianto_id, generatore_id, tipo, stato, finestra,
                                               tecnico_utente_id, descrizione, creato_da)
                VALUES (@aziendaId, @impianto, @generatore, @tipo,
                        CASE WHEN @inizio::timestamptz IS NULL THEN 'da_pianificare' ELSE 'pianificato' END,
                        CASE WHEN @inizio::timestamptz IS NULL THEN NULL ELSE tstzrange(@inizio, @fine, '[)') END,
                        @tecnico, @descrizione, @utenteId)
                RETURNING intervento_id AS id, versione, anno, numero
                """,
                Parametri(contesto, dati), t, ct));

            return riga.Esito();
        }, ct);

    public Task<EsitoIntervento> AggiornaAsync(Contesto contesto, Guid interventoId, int versione, DatiIntervento dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, interventoId, null, ct);
            if (Controlla(attuale, versione, StatiIntervento.DaPianificare, StatiIntervento.Pianificato, StatiIntervento.InCorso) is { } esito)
            {
                return esito;
            }

            // Un intervento già iniziato resta in calendario.
            if (attuale!.Stato == StatiIntervento.InCorso && dati.Inizio is null)
            {
                return new EsitoIntervento.StatoNonConsentito(attuale.Stato);
            }

            if (await ErroriRiferimentiAsync(c, t, dati, attuale.ImpiantoId, ct) is { Count: > 0 } errori)
            {
                return new EsitoIntervento.NonValida(errori);
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                UPDATE lavoro.interventi
                SET impianto_id = @impianto, generatore_id = @generatore, tipo = @tipo, descrizione = @descrizione,
                    tecnico_utente_id = @tecnico,
                    finestra = CASE WHEN @inizio::timestamptz IS NULL THEN NULL ELSE tstzrange(@inizio, @fine, '[)') END,
                    stato = CASE WHEN stato = 'in_corso' THEN 'in_corso'
                                 WHEN @inizio::timestamptz IS NULL THEN 'da_pianificare'
                                 ELSE 'pianificato' END,
                    modificato_da = @utenteId
                WHERE intervento_id = @interventoId
                RETURNING intervento_id AS id, versione, anno, numero
                """,
                new DynamicParameters(Parametri(contesto, dati)).Aggiungi("interventoId", interventoId), t, ct));

            return riga.Esito();
        }, ct);

    public Task<EsitoIntervento> AvviaAsync(Contesto contesto, Guid interventoId, int versione, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, interventoId, soloTecnico, ct);
            if (Controlla(attuale, versione, StatiIntervento.Pianificato) is { } esito)
            {
                return esito;
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                UPDATE lavoro.interventi SET stato = 'in_corso', modificato_da = @utenteId
                WHERE intervento_id = @interventoId
                RETURNING intervento_id AS id, versione, anno, numero
                """,
                new { interventoId, utenteId = contesto.UtenteId }, t, ct));

            return riga.Esito();
        }, ct);

    public Task<EsitoIntervento> ChiudiAsync(
        Contesto contesto, Guid interventoId, int versione, DatiChiusura dati, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, interventoId, soloTecnico, ct);
            if (Controlla(attuale, versione, StatiIntervento.Pianificato, StatiIntervento.InCorso) is { } esito)
            {
                return esito;
            }

            // Il controllo di efficienza si esegue durante una manutenzione (vincolo anche nel database).
            if (dati.ControlloEfficienza && attuale!.Tipo != TipiIntervento.Manutenzione)
            {
                return new EsitoIntervento.NonValida([ErroreIntervento.ControlloSoloManutenzione]);
            }

            // La bozza del rapporto vuole un modello in vigore e un generatore attivo dell'impianto.
            if (dati.ModelloRapporto is { } modello)
            {
                var modelloValido = await c.ExecuteScalarAsync<bool>(Comando(
                    """
                    SELECT EXISTS (SELECT 1 FROM norma.modelli_rapporto
                                   WHERE codice = @modello AND valido_dal <= current_date
                                     AND (valido_al IS NULL OR valido_al >= current_date))
                    """,
                    new { modello }, t, ct));
                if (!modelloValido)
                {
                    return new EsitoIntervento.NonValida([ErroreIntervento.ModelloBozzaNonValido]);
                }

                var generatore = dati.GeneratoreRapporto ?? attuale!.GeneratoreId;
                var generatoreValido = generatore is not null && await c.ExecuteScalarAsync<bool>(Comando(
                    """
                    SELECT EXISTS (SELECT 1 FROM impianti.generatori
                                   WHERE generatore_id = @generatore AND impianto_id = @impianto AND dismesso_il IS NULL)
                    """,
                    new { generatore, impianto = attuale!.ImpiantoId }, t, ct));
                if (!generatoreValido)
                {
                    return new EsitoIntervento.NonValida([ErroreIntervento.GeneratoreBozzaNonValido]);
                }
            }

            var righe = JsonSerializer.Serialize(
                (dati.Righe ?? []).Select(r => new
                {
                    r.Tipo,
                    r.Codice,
                    r.Descrizione,
                    r.Unita,
                    r.Quantita,
                    r.PrezzoUnitario,
                    r.ScontoPercentuale,
                    r.AliquotaIva,
                }),
                s_json);

            await c.ExecuteAsync(Comando(
                "SELECT lavoro.chiudi_intervento(@interventoId, @versione, @esito, @note, @righe::jsonb, @controllo, @modello::text, @generatore::uuid)",
                new
                {
                    interventoId,
                    versione,
                    esito = dati.Esito,
                    note = dati.Note,
                    righe,
                    controllo = dati.ControlloEfficienza,
                    modello = dati.ModelloRapporto,
                    generatore = dati.GeneratoreRapporto,
                }, t, ct));

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                "SELECT intervento_id AS id, versione, anno, numero FROM lavoro.interventi WHERE intervento_id = @interventoId",
                new { interventoId }, t, ct));

            // La bozza creata ora, o quella che l'intervento aveva già per lo stesso generatore.
            var rapporto = dati.ModelloRapporto is null ? null : await c.ExecuteScalarAsync<Guid?>(Comando(
                """
                SELECT rapporto_controllo_id FROM norma.rapporti_controllo
                WHERE intervento_id = @interventoId AND stato <> 'annullato'
                ORDER BY creato_il DESC LIMIT 1
                """,
                new { interventoId }, t, ct));

            return new EsitoIntervento.Riuscita(riga.Id, riga.Versione, riga.Anno, riga.Numero, rapporto);
        }, ct);

    public Task<EsitoIntervento> AnnullaAsync(Contesto contesto, Guid interventoId, int versione, string motivo, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await BloccaAsync(c, t, interventoId, null, ct);
            if (Controlla(attuale, versione, StatiIntervento.DaPianificare, StatiIntervento.Pianificato, StatiIntervento.InCorso) is { } esito)
            {
                return esito;
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                UPDATE lavoro.interventi
                SET stato = 'annullato', annullato_il = now(), annullato_da = @utenteId,
                    motivo_annullamento = @motivo, modificato_da = @utenteId
                WHERE intervento_id = @interventoId
                RETURNING intervento_id AS id, versione, anno, numero
                """,
                new { interventoId, motivo, utenteId = contesto.UtenteId }, t, ct));

            return riga.Esito();
        }, ct);

    private static Task<StatoAttuale?> BloccaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid interventoId, Guid? soloTecnico, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
            """
            SELECT stato, versione, impianto_id, generatore_id, tipo FROM lavoro.interventi
            WHERE intervento_id = @interventoId
              AND (@soloTecnico::uuid IS NULL OR tecnico_utente_id = @soloTecnico)
            FOR UPDATE
            """,
            new { interventoId, soloTecnico }, t, ct));

    private static EsitoIntervento? Controlla(StatoAttuale? attuale, int versione, params string[] statiAmmessi) =>
        attuale switch
        {
            null => new EsitoIntervento.NonTrovata(),
            _ when attuale.Versione != versione => new EsitoIntervento.VersioneSuperata(),
            _ when !statiAmmessi.Contains(attuale.Stato) => new EsitoIntervento.StatoNonConsentito(attuale.Stato),
            _ => null,
        };

    // Impianto attivo e visibile (o quello già collegato), generatore dello stesso impianto, tecnico assegnabile.
    private static async Task<IReadOnlyList<ErroreIntervento>> ErroriRiferimentiAsync(
        NpgsqlConnection c, NpgsqlTransaction t, DatiIntervento dati, Guid? impiantoAttuale, CancellationToken ct)
    {
        var controllo = await c.QuerySingleAsync<ControlloRiferimenti>(Comando(
            $"""
            SELECT
                EXISTS (SELECT 1 FROM impianti.impianti i
                        JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
                        JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
                        WHERE i.impianto_id = @impianto AND i.eliminato_il IS NULL
                          AND s.eliminata_il IS NULL AND c.eliminato_il IS NULL
                          AND (i.stato = 'attivo' OR i.impianto_id = @impiantoAttuale::uuid)) AS impianto,
                (@generatore::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM impianti.generatori g WHERE g.generatore_id = @generatore AND g.impianto_id = @impianto)) AS generatore,
                (@tecnico::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM sec.utenti u WHERE u.utente_id = @tecnico AND {TecnicoAssegnabile})) AS tecnico
            """,
            new { impianto = dati.ImpiantoId, impiantoAttuale, generatore = dati.GeneratoreId, tecnico = dati.TecnicoUtenteId }, t, ct));

        var errori = new List<ErroreIntervento>();
        if (!controllo.Impianto)
        {
            errori.Add(ErroreIntervento.ImpiantoInterventoNonValido);
        }

        if (!controllo.Generatore)
        {
            errori.Add(ErroreIntervento.GeneratoreInterventoNonValido);
        }

        if (!controllo.Tecnico)
        {
            errori.Add(ErroreIntervento.TecnicoNonValido);
        }

        return errori;
    }

    private static object Parametri(Contesto contesto, DatiIntervento dati) => new
    {
        aziendaId = contesto.AziendaId,
        utenteId = contesto.UtenteId,
        impianto = dati.ImpiantoId,
        generatore = dati.GeneratoreId,
        tipo = dati.Tipo,
        descrizione = dati.Descrizione,
        tecnico = dati.TecnicoUtenteId,
        // timestamptz vuole un DateTime in UTC.
        inizio = dati.Inizio?.UtcDateTime,
        fine = dati.Fine?.UtcDateTime,
    };

    private async Task<EsitoIntervento> Scrivi(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<EsitoIntervento>> lavoro, CancellationToken ct)
    {
        try
        {
            return await Esegui(contesto, lavoro, ct);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneEsclusione && e.ConstraintName == "ex_interventi_tecnico_sovrapposti")
        {
            // La transazione è già annullata: l'eccezione è uscita prima del COMMIT.
            return new EsitoIntervento.TecnicoOccupato();
        }
        catch (PostgresException e) when (e.SqlState == SenzaCertificatoFgas)
        {
            // Impianto con apparecchiature F-Gas e tecnico senza certificato in corso (migrazione 0015, ADR 0008).
            return new EsitoIntervento.NonValida([ErroreIntervento.TecnicoSenzaCertificatoFgas]);
        }
    }

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class RigaScritta
    {
        public Guid Id { get; init; }

        public int Versione { get; init; }

        public int Anno { get; init; }

        public int Numero { get; init; }

        public EsitoIntervento Esito() => new EsitoIntervento.Riuscita(Id, Versione, Anno, Numero);
    }

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }

        public Guid ImpiantoId { get; init; }

        public Guid? GeneratoreId { get; init; }

        public string Tipo { get; init; } = "";
    }

    private sealed class ControlloRiferimenti
    {
        public bool Impianto { get; init; }

        public bool Generatore { get; init; }

        public bool Tecnico { get; init; }
    }
}

internal static class EstensioniParametri
{
    public static DynamicParameters Aggiungi(this DynamicParameters parametri, string nome, object? valore)
    {
        parametri.Add(nome, valore);
        return parametri;
    }
}

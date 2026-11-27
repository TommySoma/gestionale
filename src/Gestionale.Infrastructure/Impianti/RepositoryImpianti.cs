using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Impianti;
using Gestionale.Domain.Impianti;
using Gestionale.Infrastructure.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Impianti;

/// <summary>
/// Impianti con Dapper (ADR 0003). Ogni metodo è una transazione con azienda e
/// utente impostati: la Row-Level Security limita tutto all'azienda corrente.
/// Prima di scrivere si controllano sede, clienti e voci delle lookup, per
/// rispondere con un errore chiaro invece che con una violazione di chiave.
/// Gli avvisi (indirizzo o matricola duplicati) si calcolano nella stessa transazione.
/// </summary>
public sealed class RepositoryImpianti(Database.Database db) : IRepositoryImpianti
{
    private const string ViolazioneUnicita = "23505";

    // Un impianto si vede se non è eliminato e la sua sede e il suo cliente esistono ancora.
    private const string ImpiantoVisibile = """
        i.eliminato_il IS NULL
        AND EXISTS (SELECT 1 FROM anagrafica.sedi vs JOIN anagrafica.clienti vc ON vc.cliente_id = vs.cliente_id
                    WHERE vs.sede_id = i.sede_id AND vs.eliminata_il IS NULL AND vc.eliminato_il IS NULL)
        """;

    // Come nei controlli del catasto: un altro impianto attivo dello stesso tipo allo stesso indirizzo.
    private const string IndirizzoDuplicato = """
        (i.stato = 'attivo' AND EXISTS (
            SELECT 1
            FROM impianti.impianti a
            JOIN anagrafica.sedi sa ON sa.sede_id = a.sede_id
            WHERE a.impianto_id <> i.impianto_id
              AND a.tipo_impianto_id = i.tipo_impianto_id
              AND a.stato = 'attivo' AND a.eliminato_il IS NULL AND sa.eliminata_il IS NULL
              AND sa.cap = s.cap
              AND lower(btrim(sa.comune)) = lower(btrim(s.comune))
              AND lower(btrim(sa.indirizzo)) = lower(btrim(s.indirizzo))
              AND lower(coalesce(btrim(sa.civico), '')) = lower(coalesce(btrim(s.civico), ''))))
        """;

    private const string MatricolaDuplicata = """
        (g.matricola IS NOT NULL AND EXISTS (
            SELECT 1
            FROM impianti.generatori x
            JOIN impianti.impianti xi ON xi.impianto_id = x.impianto_id
            WHERE x.generatore_id <> g.generatore_id
              AND x.azienda_id = g.azienda_id
              AND lower(x.matricola) = lower(g.matricola)
              AND xi.eliminato_il IS NULL))
        """;

    private const string ValidaOggi = "valido_dal <= current_date AND (valido_al IS NULL OR valido_al >= current_date)";

    public Task<Pagina<RiepilogoImpianto>> CercaAsync(Contesto contesto, FiltroImpianti filtro, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            const string condizioni = """
                FROM impianti.impianti i
                JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
                JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
                JOIN impianti.tipi_impianto ti ON ti.tipo_impianto_id = i.tipo_impianto_id
                WHERE i.eliminato_il IS NULL AND s.eliminata_il IS NULL AND c.eliminato_il IS NULL
                  AND (@clienteId::uuid IS NULL OR c.cliente_id = @clienteId)
                  AND (@stato::text IS NULL OR i.stato = @stato)
                  AND (@tecnico::uuid IS NULL OR EXISTS (
                       SELECT 1 FROM lavoro.interventi x
                       WHERE x.impianto_id = i.impianto_id AND x.tecnico_utente_id = @tecnico AND x.stato <> 'annullato'))
                  AND (@testo::text IS NULL
                       OR c.nome_ricerca LIKE @modello
                       OR lower(s.indirizzo || ' ' || coalesce(s.civico || ' ', '') || s.comune) LIKE @modello
                       OR i.pdr = @pdr
                       OR EXISTS (SELECT 1 FROM impianti.riferimenti_catasto r
                                  WHERE r.impianto_id = i.impianto_id AND lower(r.codice) = lower(@testo))
                       OR EXISTS (SELECT 1 FROM impianti.generatori g
                                  WHERE g.impianto_id = i.impianto_id AND lower(g.matricola) = lower(@testo)))
                """;

            var parametri = new
            {
                clienteId = filtro.ClienteId,
                stato = filtro.Stato,
                tecnico = filtro.TecnicoUtenteId,
                testo = filtro.Testo,
                modello = filtro.Testo is null ? null : "%" + EscapeLike(filtro.Testo.ToLowerInvariant()) + "%",
                pdr = filtro.Testo is null ? null : string.Concat(filtro.Testo.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant(),
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + condizioni, parametri, t, ct));
            var elementi = await c.QueryAsync<RiepilogoImpianto>(Comando(
                $"""
                SELECT i.impianto_id, ti.nome AS tipo_impianto, i.stato, i.sede_id, c.cliente_id, {NomeCliente("c")} AS cliente,
                       s.indirizzo, s.civico, s.comune, s.provincia, i.pdr, i.versione,
                       (SELECT r.codice FROM impianti.riferimenti_catasto r
                        WHERE r.impianto_id = i.impianto_id ORDER BY r.regione LIMIT 1) AS codice_catasto,
                       (SELECT count(*)::int FROM impianti.generatori g
                        WHERE g.impianto_id = i.impianto_id AND g.dismesso_il IS NULL) AS numero_generatori
                {condizioni}
                ORDER BY c.nome_ricerca, s.comune, s.indirizzo, i.creato_il, i.impianto_id
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<RiepilogoImpianto>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<Impianto?> ImpiantoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var impianto = await c.QuerySingleOrDefaultAsync<Impianto>(Comando(
                $"""
                SELECT i.impianto_id, i.sede_id, c.cliente_id, {NomeCliente("c")} AS cliente,
                       s.indirizzo, s.civico, s.cap, s.comune, s.provincia,
                       i.tipo_impianto_id, ti.nome AS tipo_impianto, ti.da_verificare AS tipo_da_verificare,
                       i.stato, i.anno_installazione, i.potenza_utile_kw, i.combustibile_id, cb.nome AS combustibile, i.pdr,
                       i.proprietario_cliente_id, {NomeCliente("p")} AS proprietario,
                       i.responsabile_cliente_id, {NomeCliente("r")} AS responsabile,
                       i.note, i.dismesso_il, i.manutenzione_mesi, i.versione,
                       {IndirizzoDuplicato} AS indirizzo_duplicato
                FROM impianti.impianti i
                JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
                JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
                JOIN impianti.tipi_impianto ti ON ti.tipo_impianto_id = i.tipo_impianto_id
                LEFT JOIN impianti.combustibili cb ON cb.combustibile_id = i.combustibile_id
                LEFT JOIN anagrafica.clienti p ON p.cliente_id = i.proprietario_cliente_id
                LEFT JOIN anagrafica.clienti r ON r.cliente_id = i.responsabile_cliente_id
                WHERE i.impianto_id = @impiantoId AND {ImpiantoVisibile}
                """,
                new { impiantoId }, t, ct));

            if (impianto is null)
            {
                return null;
            }

            impianto.Generatori = [.. await c.QueryAsync<Generatore>(Comando(
                $"""
                SELECT g.generatore_id, g.tipo_generatore_id, tg.nome AS tipo_generatore, g.marca, g.modello, g.matricola,
                       g.potenza_kw, g.combustibile_id, cb.nome AS combustibile, g.installato_il, g.dismesso_il, g.note, g.sigla_catasto, g.versione,
                       {MatricolaDuplicata} AS matricola_duplicata
                FROM impianti.generatori g
                JOIN impianti.tipi_generatore tg ON tg.tipo_generatore_id = g.tipo_generatore_id
                LEFT JOIN impianti.combustibili cb ON cb.combustibile_id = g.combustibile_id
                WHERE g.impianto_id = @impiantoId
                ORDER BY g.dismesso_il IS NOT NULL, g.installato_il NULLS LAST, g.creato_il, g.generatore_id
                """,
                new { impiantoId }, t, ct))];

            impianto.RiferimentiCatasto = [.. await c.QueryAsync<RiferimentoCatasto>(Comando(
                """
                SELECT riferimento_catasto_id, regione, sistema, codice, registrato_il, versione
                FROM impianti.riferimenti_catasto
                WHERE impianto_id = @impiantoId
                ORDER BY regione
                """,
                new { impiantoId }, t, ct))];

            return impianto;
        }, ct);

    public Task<bool> AssegnatoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.ExecuteScalarAsync<bool>(Comando(
            """
            SELECT EXISTS (SELECT 1 FROM lavoro.interventi
                           WHERE impianto_id = @impiantoId AND tecnico_utente_id = @utenteId AND stato <> 'annullato')
            """,
            new { impiantoId, utenteId = contesto.UtenteId }, t, ct)), ct);

    public Task<VociImpianti> VociAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            async Task<IReadOnlyList<VoceLookup>> Voci(string tabella, string chiave) =>
                [.. await c.QueryAsync<VoceLookup>(Comando(
                    $"SELECT {chiave} AS id, codice, nome, da_verificare FROM impianti.{tabella} WHERE {ValidaOggi} ORDER BY nome, codice",
                    null, t, ct))];

            return new VociImpianti(
                await Voci("tipi_impianto", "tipo_impianto_id"),
                await Voci("tipi_generatore", "tipo_generatore_id"),
                await Voci("combustibili", "combustibile_id"));
        }, ct);

    public Task<EsitoImpianto> CreaAsync(Contesto contesto, DatiImpianto dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (await ErroriImpiantoAsync(c, t, dati, null, ct) is { Count: > 0 } errori)
            {
                return new EsitoImpianto.NonValida(errori);
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, stato, anno_installazione, potenza_utile_kw,
                                               combustibile_id, pdr, proprietario_cliente_id, responsabile_cliente_id, note,
                                               dismesso_il, manutenzione_mesi, creato_da)
                VALUES (@aziendaId, @SedeId, @TipoImpiantoId, @Stato, @AnnoInstallazione, @PotenzaUtileKw,
                        @CombustibileId, @Pdr, @ProprietarioClienteId, @ResponsabileClienteId, @Note,
                        @DismessoIl, @ManutenzioneMesi, @utenteId)
                RETURNING impianto_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, utenteId = contesto.UtenteId }), t, ct));

            return await RiuscitaImpiantoAsync(c, t, riga, ct);
        }, ct);

    public Task<EsitoImpianto> AggiornaAsync(Contesto contesto, Guid impiantoId, int versione, DatiImpianto dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuali = await c.QuerySingleOrDefaultAsync<VociAttuali>(Comando(
                $"SELECT i.tipo_impianto_id AS tipo, i.combustibile_id AS combustibile FROM impianti.impianti i WHERE i.impianto_id = @impiantoId AND {ImpiantoVisibile}",
                new { impiantoId }, t, ct));
            if (attuali is null)
            {
                return new EsitoImpianto.NonTrovata();
            }

            if (await ErroriImpiantoAsync(c, t, dati, attuali, ct) is { Count: > 0 } errori)
            {
                return new EsitoImpianto.NonValida(errori);
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE impianti.impianti
                SET sede_id = @SedeId, tipo_impianto_id = @TipoImpiantoId, stato = @Stato, anno_installazione = @AnnoInstallazione,
                    potenza_utile_kw = @PotenzaUtileKw, combustibile_id = @CombustibileId, pdr = @Pdr,
                    proprietario_cliente_id = @ProprietarioClienteId, responsabile_cliente_id = @ResponsabileClienteId,
                    note = @Note, dismesso_il = @DismessoIl, manutenzione_mesi = @ManutenzioneMesi, modificato_da = @utenteId
                WHERE impianto_id = @impiantoId AND versione = @versione AND eliminato_il IS NULL
                RETURNING impianto_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { impiantoId, versione, utenteId = contesto.UtenteId }), t, ct));

            return riga is null ? new EsitoImpianto.VersioneSuperata() : await RiuscitaImpiantoAsync(c, t, riga, ct);
        }, ct);

    public Task<EsitoImpianto> EliminaAsync(Contesto contesto, Guid impiantoId, int versione, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await ImpiantoEsisteAsync(c, t, impiantoId, ct))
            {
                return new EsitoImpianto.NonTrovata();
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE impianti.impianti
                SET eliminato_il = now(), eliminato_da = @utenteId, modificato_da = @utenteId
                WHERE impianto_id = @impiantoId AND versione = @versione AND eliminato_il IS NULL
                RETURNING impianto_id AS id, versione
                """,
                new { impiantoId, versione, utenteId = contesto.UtenteId }, t, ct));

            return riga is null ? new EsitoImpianto.VersioneSuperata() : new EsitoImpianto.Riuscita(riga.Id, riga.Versione, []);
        }, ct);

    public Task<EsitoImpianto> CreaGeneratoreAsync(Contesto contesto, Guid impiantoId, DatiGeneratore dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await ImpiantoEsisteAsync(c, t, impiantoId, ct))
            {
                return new EsitoImpianto.NonTrovata();
            }

            if (await ErroriGeneratoreAsync(c, t, dati, null, ct) is { Count: > 0 } errori)
            {
                return new EsitoImpianto.NonValida(errori);
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO impianti.generatori (azienda_id, impianto_id, tipo_generatore_id, marca, modello, matricola, potenza_kw,
                                                 combustibile_id, installato_il, dismesso_il, note, sigla_catasto, creato_da)
                VALUES (@aziendaId, @impiantoId, @TipoGeneratoreId, @Marca, @Modello, @Matricola, @PotenzaKw,
                        @CombustibileId, @InstallatoIl, @DismessoIl, @Note, @SiglaCatasto, @utenteId)
                RETURNING generatore_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, impiantoId, utenteId = contesto.UtenteId }), t, ct));

            return await RiuscitaGeneratoreAsync(c, t, riga, ct);
        }, ct);

    public Task<EsitoImpianto> AggiornaGeneratoreAsync(
        Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, DatiGeneratore dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuali = await c.QuerySingleOrDefaultAsync<VociAttuali>(Comando(
                $"""
                SELECT g.tipo_generatore_id AS tipo, g.combustibile_id AS combustibile
                FROM impianti.generatori g
                JOIN impianti.impianti i ON i.impianto_id = g.impianto_id
                WHERE g.generatore_id = @generatoreId AND g.impianto_id = @impiantoId AND {ImpiantoVisibile}
                """,
                new { impiantoId, generatoreId }, t, ct));
            if (attuali is null)
            {
                return new EsitoImpianto.NonTrovata();
            }

            if (await ErroriGeneratoreAsync(c, t, dati, attuali, ct) is { Count: > 0 } errori)
            {
                return new EsitoImpianto.NonValida(errori);
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE impianti.generatori
                SET tipo_generatore_id = @TipoGeneratoreId, marca = @Marca, modello = @Modello, matricola = @Matricola,
                    potenza_kw = @PotenzaKw, combustibile_id = @CombustibileId, installato_il = @InstallatoIl,
                    dismesso_il = @DismessoIl, note = @Note, sigla_catasto = @SiglaCatasto, modificato_da = @utenteId
                WHERE generatore_id = @generatoreId AND impianto_id = @impiantoId AND versione = @versione
                RETURNING generatore_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { impiantoId, generatoreId, versione, utenteId = contesto.UtenteId }), t, ct));

            return riga is null ? new EsitoImpianto.VersioneSuperata() : await RiuscitaGeneratoreAsync(c, t, riga, ct);
        }, ct);

    public Task<EsitoImpianto> CreaRiferimentoAsync(Contesto contesto, Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await ImpiantoEsisteAsync(c, t, impiantoId, ct))
            {
                return new EsitoImpianto.NonTrovata();
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice, registrato_il, creato_da)
                VALUES (@aziendaId, @impiantoId, @Regione, @Sistema, @Codice, @RegistratoIl, @utenteId)
                RETURNING riferimento_catasto_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { aziendaId = contesto.AziendaId, impiantoId, utenteId = contesto.UtenteId }), t, ct));

            return new EsitoImpianto.Riuscita(riga.Id, riga.Versione, []);
        }, ct);

    public Task<EsitoImpianto> AggiornaRiferimentoAsync(
        Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, DatiRiferimentoCatasto dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await RiferimentoEsisteAsync(c, t, impiantoId, riferimentoId, ct))
            {
                return new EsitoImpianto.NonTrovata();
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE impianti.riferimenti_catasto
                SET regione = @Regione, sistema = @Sistema, codice = @Codice, registrato_il = @RegistratoIl, modificato_da = @utenteId
                WHERE riferimento_catasto_id = @riferimentoId AND impianto_id = @impiantoId AND versione = @versione
                RETURNING riferimento_catasto_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { impiantoId, riferimentoId, versione, utenteId = contesto.UtenteId }), t, ct));

            return riga is null ? new EsitoImpianto.VersioneSuperata() : new EsitoImpianto.Riuscita(riga.Id, riga.Versione, []);
        }, ct);

    public Task<EsitoImpianto> EliminaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await RiferimentoEsisteAsync(c, t, impiantoId, riferimentoId, ct))
            {
                return new EsitoImpianto.NonTrovata();
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                DELETE FROM impianti.riferimenti_catasto
                WHERE riferimento_catasto_id = @riferimentoId AND impianto_id = @impiantoId AND versione = @versione
                RETURNING riferimento_catasto_id AS id, versione
                """,
                new { impiantoId, riferimentoId, versione }, t, ct));

            return riga is null ? new EsitoImpianto.VersioneSuperata() : new EsitoImpianto.Riuscita(riga.Id, riga.Versione, []);
        }, ct);

    /// <summary>
    /// Sede, clienti e voci delle lookup. Una voce chiusa (valido_al passato) non si
    /// sceglie più, ma resta accettata sull'impianto che la usa già.
    /// </summary>
    private static async Task<IReadOnlyList<ErroreImpianto>> ErroriImpiantoAsync(
        NpgsqlConnection c, NpgsqlTransaction t, DatiImpianto dati, VociAttuali? attuali, CancellationToken ct)
    {
        var esito = await c.QuerySingleAsync<ControlloRiferimenti>(Comando(
            $"""
            SELECT
                EXISTS (SELECT 1 FROM anagrafica.sedi s JOIN anagrafica.clienti c ON c.cliente_id = s.cliente_id
                        WHERE s.sede_id = @SedeId AND s.eliminata_il IS NULL AND c.eliminato_il IS NULL) AS sede,
                EXISTS (SELECT 1 FROM impianti.tipi_impianto
                        WHERE tipo_impianto_id = @TipoImpiantoId AND ({ValidaOggi} OR tipo_impianto_id = @tipoAttuale)) AS tipo,
                (@CombustibileId::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM impianti.combustibili
                        WHERE combustibile_id = @CombustibileId AND ({ValidaOggi} OR combustibile_id = @combustibileAttuale))) AS combustibile,
                (@ProprietarioClienteId::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM anagrafica.clienti WHERE cliente_id = @ProprietarioClienteId AND eliminato_il IS NULL)) AS proprietario,
                (@ResponsabileClienteId::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM anagrafica.clienti WHERE cliente_id = @ResponsabileClienteId AND eliminato_il IS NULL)) AS responsabile
            """,
            new DynamicParameters(dati).Con(new { tipoAttuale = attuali?.Tipo, combustibileAttuale = attuali?.Combustibile }), t, ct));

        var errori = new List<ErroreImpianto>();
        Aggiungi(errori, esito.Sede, ErroreImpianto.SedeNonValida);
        Aggiungi(errori, esito.Tipo, ErroreImpianto.TipoImpiantoNonValido);
        Aggiungi(errori, esito.Combustibile, ErroreImpianto.CombustibileNonValido);
        Aggiungi(errori, esito.Proprietario, ErroreImpianto.ProprietarioNonValido);
        Aggiungi(errori, esito.Responsabile, ErroreImpianto.ResponsabileNonValido);
        return errori;
    }

    private static async Task<IReadOnlyList<ErroreImpianto>> ErroriGeneratoreAsync(
        NpgsqlConnection c, NpgsqlTransaction t, DatiGeneratore dati, VociAttuali? attuali, CancellationToken ct)
    {
        var esito = await c.QuerySingleAsync<ControlloRiferimenti>(Comando(
            $"""
            SELECT
                EXISTS (SELECT 1 FROM impianti.tipi_generatore
                        WHERE tipo_generatore_id = @TipoGeneratoreId AND ({ValidaOggi} OR tipo_generatore_id = @tipoAttuale)) AS tipo,
                (@CombustibileId::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM impianti.combustibili
                        WHERE combustibile_id = @CombustibileId AND ({ValidaOggi} OR combustibile_id = @combustibileAttuale))) AS combustibile
            """,
            new DynamicParameters(dati).Con(new { tipoAttuale = attuali?.Tipo, combustibileAttuale = attuali?.Combustibile }), t, ct));

        var errori = new List<ErroreImpianto>();
        Aggiungi(errori, esito.Tipo, ErroreImpianto.TipoGeneratoreNonValido);
        Aggiungi(errori, esito.Combustibile, ErroreImpianto.CombustibileNonValido);
        return errori;
    }

    private static void Aggiungi(List<ErroreImpianto> errori, bool valido, ErroreImpianto errore)
    {
        if (!valido)
        {
            errori.Add(errore);
        }
    }

    private static async Task<EsitoImpianto> RiuscitaImpiantoAsync(NpgsqlConnection c, NpgsqlTransaction t, RigaScritta riga, CancellationToken ct)
    {
        var duplicato = await c.ExecuteScalarAsync<bool>(Comando(
            $"""
            SELECT {IndirizzoDuplicato}
            FROM impianti.impianti i JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
            WHERE i.impianto_id = @id
            """,
            new { id = riga.Id }, t, ct));
        return new EsitoImpianto.Riuscita(riga.Id, riga.Versione, duplicato ? [AvvisoImpianto.IndirizzoDuplicato] : []);
    }

    private static async Task<EsitoImpianto> RiuscitaGeneratoreAsync(NpgsqlConnection c, NpgsqlTransaction t, RigaScritta riga, CancellationToken ct)
    {
        var duplicata = await c.ExecuteScalarAsync<bool>(Comando(
            $"SELECT {MatricolaDuplicata} FROM impianti.generatori g WHERE g.generatore_id = @id",
            new { id = riga.Id }, t, ct));
        return new EsitoImpianto.Riuscita(riga.Id, riga.Versione, duplicata ? [AvvisoImpianto.MatricolaDuplicata] : []);
    }

    private static Task<bool> ImpiantoEsisteAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid impiantoId, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(Comando(
            $"SELECT EXISTS (SELECT 1 FROM impianti.impianti i WHERE i.impianto_id = @impiantoId AND {ImpiantoVisibile})",
            new { impiantoId }, t, ct));

    private static Task<bool> RiferimentoEsisteAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid impiantoId, Guid riferimentoId, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(Comando(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM impianti.riferimenti_catasto r
                JOIN impianti.impianti i ON i.impianto_id = r.impianto_id
                WHERE r.riferimento_catasto_id = @riferimentoId AND r.impianto_id = @impiantoId AND {ImpiantoVisibile})
            """,
            new { impiantoId, riferimentoId }, t, ct));

    private async Task<EsitoImpianto> Scrivi(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<EsitoImpianto>> lavoro, CancellationToken ct)
    {
        try
        {
            return await Esegui(contesto, lavoro, ct);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita && Duplicato(e.ConstraintName) is { } motivo)
        {
            // La transazione è già annullata: l'eccezione è uscita prima del COMMIT.
            return new EsitoImpianto.Duplicata(motivo);
        }
    }

    private static ErroreImpianto? Duplicato(string? vincolo) => vincolo switch
    {
        "uq_impianti_azienda_pdr_attivi" => ErroreImpianto.PdrInUso,
        "uq_riferimenti_catasto_codice" => ErroreImpianto.CodiceCatastoInUso,
        "uq_riferimenti_catasto_impianto_regione" => ErroreImpianto.RegioneGiaRegistrata,
        "uq_generatori_impianto_sigla_catasto" => ErroreImpianto.SiglaCatastoInUso,
        _ => null,
    };

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static string NomeCliente(string alias) =>
        $"CASE WHEN {alias}.tipo = 'privato' THEN {alias}.cognome || ' ' || {alias}.nome ELSE {alias}.denominazione END";

    private static string EscapeLike(string testo) =>
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

    private sealed class VociAttuali
    {
        public Guid Tipo { get; init; }

        public Guid? Combustibile { get; init; }
    }

    private sealed class ControlloRiferimenti
    {
        public bool Sede { get; init; } = true;

        public bool Tipo { get; init; } = true;

        public bool Combustibile { get; init; } = true;

        public bool Proprietario { get; init; } = true;

        public bool Responsabile { get; init; } = true;
    }
}

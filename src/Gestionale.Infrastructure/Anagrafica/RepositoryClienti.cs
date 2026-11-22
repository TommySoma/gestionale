using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Domain.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Anagrafica;

/// <summary>
/// Anagrafiche con Dapper (ADR 0003). Ogni metodo è una transazione con azienda e
/// utente impostati: la Row-Level Security limita tutto all'azienda corrente.
/// Le scritture usano la concorrenza ottimistica sulla colonna versione.
/// </summary>
public sealed class RepositoryClienti(Database.Database db) : IRepositoryClienti
{
    private const string ViolazioneUnicita = "23505";

    public Task<Pagina<RiepilogoCliente>> CercaAsync(
        Contesto contesto, string? testo, string? tipo, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            const string filtro = """
                FROM anagrafica.clienti c
                WHERE c.eliminato_il IS NULL
                  AND (@tipo::text IS NULL OR c.tipo = @tipo)
                  AND (@testo::text IS NULL
                       OR c.nome_ricerca LIKE @modello
                       OR c.codice_fiscale = upper(@testo)
                       OR c.partita_iva = @testo)
                """;

            var parametri = new
            {
                tipo,
                testo,
                modello = testo is null ? null : "%" + EscapeLike(testo.ToLowerInvariant()) + "%",
                dimensione,
                salta = (numeroPagina - 1) * dimensione,
            };

            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) " + filtro, parametri, t, ct));
            var elementi = await c.QueryAsync<RiepilogoCliente>(Comando(
                """
                SELECT c.cliente_id, c.tipo,
                       CASE WHEN c.tipo = 'privato' THEN c.cognome || ' ' || c.nome ELSE c.denominazione END AS nome_visualizzato,
                       c.codice_fiscale, c.partita_iva, c.versione,
                       (SELECT s.comune FROM anagrafica.sedi s
                        WHERE s.cliente_id = c.cliente_id AND s.eliminata_il IS NULL
                        ORDER BY s.creato_il, s.sede_id LIMIT 1) AS comune_principale
                """ + "\n" + filtro + "\n" + """
                ORDER BY c.nome_ricerca, c.cliente_id
                LIMIT @dimensione OFFSET @salta
                """,
                parametri, t, ct));

            return new Pagina<RiepilogoCliente>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<Cliente?> ClienteAsync(Contesto contesto, Guid clienteId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var cliente = await c.QuerySingleOrDefaultAsync<Cliente>(Comando(
                """
                SELECT cliente_id, tipo, denominazione, nome, cognome, codice_fiscale, partita_iva, pec, note, versione
                FROM anagrafica.clienti
                WHERE cliente_id = @clienteId AND eliminato_il IS NULL
                """,
                new { clienteId }, t, ct));

            if (cliente is null)
            {
                return null;
            }

            cliente.Sedi = [.. await c.QueryAsync<Sede>(Comando(
                """
                SELECT sede_id, descrizione, indirizzo, civico, cap, comune, provincia, note_accesso, latitudine, longitudine, versione
                FROM anagrafica.sedi
                WHERE cliente_id = @clienteId AND eliminata_il IS NULL
                ORDER BY creato_il, sede_id
                """,
                new { clienteId }, t, ct))];

            cliente.Contatti = [.. await c.QueryAsync<Contatto>(Comando(
                """
                SELECT contatto_id, tipo, valore, etichetta, principale, versione
                FROM anagrafica.contatti
                WHERE cliente_id = @clienteId
                ORDER BY tipo, NOT principale, creato_il, contatto_id
                """,
                new { clienteId }, t, ct))];

            cliente.Consensi = [.. await c.QueryAsync<ConsensoCorrente>(Comando(
                "SELECT finalita, concesso, fonte, registrato_il FROM anagrafica.v_consensi_correnti WHERE cliente_id = @clienteId ORDER BY finalita",
                new { clienteId }, t, ct))];

            return cliente;
        }, ct);

    public Task<EsitoScrittura> CreaAsync(Contesto contesto, DatiCliente dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione, nome, cognome, codice_fiscale, partita_iva, pec, note, creato_da)
            VALUES (@AziendaId, @Tipo, @Denominazione, @Nome, @Cognome, @CodiceFiscale, @PartitaIva, @Pec, @Note, @UtenteId)
            RETURNING cliente_id AS id, versione
            """,
            Parametri(contesto, dati), t, ct)), esisteQuery: null, ct);

    public Task<EsitoScrittura> AggiornaAsync(Contesto contesto, Guid clienteId, int versione, DatiCliente dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            UPDATE anagrafica.clienti
            SET tipo = @Tipo, denominazione = @Denominazione, nome = @Nome, cognome = @Cognome,
                codice_fiscale = @CodiceFiscale, partita_iva = @PartitaIva, pec = @Pec, note = @Note,
                modificato_da = @UtenteId
            WHERE cliente_id = @clienteId AND versione = @versione AND eliminato_il IS NULL
            RETURNING cliente_id AS id, versione
            """,
            new DynamicParameters(Parametri(contesto, dati)).Con(new { clienteId, versione }), t, ct)),
            EsisteCliente(clienteId), ct);

    public Task<EsitoScrittura> EliminaAsync(Contesto contesto, Guid clienteId, int versione, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
            await c.ExecuteScalarAsync<bool>(Comando("SELECT anagrafica.elimina_cliente(@clienteId, @versione)", new { clienteId, versione }, t, ct))
                ? new RigaScritta { Id = clienteId, Versione = versione + 1 }
                : null,
            EsisteCliente(clienteId), ct);

    public Task<EsitoScrittura> CreaSedeAsync(Contesto contesto, Guid clienteId, DatiSede dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            INSERT INTO anagrafica.sedi (azienda_id, cliente_id, descrizione, indirizzo, civico, cap, comune, provincia,
                                         note_accesso, latitudine, longitudine, creato_da)
            SELECT c.azienda_id, c.cliente_id, @Descrizione, @Indirizzo, @Civico, @Cap, @Comune, @Provincia,
                   @NoteAccesso, @Latitudine, @Longitudine, @utenteId
            FROM anagrafica.clienti c
            WHERE c.cliente_id = @clienteId AND c.eliminato_il IS NULL
            RETURNING sede_id AS id, versione
            """,
            new DynamicParameters(dati).Con(new { clienteId, utenteId = contesto.UtenteId }), t, ct)),
            esisteQuery: null, ct);

    public Task<EsitoScrittura> AggiornaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, DatiSede dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            UPDATE anagrafica.sedi
            SET descrizione = @Descrizione, indirizzo = @Indirizzo, civico = @Civico, cap = @Cap, comune = @Comune,
                provincia = @Provincia, note_accesso = @NoteAccesso, latitudine = @Latitudine, longitudine = @Longitudine,
                modificato_da = @utenteId
            WHERE sede_id = @sedeId AND cliente_id = @clienteId AND versione = @versione AND eliminata_il IS NULL
            RETURNING sede_id AS id, versione
            """,
            new DynamicParameters(dati).Con(new { clienteId, sedeId, versione, utenteId = contesto.UtenteId }), t, ct)),
            EsisteSede(clienteId, sedeId), ct);

    public Task<EsitoScrittura> EliminaSedeAsync(Contesto contesto, Guid clienteId, Guid sedeId, int versione, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            UPDATE anagrafica.sedi
            SET eliminata_il = now(), eliminata_da = @utenteId
            WHERE sede_id = @sedeId AND cliente_id = @clienteId AND versione = @versione AND eliminata_il IS NULL
            RETURNING sede_id AS id, versione
            """,
            new { clienteId, sedeId, versione, utenteId = contesto.UtenteId }, t, ct)),
            EsisteSede(clienteId, sedeId), ct);

    public Task<EsitoScrittura> CreaContattoAsync(Contesto contesto, Guid clienteId, DatiContatto dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, etichetta, principale, creato_da)
            SELECT c.azienda_id, c.cliente_id, @Tipo, @Valore, @Etichetta, @Principale, @utenteId
            FROM anagrafica.clienti c
            WHERE c.cliente_id = @clienteId AND c.eliminato_il IS NULL
            RETURNING contatto_id AS id, versione
            """,
            new DynamicParameters(dati).Con(new { clienteId, utenteId = contesto.UtenteId }), t, ct)),
            esisteQuery: null, ct);

    public Task<EsitoScrittura> AggiornaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, DatiContatto dati, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            UPDATE anagrafica.contatti
            SET tipo = @Tipo, valore = @Valore, etichetta = @Etichetta, principale = @Principale, modificato_da = @utenteId
            WHERE contatto_id = @contattoId AND cliente_id = @clienteId AND versione = @versione
            RETURNING contatto_id AS id, versione
            """,
            new DynamicParameters(dati).Con(new { clienteId, contattoId, versione, utenteId = contesto.UtenteId }), t, ct)),
            EsisteContatto(clienteId, contattoId), ct);

    public Task<EsitoScrittura> EliminaContattoAsync(Contesto contesto, Guid clienteId, Guid contattoId, int versione, CancellationToken ct) =>
        Scrivi(contesto, (c, t) => c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
            """
            DELETE FROM anagrafica.contatti
            WHERE contatto_id = @contattoId AND cliente_id = @clienteId AND versione = @versione
            RETURNING contatto_id AS id, versione
            """,
            new { clienteId, contattoId, versione }, t, ct)),
            EsisteContatto(clienteId, contattoId), ct);

    public async Task<IReadOnlyList<FinalitaPrivacy>> FinalitaAsync(Contesto contesto, CancellationToken ct) =>
        [.. await Esegui(contesto, (c, t) => c.QueryAsync<FinalitaPrivacy>(Comando(
            "SELECT codice, nome, descrizione FROM anagrafica.finalita_privacy WHERE attiva ORDER BY nome", null, t, ct)), ct)];

    public Task<EsitoScrittura> RegistraConsensoAsync(Contesto contesto, Guid clienteId, DatiConsenso dati, CancellationToken ct) =>
        Esegui<EsitoScrittura>(contesto, async (c, t) =>
        {
            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                INSERT INTO anagrafica.consensi_privacy (azienda_id, cliente_id, finalita_privacy_id, concesso, fonte, note, registrato_da)
                SELECT c.azienda_id, c.cliente_id, f.finalita_privacy_id, @Concesso, @Fonte, @Note, @utenteId
                FROM anagrafica.clienti c
                CROSS JOIN anagrafica.finalita_privacy f
                WHERE c.cliente_id = @clienteId AND c.eliminato_il IS NULL AND f.codice = @Finalita AND f.attiva
                RETURNING consenso_id AS id, 1 AS versione
                """,
                new DynamicParameters(dati).Con(new { clienteId, utenteId = contesto.UtenteId }), t, ct));

            if (riga is not null)
            {
                return new EsitoScrittura.Riuscita(riga.Id, riga.Versione);
            }

            return await EsisteCliente(clienteId)(c, t, ct)
                ? new EsitoScrittura.NonValida([ErroreAnagrafica.FinalitaSconosciuta])
                : new EsitoScrittura.NonTrovata();
        }, ct);

    public Task<IReadOnlyList<VoceStoricoConsenso>?> StoricoConsensiAsync(Contesto contesto, Guid clienteId, CancellationToken ct) =>
        Esegui<IReadOnlyList<VoceStoricoConsenso>?>(contesto, async (c, t) =>
        {
            if (!await EsisteCliente(clienteId)(c, t, ct))
            {
                return null;
            }

            return [.. await c.QueryAsync<VoceStoricoConsenso>(Comando(
                """
                SELECT p.consenso_id, f.codice AS finalita, p.concesso, p.fonte, p.note, p.registrato_il
                FROM anagrafica.consensi_privacy p
                JOIN anagrafica.finalita_privacy f ON f.finalita_privacy_id = p.finalita_privacy_id
                WHERE p.cliente_id = @clienteId
                ORDER BY p.registrato_il DESC, p.consenso_id DESC
                """,
                new { clienteId }, t, ct))];
        }, ct);

    private delegate Task<bool> Esistenza(NpgsqlConnection connessione, NpgsqlTransaction transazione, CancellationToken ct);

    private static Esistenza EsisteCliente(Guid clienteId) => (c, t, ct) => c.ExecuteScalarAsync<bool>(Comando(
        "SELECT EXISTS (SELECT 1 FROM anagrafica.clienti WHERE cliente_id = @clienteId AND eliminato_il IS NULL)",
        new { clienteId }, t, ct));

    private static Esistenza EsisteSede(Guid clienteId, Guid sedeId) => (c, t, ct) => c.ExecuteScalarAsync<bool>(Comando(
        "SELECT EXISTS (SELECT 1 FROM anagrafica.sedi WHERE sede_id = @sedeId AND cliente_id = @clienteId AND eliminata_il IS NULL)",
        new { clienteId, sedeId }, t, ct));

    private static Esistenza EsisteContatto(Guid clienteId, Guid contattoId) => (c, t, ct) => c.ExecuteScalarAsync<bool>(Comando(
        "SELECT EXISTS (SELECT 1 FROM anagrafica.contatti WHERE contatto_id = @contattoId AND cliente_id = @clienteId)",
        new { clienteId, contattoId }, t, ct));

    /// <summary>
    /// Esegue una scrittura. Nessuna riga toccata significa: dato inesistente oppure
    /// versione superata, e lo si distingue controllando se il dato esiste ancora.
    /// </summary>
    private async Task<EsitoScrittura> Scrivi(
        Contesto contesto,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<RigaScritta?>> scrittura,
        Esistenza? esisteQuery,
        CancellationToken ct)
    {
        try
        {
            return await Esegui<EsitoScrittura>(contesto, async (c, t) =>
            {
                var riga = await scrittura(c, t);
                if (riga is not null)
                {
                    return new EsitoScrittura.Riuscita(riga.Id, riga.Versione);
                }

                return esisteQuery is not null && await esisteQuery(c, t, ct)
                    ? new EsitoScrittura.VersioneSuperata()
                    : new EsitoScrittura.NonTrovata();
            }, ct);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita)
        {
            // La transazione è già annullata: l'eccezione è uscita prima del COMMIT.
            return new EsitoScrittura.Duplicata(e.ConstraintName switch
            {
                "uq_contatti_principale" => ErroreAnagrafica.ContattoPrincipaleGiaPresente,
                _ => ErroreAnagrafica.PartitaIvaInUso,
            });
        }
    }

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static object Parametri(Contesto contesto, DatiCliente dati) => new
    {
        contesto.AziendaId,
        contesto.UtenteId,
        dati.Tipo,
        dati.Denominazione,
        dati.Nome,
        dati.Cognome,
        dati.CodiceFiscale,
        dati.PartitaIva,
        dati.Pec,
        dati.Note,
    };

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
}

internal static class EstensioniParametri
{
    /// <summary>Aggiunge altri parametri a quelli già presenti.</summary>
    public static DynamicParameters Con(this DynamicParameters parametri, object altri)
    {
        parametri.AddDynamicParams(altri);
        return parametri;
    }
}

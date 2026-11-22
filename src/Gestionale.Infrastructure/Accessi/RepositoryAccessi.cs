using System.Net;
using Dapper;
using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Sicurezza;
using Npgsql;

namespace Gestionale.Infrastructure.Accessi;

/// <summary>
/// Accessi su schema sec con Dapper. Le operazioni senza azienda nota
/// (dati di accesso, rotazione, primo avvio) passano da funzioni SECURITY DEFINER;
/// tutte le altre girano sotto Row-Level Security con l'azienda dell'utente.
/// </summary>
public sealed class RepositoryAccessi(Database.Database db) : IRepositoryAccessi
{
    private const string ViolazioneUnicita = "23505";
    private const string PrerequisitoMancante = "55000";

    public Task<bool> InstallazioneDaInizializzareAsync(CancellationToken ct) =>
        db.EseguiAsync(null, null, (c, t) =>
            c.ExecuteScalarAsync<bool>(Comando("SELECT sec.installazione_da_inizializzare()", null, t, ct)), ct);

    public async Task<Guid> InizializzaAsync(DatiInstallazione dati, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dati);

        try
        {
            return await db.EseguiAsync(null, null, (c, t) => c.ExecuteScalarAsync<Guid>(Comando(
                """
                SELECT sec.inizializza_installazione(
                    @RagioneSociale, @PartitaIva, @Regione,
                    @TitolareEmail, @TitolareNome, @TitolareCognome, @TitolarePasswordHash,
                    @AmministratoreEmail, @AmministratoreNome, @AmministratoreCognome, @AmministratorePasswordHash)
                """,
                dati, t, ct)), ct);
        }
        catch (PostgresException e) when (e.SqlState == PrerequisitoMancante)
        {
            throw new InstallazioneGiaInizializzataException(e.MessageText, e);
        }
    }

    public Task<DatiAccesso?> DatiAccessoAsync(string email, CancellationToken ct) =>
        db.EseguiAsync(null, null, (c, t) =>
            c.QuerySingleOrDefaultAsync<DatiAccesso>(Comando("SELECT * FROM sec.dati_accesso(@email)", new { email }, t, ct)), ct);

    public Task<StatoUtente?> StatoUtenteAsync(Guid aziendaId, Guid utenteId, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) => c.QuerySingleOrDefaultAsync<StatoUtente>(Comando(
            """
            SELECT email, stato, mfa_attivo, mfa_segreto_cifrato, bloccato_fino_al
            FROM sec.utenti
            WHERE utente_id = @utenteId
            """,
            new { utenteId }, t, ct)), ct);

    public Task<DateTime?> RegistraEsitoAccessoAsync(Guid aziendaId, Guid utenteId, bool riuscito, IPAddress? indirizzo, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, async (c, t) =>
        {
            var bloccatoFinoAl = await c.ExecuteScalarAsync<DateTime?>(Comando(
                "SELECT sec.registra_esito_accesso(@utenteId, @riuscito)", new { utenteId, riuscito }, t, ct));

            var azione = riuscito ? "accesso.riuscito" : bloccatoFinoAl is null ? "accesso.fallito" : "accesso.bloccato";
            await InserisciAuditAsync(c, t, aziendaId, utenteId, azione, indirizzo, ct);
            return bloccatoFinoAl;
        }, ct);

    public Task<UtenteAutenticato> ProfiloAsync(Guid aziendaId, Guid utenteId, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, async (c, t) =>
        {
            var anagrafica = await c.QuerySingleAsync<(string Email, string Nome, string Cognome)>(Comando(
                "SELECT email, nome, cognome FROM sec.utenti WHERE utente_id = @utenteId", new { utenteId }, t, ct));

            var ruoli = await c.QueryAsync<string>(Comando(
                """
                SELECT r.codice
                FROM sec.utenti_ruoli ur
                JOIN sec.ruoli r ON r.ruolo_id = ur.ruolo_id
                WHERE ur.utente_id = @utenteId
                ORDER BY r.codice
                """,
                new { utenteId }, t, ct));

            var permessi = await c.QueryAsync<(string Codice, string Ambito)>(Comando(
                "SELECT codice, ambito FROM sec.permessi_utente(@utenteId)", new { utenteId }, t, ct));

            return new UtenteAutenticato(
                utenteId,
                aziendaId,
                anagrafica.Email,
                anagrafica.Nome,
                anagrafica.Cognome,
                [.. ruoli],
                [.. permessi.Select(p => new PermessoEffettivo(p.Codice, p.Ambito == "propri" ? AmbitoPermesso.Propri : AmbitoPermesso.Tutti))]);
        }, ct);

    public Task<bool> RichiedeMfaAsync(Guid aziendaId, Guid utenteId, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) =>
            c.ExecuteScalarAsync<bool>(Comando("SELECT sec.richiede_mfa(@utenteId)", new { utenteId }, t, ct)), ct);

    public Task SalvaSegretoMfaAsync(Guid aziendaId, Guid utenteId, byte[] segretoCifrato, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) => c.ExecuteAsync(Comando(
            """
            UPDATE sec.utenti
            SET mfa_segreto_cifrato = @segretoCifrato, mfa_ultimo_passo = NULL, modificato_da = @utenteId
            WHERE utente_id = @utenteId AND NOT mfa_attivo
            """,
            new { utenteId, segretoCifrato }, t, ct)), ct);

    public Task<bool> RegistraPassoTotpAsync(Guid aziendaId, Guid utenteId, long passo, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) =>
            c.ExecuteScalarAsync<bool>(Comando("SELECT sec.registra_passo_totp(@utenteId, @passo)", new { utenteId, passo }, t, ct)), ct);

    public Task<bool> UsaCodiceRecuperoAsync(Guid aziendaId, Guid utenteId, byte[] codiceHash, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, async (c, t) =>
        {
            var usato = await c.ExecuteScalarAsync<bool>(Comando(
                "SELECT sec.usa_codice_recupero(@utenteId, @codiceHash)", new { utenteId, codiceHash }, t, ct));

            if (usato)
            {
                await InserisciAuditAsync(c, t, aziendaId, utenteId, "mfa.codice_recupero_usato", null, ct);
            }

            return usato;
        }, ct);

    public Task AttivaMfaAsync(Guid aziendaId, Guid utenteId, IReadOnlyList<byte[]> hashCodiciRecupero, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, async (c, t) =>
        {
            await c.ExecuteAsync(Comando(
                "UPDATE sec.utenti SET mfa_attivo = true, modificato_da = @utenteId WHERE utente_id = @utenteId",
                new { utenteId }, t, ct));

            await c.ExecuteAsync(Comando("DELETE FROM sec.codici_recupero WHERE utente_id = @utenteId", new { utenteId }, t, ct));

            await c.ExecuteAsync(Comando(
                "INSERT INTO sec.codici_recupero (azienda_id, utente_id, codice_hash) VALUES (@aziendaId, @utenteId, @codiceHash)",
                hashCodiciRecupero.Select(codiceHash => new { aziendaId, utenteId, codiceHash }), t, ct));

            await InserisciAuditAsync(c, t, aziendaId, utenteId, "mfa.attivato", null, ct);
            return 0;
        }, ct);

    public Task CreaSessioneAsync(Guid aziendaId, Guid utenteId, byte[] tokenHash, DateTime scadeIl, string? dispositivo, IPAddress? indirizzo, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) => c.ExecuteAsync(Comando(
            """
            INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, token_hash, scade_il, dispositivo, indirizzo)
            VALUES (@aziendaId, @utenteId, @tokenHash, @scadeIl, @dispositivo, @indirizzo)
            """,
            new { aziendaId, utenteId, tokenHash, scadeIl = DateTime.SpecifyKind(scadeIl, DateTimeKind.Utc), dispositivo, indirizzo },
            t, ct)), ct);

    // La transazione si conclude sempre con COMMIT: se la funzione rileva un riuso,
    // la revoca della catena deve restare anche se il rinnovo viene rifiutato.
    public Task<EsitoRotazione> RuotaSessioneAsync(byte[] tokenHash, byte[] nuovoHash, string? dispositivo, IPAddress? indirizzo, CancellationToken ct) =>
        db.EseguiAsync(null, null, (c, t) => c.QuerySingleAsync<EsitoRotazione>(Comando(
            "SELECT * FROM sec.ruota_sessione(@tokenHash, @nuovoHash, @dispositivo, @indirizzo)",
            new { tokenHash, nuovoHash, dispositivo, indirizzo }, t, ct)), ct);

    public Task<int> RevocaSessioneAsync(Guid aziendaId, Guid utenteId, byte[] tokenHash, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, (c, t) => c.ExecuteScalarAsync<int>(Comando(
            "SELECT sec.revoca_sessione(@utenteId, @tokenHash, 'uscita')", new { utenteId, tokenHash }, t, ct)), ct);

    public Task RegistraAuditAsync(Guid aziendaId, Guid? utenteId, string azione, IPAddress? indirizzo, CancellationToken ct) =>
        db.EseguiAsync(aziendaId, utenteId, async (c, t) =>
        {
            await InserisciAuditAsync(c, t, aziendaId, utenteId, azione, indirizzo, ct);
            return 0;
        }, ct);

    public async Task<IReadOnlyList<UtenteElenco>> ElencoUtentiAsync(Guid aziendaId, Guid utenteCorrente, CancellationToken ct) =>
        [.. await db.EseguiAsync(aziendaId, utenteCorrente, (c, t) => c.QueryAsync<UtenteElenco>(Comando(
            """
            SELECT u.utente_id, u.email, u.nome, u.cognome, u.stato, u.mfa_attivo,
                   coalesce(array_agg(r.codice ORDER BY r.codice) FILTER (WHERE r.codice IS NOT NULL), '{}') AS ruoli
            FROM sec.utenti u
            LEFT JOIN sec.utenti_ruoli ur ON ur.utente_id = u.utente_id
            LEFT JOIN sec.ruoli r ON r.ruolo_id = ur.ruolo_id
            GROUP BY u.utente_id
            ORDER BY u.cognome, u.nome
            """,
            null, t, ct)), ct)];

    public async Task<Guid?> CreaUtenteAsync(Guid aziendaId, Guid utenteCorrente, NuovoUtente utente, string passwordHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(utente);

        try
        {
            return await db.EseguiAsync<Guid?>(aziendaId, utenteCorrente, async (c, t) =>
            {
                var utenteId = await c.ExecuteScalarAsync<Guid>(Comando(
                    """
                    INSERT INTO sec.utenti (azienda_id, email, nome, cognome, password_hash, creato_da)
                    VALUES (@aziendaId, btrim(@Email), btrim(@Nome), btrim(@Cognome), @passwordHash, @utenteCorrente)
                    RETURNING utente_id
                    """,
                    new { aziendaId, utente.Email, utente.Nome, utente.Cognome, passwordHash, utenteCorrente }, t, ct));

                await c.ExecuteAsync(Comando(
                    """
                    INSERT INTO sec.utenti_ruoli (azienda_id, utente_id, ruolo_id, creato_da)
                    SELECT @aziendaId, @utenteId, r.ruolo_id, @utenteCorrente
                    FROM sec.ruoli r
                    WHERE r.azienda_id IS NULL AND r.codice = ANY(@Ruoli)
                    """,
                    new { aziendaId, utenteId, utenteCorrente, Ruoli = utente.Ruoli.ToArray() }, t, ct));

                await InserisciAuditAsync(c, t, aziendaId, utenteCorrente, "utenti.creato", null, ct, entitaId: utenteId);
                return utenteId;
            }, ct);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita)
        {
            return null;
        }
    }

    private static Task InserisciAuditAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid aziendaId, Guid? utenteId, string azione, IPAddress? indirizzo, CancellationToken ct, Guid? entitaId = null) =>
        c.ExecuteAsync(Comando(
            """
            INSERT INTO sec.audit_log (azienda_id, utente_id, azione, entita, entita_id, indirizzo)
            VALUES (@aziendaId, @utenteId, @azione, CASE WHEN @entitaId::uuid IS NULL THEN NULL ELSE 'utenti' END, @entitaId, @indirizzo)
            """,
            new { aziendaId, utenteId, azione, entitaId, indirizzo }, t, ct));

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);
}

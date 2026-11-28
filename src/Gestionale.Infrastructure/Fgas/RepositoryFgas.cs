using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Fgas;
using Gestionale.Domain.Fgas;
using Gestionale.Infrastructure.Anagrafica;
using Npgsql;

namespace Gestionale.Infrastructure.Fgas;

/// <summary>
/// Registro F-Gas con Dapper (ADR 0008). Obbligo e frequenza dei controlli, certificati
/// copiati sulle operazioni, termine dei 30 giorni e scadenze li calcola il database.
/// </summary>
public sealed class RepositoryFgas(Database.Database db) : IRepositoryFgas
{
    // Errore del database per un tecnico senza certificato F-Gas in corso (migrazione 0015).
    private const string SenzaCertificato = "GF001";
    private const string ViolazioneChiave = "23503";
    private const string ViolazioneUnicita = "23505";

    private const string ImpiantoVisibile = """
        i.eliminato_il IS NULL
        AND (@soloTecnico::uuid IS NULL OR EXISTS (
             SELECT 1 FROM lavoro.interventi x WHERE x.impianto_id = i.impianto_id AND x.tecnico_utente_id = @soloTecnico))
        """;

    public Task<IReadOnlyList<GasFluorurato>> GasAsync(Contesto contesto, CancellationToken ct) =>
        Esegui<IReadOnlyList<GasFluorurato>>(contesto, async (c, t) => [.. await c.QueryAsync<GasFluorurato>(Comando(
            """
            SELECT gas_id, codice, designazione, allegato, gwp, miscela, da_verificare
            FROM norma.gas_fluorurati
            WHERE valido_dal <= current_date AND (valido_al IS NULL OR valido_al >= current_date)
            ORDER BY miscela, codice
            """, null, t, ct))], ct);

    public Task<IReadOnlyList<ApparecchiaturaFgas>?> ApparecchiatureAsync(Contesto contesto, Guid impiantoId, Guid? soloTecnico, CancellationToken ct) =>
        Esegui<IReadOnlyList<ApparecchiaturaFgas>?>(contesto, async (c, t) =>
        {
            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    $"SELECT EXISTS (SELECT 1 FROM impianti.impianti i WHERE i.impianto_id = @impiantoId AND {ImpiantoVisibile})",
                    new { impiantoId, soloTecnico }, t, ct)))
            {
                return null;
            }

            var apparecchiature = (await c.QueryAsync<ApparecchiaturaFgas>(Comando(
                """
                SELECT ap.apparecchiatura_fgas_id, ap.generatore_id,
                       concat_ws(' · ', g.sigla_catasto, tg.nome, nullif(concat_ws(' ', g.marca, g.modello), '')) AS generatore,
                       ap.gas_id, gf.codice AS gas, gf.allegato, gf.gwp, gf.da_verificare AS gas_da_verificare,
                       ap.carica_kg, ap.carica_installazione_kg, round(ap.carica_kg * gf.gwp / 1000, 3) AS co2e_tonnellate,
                       norma.frequenza_controllo_perdite(ap.apparecchiatura_fgas_id) AS frequenza_mesi,
                       ap.ermetica_etichettata, ap.edificio_residenziale, ap.sistema_rilevamento,
                       ap.codice_univoco, ap.fattura_numero, ap.fattura_data, ap.versione
                FROM impianti.apparecchiature_fgas ap
                JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
                JOIN impianti.tipi_generatore tg ON tg.tipo_generatore_id = g.tipo_generatore_id
                JOIN norma.gas_fluorurati gf ON gf.gas_id = ap.gas_id
                WHERE g.impianto_id = @impiantoId
                ORDER BY g.dismesso_il IS NOT NULL, g.creato_il, g.generatore_id
                """,
                new { impiantoId }, t, ct))).ToList();

            var operazioni = (await c.QueryAsync<OperazioneFgas>(Comando(
                """
                SELECT f.intervento_fgas_id, f.apparecchiatura_fgas_id, f.intervento_id, f.tipo, f.eseguito_il, f.perdita_rilevata,
                       f.gas_aggiunto_kg, f.gas_recuperato_kg, f.gas_riciclato, f.impianto_riciclaggio, f.certificato_riciclaggio,
                       f.misure_smaltimento, f.tecnico_utente_id, btrim(u.nome || ' ' || u.cognome) AS tecnico,
                       f.certificato_tecnico, f.certificato_impresa, f.note, f.termine_comunicazione, f.comunicato_il,
                       f.annullato_il, f.motivo_annullamento, f.versione
                FROM lavoro.interventi_fgas f
                JOIN sec.utenti u ON u.utente_id = f.tecnico_utente_id
                WHERE f.apparecchiatura_fgas_id = ANY (@ids)
                ORDER BY f.eseguito_il DESC, f.creato_il DESC
                """,
                new { ids = apparecchiature.Select(a => a.ApparecchiaturaFgasId).ToArray() }, t, ct))).ToList();

            foreach (var a in apparecchiature)
            {
                a.Operazioni = [.. operazioni.Where(o => o.ApparecchiaturaFgasId == a.ApparecchiaturaFgasId)];
            }

            return apparecchiature;
        }, ct);

    public Task<EsitoFgas> SalvaApparecchiaturaAsync(
        Contesto contesto, Guid impiantoId, Guid generatoreId, int? versione, DatiApparecchiatura dati, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    $"""
                    SELECT EXISTS (SELECT 1 FROM impianti.generatori g JOIN impianti.impianti i ON i.impianto_id = g.impianto_id
                                   WHERE g.generatore_id = @generatoreId AND g.impianto_id = @impiantoId AND {ImpiantoVisibile})
                    """,
                    new { impiantoId, generatoreId, soloTecnico }, t, ct)))
            {
                return new EsitoFgas.NonTrovata();
            }

            if (!await c.ExecuteScalarAsync<bool>(Comando(
                    "SELECT EXISTS (SELECT 1 FROM norma.gas_fluorurati WHERE gas_id = @GasId AND valido_dal <= current_date AND (valido_al IS NULL OR valido_al >= current_date))",
                    new { dati.GasId }, t, ct)))
            {
                return new EsitoFgas.NonValida([ErroreFgas.GasNonValido]);
            }

            var parametri = new DynamicParameters(dati).Con(new { contesto.AziendaId, generatoreId, versione, contesto.UtenteId });
            var riga = versione is null
                ? await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                    """
                    INSERT INTO impianti.apparecchiature_fgas (azienda_id, generatore_id, gas_id, carica_kg, carica_installazione_kg,
                        ermetica_etichettata, edificio_residenziale, sistema_rilevamento, codice_univoco, fattura_numero, fattura_data, creato_da)
                    VALUES (@AziendaId, @generatoreId, @GasId, @CaricaKg, @CaricaInstallazioneKg,
                        @ErmeticaEtichettata, @EdificioResidenziale, @SistemaRilevamento, @CodiceUnivoco, @FatturaNumero, @FatturaData, @UtenteId)
                    ON CONFLICT (generatore_id) DO NOTHING
                    RETURNING apparecchiatura_fgas_id AS id, versione
                    """, parametri, t, ct))
                : await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                    """
                    UPDATE impianti.apparecchiature_fgas
                    SET gas_id = @GasId, carica_kg = @CaricaKg, carica_installazione_kg = @CaricaInstallazioneKg,
                        ermetica_etichettata = @ErmeticaEtichettata, edificio_residenziale = @EdificioResidenziale,
                        sistema_rilevamento = @SistemaRilevamento, codice_univoco = @CodiceUnivoco, fattura_numero = @FatturaNumero,
                        fattura_data = @FatturaData, modificato_da = @UtenteId
                    WHERE generatore_id = @generatoreId AND versione = @versione
                    RETURNING apparecchiatura_fgas_id AS id, versione
                    """, parametri, t, ct));

            // Nessuna riga: con versione, è stata superata; senza, il generatore ne aveva già una.
            return riga is null ? new EsitoFgas.VersioneSuperata() : new EsitoFgas.Riuscita(riga.Id, riga.Versione);
        }, ct);

    public Task<EsitoFgas> EliminaApparecchiaturaAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var attuale = await c.QuerySingleOrDefaultAsync<(Guid Id, int Versione, bool ConOperazioni)>(Comando(
                """
                SELECT ap.apparecchiatura_fgas_id, ap.versione,
                       EXISTS (SELECT 1 FROM lavoro.interventi_fgas f WHERE f.apparecchiatura_fgas_id = ap.apparecchiatura_fgas_id)
                FROM impianti.apparecchiature_fgas ap JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
                WHERE ap.generatore_id = @generatoreId AND g.impianto_id = @impiantoId
                """,
                new { impiantoId, generatoreId }, t, ct));
            if (attuale.Id == Guid.Empty)
            {
                return new EsitoFgas.NonTrovata();
            }

            // Il registro si conserva: un'apparecchiatura con operazioni non si toglie.
            if (attuale.ConOperazioni)
            {
                return new EsitoFgas.NonValida([ErroreFgas.ApparecchiaturaConOperazioni]);
            }

            if (attuale.Versione != versione)
            {
                return new EsitoFgas.VersioneSuperata();
            }

            await c.ExecuteAsync(Comando("DELETE FROM impianti.apparecchiature_fgas WHERE apparecchiatura_fgas_id = @Id", new { attuale.Id }, t, ct));
            return new EsitoFgas.Riuscita(attuale.Id, versione);
        }, ct);

    public Task<EsitoFgas> RegistraOperazioneAsync(Contesto contesto, DatiOperazione dati, Guid? soloTecnico, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            // Il Tecnico registra solo sugli interventi suoi.
            var intervento = await c.ExecuteScalarAsync<bool>(Comando(
                """
                SELECT EXISTS (SELECT 1 FROM lavoro.interventi
                               WHERE intervento_id = @InterventoId AND stato <> 'annullato'
                                 AND (@soloTecnico::uuid IS NULL OR tecnico_utente_id = @soloTecnico))
                """,
                new { dati.InterventoId, soloTecnico }, t, ct));
            if (!intervento)
            {
                return new EsitoFgas.NonTrovata();
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                """
                INSERT INTO lavoro.interventi_fgas (azienda_id, apparecchiatura_fgas_id, intervento_id, tipo, eseguito_il, perdita_rilevata,
                    gas_aggiunto_kg, gas_recuperato_kg, gas_riciclato, impianto_riciclaggio, certificato_riciclaggio, misure_smaltimento,
                    tecnico_utente_id, certificato_tecnico, note, termine_comunicazione, creato_da)
                VALUES (@AziendaId, @ApparecchiaturaId, @InterventoId, @Tipo, @EseguitoIl, @PerditaRilevata,
                    @GasAggiuntoKg, @GasRecuperatoKg, @GasRiciclato, @ImpiantoRiciclaggio, @CertificatoRiciclaggio, @MisureSmaltimento,
                    @TecnicoUtenteId, '', @Note, @EseguitoIl, @UtenteId)
                RETURNING intervento_fgas_id AS id, versione
                """,
                new DynamicParameters(dati).Con(new { contesto.AziendaId, contesto.UtenteId }), t, ct));
            return new EsitoFgas.Riuscita(riga.Id, riga.Versione);
        }, ct);

    public Task<EsitoFgas> AnnullaOperazioneAsync(Contesto contesto, Guid operazioneId, int versione, string motivo, CancellationToken ct) =>
        CambiaOperazioneAsync(contesto, operazioneId, versione,
            "annullato_il = now(), motivo_annullamento = @valore", motivo, ct);

    public Task<EsitoFgas> SegnaComunicataAsync(Contesto contesto, Guid operazioneId, int versione, DateOnly comunicatoIl, CancellationToken ct) =>
        CambiaOperazioneAsync(contesto, operazioneId, versione, "comunicato_il = @valore", comunicatoIl, ct);

    public Task<IReadOnlyList<ComunicazioneFgas>> ComunicazioniAsync(Contesto contesto, bool soloDaComunicare, CancellationToken ct) =>
        Esegui<IReadOnlyList<ComunicazioneFgas>>(contesto, async (c, t) => [.. await c.QueryAsync<ComunicazioneFgas>(Comando(
            "SELECT * FROM lavoro.comunicazioni_fgas(@soloDaComunicare)", new { soloDaComunicare }, t, ct))], ct);

    public Task<IReadOnlyList<Certificazione>> CertificazioniAsync(Contesto contesto, CancellationToken ct) =>
        Esegui<IReadOnlyList<Certificazione>>(contesto, async (c, t) => [.. await c.QueryAsync<Certificazione>(Comando(
            """
            SELECT ce.certificazione_id, ce.utente_id, btrim(u.nome || ' ' || u.cognome) AS utente, ce.tipo, ce.numero, ce.ente,
                   ce.rilasciata_il, ce.scade_il, ce.versione
            FROM sec.certificazioni ce
            LEFT JOIN sec.utenti u ON u.utente_id = ce.utente_id
            ORDER BY ce.utente_id IS NOT NULL, u.cognome, u.nome, ce.scade_il DESC NULLS FIRST
            """, null, t, ct))], ct);

    public Task<EsitoFgas> SalvaCertificazioneAsync(Contesto contesto, Guid? certificazioneId, int? versione, DatiCertificato dati, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            // Parametri espliciti: UtenteId del certificato e utente che scrive sono due cose diverse.
            var parametri = new
            {
                contesto.AziendaId,
                scrittore = contesto.UtenteId,
                certificatoUtente = dati.UtenteId,
                certificazioneId,
                versione,
                tipo = dati.UtenteId is null ? TipiCertificato.Impresa : TipiCertificato.Persona,
                dati.Numero,
                dati.Ente,
                dati.RilasciataIl,
                dati.ScadeIl,
            };
            if (certificazioneId is null)
            {
                var nuova = await c.QuerySingleAsync<RigaScritta>(Comando(
                    """
                    INSERT INTO sec.certificazioni (azienda_id, utente_id, tipo, numero, ente, rilasciata_il, scade_il, creato_da)
                    VALUES (@AziendaId, @certificatoUtente, @tipo, @Numero, @Ente, @RilasciataIl, @ScadeIl, @scrittore)
                    RETURNING certificazione_id AS id, versione
                    """,
                    parametri, t, ct));
                return new EsitoFgas.Riuscita(nuova.Id, nuova.Versione);
            }

            var riga = await c.QuerySingleOrDefaultAsync<RigaScritta>(Comando(
                """
                UPDATE sec.certificazioni
                SET utente_id = @certificatoUtente, tipo = @tipo, numero = @Numero, ente = @Ente, rilasciata_il = @RilasciataIl,
                    scade_il = @ScadeIl, modificato_da = @scrittore
                WHERE certificazione_id = @certificazioneId AND versione = @versione
                RETURNING certificazione_id AS id, versione
                """,
                parametri, t, ct));
            if (riga is not null)
            {
                return new EsitoFgas.Riuscita(riga.Id, riga.Versione);
            }

            return await EsisteAsync(c, t, "sec.certificazioni", "certificazione_id", certificazioneId.Value, ct)
                ? new EsitoFgas.VersioneSuperata()
                : new EsitoFgas.NonTrovata();
        }, ct);

    public Task<EsitoFgas> EliminaCertificazioneAsync(Contesto contesto, Guid certificazioneId, int versione, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var eliminate = await c.ExecuteAsync(Comando(
                "DELETE FROM sec.certificazioni WHERE certificazione_id = @certificazioneId AND versione = @versione",
                new { certificazioneId, versione }, t, ct));
            if (eliminate > 0)
            {
                return new EsitoFgas.Riuscita(certificazioneId, versione);
            }

            return await EsisteAsync(c, t, "sec.certificazioni", "certificazione_id", certificazioneId, ct)
                ? new EsitoFgas.VersioneSuperata()
                : new EsitoFgas.NonTrovata();
        }, ct);

    // Annullamento e comunicazione: solo su un'operazione ancora aperta e alla versione letta.
    private Task<EsitoFgas> CambiaOperazioneAsync(Contesto contesto, Guid operazioneId, int versione, string assegnazione, object valore, CancellationToken ct) =>
        Scrivi(contesto, async (c, t) =>
        {
            var stato = await c.QuerySingleOrDefaultAsync<(int Versione, bool Chiusa)>(Comando(
                "SELECT versione, comunicato_il IS NOT NULL OR annullato_il IS NOT NULL FROM lavoro.interventi_fgas WHERE intervento_fgas_id = @operazioneId",
                new { operazioneId }, t, ct));
            if (stato.Versione == 0)
            {
                return new EsitoFgas.NonTrovata();
            }

            if (stato.Chiusa)
            {
                return new EsitoFgas.StatoNonConsentito();
            }

            if (stato.Versione != versione)
            {
                return new EsitoFgas.VersioneSuperata();
            }

            var riga = await c.QuerySingleAsync<RigaScritta>(Comando(
                $"""
                UPDATE lavoro.interventi_fgas SET {assegnazione}, modificato_da = @utenteId
                WHERE intervento_fgas_id = @operazioneId
                RETURNING intervento_fgas_id AS id, versione
                """,
                new { operazioneId, valore, utenteId = contesto.UtenteId }, t, ct));
            return new EsitoFgas.Riuscita(riga.Id, riga.Versione);
        }, ct);

    private static Task<bool> EsisteAsync(NpgsqlConnection c, NpgsqlTransaction t, string tabella, string chiave, Guid id, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(Comando($"SELECT EXISTS (SELECT 1 FROM {tabella} WHERE {chiave} = @id)", new { id }, t, ct));

    // Gli errori del database che hanno un significato per chi usa il programma diventano NonValida.
    private async Task<EsitoFgas> Scrivi(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<EsitoFgas>> lavoro, CancellationToken ct)
    {
        try
        {
            return await Esegui(contesto, lavoro, ct);
        }
        catch (PostgresException e) when (e.SqlState == SenzaCertificato)
        {
            return new EsitoFgas.NonValida([ErroreFgas.TecnicoSenzaCertificato]);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneChiave)
        {
            return new EsitoFgas.NonValida([ErroreFgas.InterventoNonDellImpianto]);
        }
        catch (PostgresException e) when (e.SqlState == ViolazioneUnicita)
        {
            return new EsitoFgas.VersioneSuperata();
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
    }
}

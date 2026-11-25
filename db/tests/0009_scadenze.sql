-- Scadenze: regole di cadenza, calcolo dalla periodicità e dalle regole,
-- chiusura con l'intervento, data corretta a mano, annullamento con
-- l'impianto, richiami, lista in arrivo, Row-Level Security e permessi.
-- Dati fittizi; le regole usate sono le voci di prova della migrazione.
-- Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(47);

-- Date attese, calcolate come fa il database.
CREATE TEMP TABLE oggi AS SELECT (now() AT TIME ZONE 'Europe/Rome')::date AS d;
GRANT SELECT ON oggi TO app_api;

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende toscane, sedi, clienti, un tecnico
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01910000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01910000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01910000-0000-7000-8000-0000000000f1', '01910000-0000-7000-8000-00000000000a', 'tecnico@alfa.example', 'Marco', 'Rossi', '$argon2id$prova');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01910000-0000-7000-8000-0000000000c1', '01910000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri'),
    ('01910000-0000-7000-8000-0000000000c2', '01910000-0000-7000-8000-00000000000b', 'privato', 'Paolo', 'Verdi');

INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale) VALUES
    ('01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000c1', 'email', 'giulia.neri@example.com', true),
    ('01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000c1', 'cellulare', '+39 333 000 0001', false);

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia) VALUES
    ('01910000-0000-7000-8000-0000000000d1', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000c1',
     'Via dei Platani', '12', '50100', 'Firenze', 'FI'),
    ('01910000-0000-7000-8000-0000000000d2', '01910000-0000-7000-8000-00000000000b', '01910000-0000-7000-8000-0000000000c2',
     'Via dei Tigli', '3', '56100', 'Pisa', 'PI');

-- ---------------------------------------------------------------------------
-- Struttura
-- ---------------------------------------------------------------------------

SELECT has_schema('norma', 'esiste lo schema norma');
SELECT has_table('norma', 'regole_cadenza', 'esiste norma.regole_cadenza');
SELECT has_table('lavoro', 'scadenze', 'esiste lavoro.scadenze');
SELECT has_table('lavoro', 'richiami', 'esiste lavoro.richiami');
SELECT has_function('lavoro', 'ricalcola_scadenze', ARRAY['uuid'], 'esiste lavoro.ricalcola_scadenze');
SELECT has_function('lavoro', 'scadenze_in_arrivo', ARRAY['integer'], 'esiste lavoro.scadenze_in_arrivo');
SELECT is(
    (SELECT bool_and(da_verificare) FROM norma.regole_cadenza),
    true,
    'le regole caricate sono tutte voci di prova da verificare'
);

-- ---------------------------------------------------------------------------
-- Regole di cadenza
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ INSERT INTO norma.regole_cadenza (codice, nome, regione, tipo_impianto_id, potenza_da_kw, potenza_a_kw, periodicita_mesi, valido_dal)
       SELECT 'prova_sovrapposta', 'Sovrapposta', 'toscana', tipo_impianto_id, 50, 150, 12, '2026-06-01'
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    '23P01', NULL,
    'due regole valide nello stesso periodo non hanno fasce di potenza sovrapposte'
);
SELECT throws_ok(
    $$ INSERT INTO norma.regole_cadenza (codice, nome, regione, tipo_impianto_id, periodicita_mesi, valido_dal, da_verificare)
       SELECT 'prova_verificata', 'Senza fonte', 'lombardia', tipo_impianto_id, 12, '2026-01-01', false
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    '23514', NULL,
    'una regola segnata come verificata senza fonte è rifiutata'
);

-- ---------------------------------------------------------------------------
-- Calcolo alla creazione dell'impianto
-- ---------------------------------------------------------------------------

-- e1: riscaldamento da 24 kW, manutenzione ogni 12 mesi.
INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id, potenza_utile_kw, manutenzione_mesi)
SELECT '01910000-0000-7000-8000-0000000000e1', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000d1', tipo_impianto_id, 24, 12
FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento';

-- e2: riscaldamento senza potenza e senza periodicità di manutenzione.
INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01910000-0000-7000-8000-0000000000e2', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000d1', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento';

-- e3: climatizzazione dell'altra azienda, senza potenza.
INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01910000-0000-7000-8000-0000000000e3', '01910000-0000-7000-8000-00000000000b', '01910000-0000-7000-8000-0000000000d2', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_climatizzazione';

SELECT results_eq(
    $$ SELECT tipo, data_scadenza, periodicita_mesi FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'aperta' ORDER BY tipo $$,
    $$ VALUES ('controllo_efficienza', (SELECT d FROM oggi), 48), ('manutenzione', (SELECT d FROM oggi), 12) $$,
    'senza interventi alle spalle le due scadenze sono da fare subito, con la periodicità giusta'
);
SELECT is(
    (SELECT r.codice FROM lavoro.scadenze s JOIN norma.regole_cadenza r USING (regola_cadenza_id)
     WHERE s.impianto_id = '01910000-0000-7000-8000-0000000000e1' AND s.stato = 'aperta'),
    'prova_riscaldamento_piccolo',
    'il controllo usa la regola della fascia di potenza dell''impianto'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e2'),
    0,
    'senza periodicità né potenza (le regole hanno fasce) non nasce nessuna scadenza'
);
SELECT results_eq(
    $$ SELECT tipo, periodicita_mesi FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e3' $$,
    $$ VALUES ('controllo_efficienza', 48) $$,
    'una regola senza fasce vale anche per un impianto senza potenza'
);

-- Cambio di potenza: si passa all'altra fascia.
UPDATE impianti.impianti SET potenza_utile_kw = 150 WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1';

SELECT results_eq(
    $$ SELECT r.codice, s.periodicita_mesi FROM lavoro.scadenze s JOIN norma.regole_cadenza r USING (regola_cadenza_id)
       WHERE s.impianto_id = '01910000-0000-7000-8000-0000000000e1' AND s.stato = 'aperta' $$,
    $$ VALUES ('prova_riscaldamento_grande'::text, 24) $$,
    'cambiando la potenza la scadenza aperta passa alla regola della nuova fascia'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1'),
    2,
    'il ricalcolo aggiorna la scadenza aperta invece di crearne un''altra'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.scadenze (azienda_id, impianto_id, tipo, data_scadenza, periodicita_mesi)
       VALUES ('01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e1', 'manutenzione', '2027-01-01', 12) $$,
    '23505', NULL,
    'un impianto ha una sola scadenza aperta per tipo'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.scadenze (azienda_id, impianto_id, tipo, data_scadenza, periodicita_mesi)
       VALUES ('01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e2', 'controllo_efficienza', '2027-01-01', 12) $$,
    '23514', NULL,
    'un controllo di efficienza ha sempre la regola che lo ha generato'
);

-- ---------------------------------------------------------------------------
-- Chiusura con l'intervento
-- ---------------------------------------------------------------------------

-- 301: riparazione risolta; 302: manutenzione da completare; 303: manutenzione
-- risolta con il controllo. Tutte pianificate dal tecnico, in giorni diversi.
INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, tipo, stato, tecnico_utente_id, finestra) VALUES
    ('01910000-0000-7000-8000-000000000301', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e1',
     'riparazione', 'pianificato', '01910000-0000-7000-8000-0000000000f1', '[2026-12-01 09:00+01, 2026-12-01 10:00+01)'),
    ('01910000-0000-7000-8000-000000000302', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e1',
     'manutenzione', 'pianificato', '01910000-0000-7000-8000-0000000000f1', '[2026-12-02 09:00+01, 2026-12-02 10:00+01)'),
    ('01910000-0000-7000-8000-000000000303', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e1',
     'manutenzione', 'pianificato', '01910000-0000-7000-8000-0000000000f1', '[2026-12-03 09:00+01, 2026-12-03 10:00+01)');

SELECT throws_ok(
    $$ SELECT lavoro.chiudi_intervento('01910000-0000-7000-8000-000000000301', 1, 'risolto', NULL, '[]', true) $$,
    '23514', NULL,
    'il controllo di efficienza si spunta solo su una manutenzione'
);

SELECT ok(
    lavoro.chiudi_intervento('01910000-0000-7000-8000-000000000301', 1, 'risolto', NULL, '[]'),
    'la chiusura con cinque argomenti funziona ancora'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'chiusa'),
    0,
    'una riparazione non chiude le scadenze'
);

SELECT ok(
    lavoro.chiudi_intervento('01910000-0000-7000-8000-000000000302', 1, 'da_completare', NULL, '[]', true),
    'si chiude una manutenzione da completare'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'chiusa'),
    0,
    'una manutenzione non risolta lascia aperte le scadenze'
);

SELECT ok(
    lavoro.chiudi_intervento('01910000-0000-7000-8000-000000000303', 1, 'risolto', NULL, '[]', true),
    'si chiude una manutenzione risolta con il controllo'
);
SELECT results_eq(
    $$ SELECT tipo, intervento_chiusura_id FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'chiusa' ORDER BY tipo $$,
    $$ VALUES ('controllo_efficienza', '01910000-0000-7000-8000-000000000303'::uuid),
              ('manutenzione',         '01910000-0000-7000-8000-000000000303'::uuid) $$,
    'la manutenzione risolta chiude entrambe le scadenze e ne registra l''intervento'
);
SELECT results_eq(
    $$ SELECT tipo, data_scadenza, intervento_origine_id FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'aperta' ORDER BY tipo $$,
    $$ VALUES ('controllo_efficienza', ((SELECT d FROM oggi) + interval '24 months')::date, '01910000-0000-7000-8000-000000000303'::uuid),
              ('manutenzione',         ((SELECT d FROM oggi) + interval '12 months')::date, '01910000-0000-7000-8000-000000000303'::uuid) $$,
    'le scadenze successive partono dalla chiusura, con la periodicità di ciascuna'
);
SELECT throws_ok(
    $$ UPDATE lavoro.scadenze SET data_scadenza = '2030-01-01'
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'chiusa' AND tipo = 'manutenzione' $$,
    '55000', NULL,
    'una scadenza chiusa non si modifica'
);

-- ---------------------------------------------------------------------------
-- Data corretta a mano
-- ---------------------------------------------------------------------------

UPDATE lavoro.scadenze SET data_scadenza = (SELECT d FROM oggi) + 100, data_manuale = true
WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'aperta';

UPDATE impianti.impianti SET manutenzione_mesi = 6 WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1';

SELECT results_eq(
    $$ SELECT data_scadenza, periodicita_mesi FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'aperta' $$,
    $$ VALUES ((SELECT d FROM oggi) + 100, 6) $$,
    'il ricalcolo non sposta una data corretta a mano, ma aggiorna la periodicità'
);

-- ---------------------------------------------------------------------------
-- Richiami
-- ---------------------------------------------------------------------------

INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito, richiamare_il)
SELECT azienda_id, scadenza_id, 'telefono', 'nessuna_risposta', '2027-01-10'
FROM lavoro.scadenze
WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'aperta';

SELECT is(
    (SELECT stato_richiamo FROM lavoro.scadenze
     WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'aperta'),
    'in_attesa',
    'un tentativo senza risposta mette il richiamo in attesa'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito, richiamare_il)
       SELECT azienda_id, scadenza_id, 'telefono', 'appuntamento', '2027-01-10'
       FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'aperta' $$,
    '23514', NULL,
    'la data per risentirsi vale solo se il cliente va richiamato'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito)
       SELECT azienda_id, scadenza_id, 'telefono', 'appuntamento'
       FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'chiusa' $$,
    '55000', NULL,
    'un richiamo si registra solo su una scadenza aperta'
);

INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito)
SELECT azienda_id, scadenza_id, 'email', 'appuntamento'
FROM lavoro.scadenze
WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_efficienza' AND stato = 'aperta';

SELECT is(
    (SELECT stato_richiamo FROM lavoro.scadenze
     WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_efficienza' AND stato = 'aperta'),
    'fissato',
    'un appuntamento fissa il richiamo'
);

-- ---------------------------------------------------------------------------
-- Scadenze in arrivo
-- ---------------------------------------------------------------------------

-- e4: climatizzazione dell'azienda Alfa, controllo da fare subito.
INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01910000-0000-7000-8000-0000000000e4', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000d1', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_climatizzazione';

SELECT results_eq(
    $$ SELECT impianto_id, tipo, giorni_mancanti, cliente, indirizzo, telefono
       FROM lavoro.scadenze_in_arrivo(30)
       WHERE impianto_id IN ('01910000-0000-7000-8000-0000000000e1', '01910000-0000-7000-8000-0000000000e4') $$,
    $$ VALUES ('01910000-0000-7000-8000-0000000000e4'::uuid, 'controllo_efficienza', 0, 'Neri Giulia', 'Via dei Platani 12', '+39 333 000 0001') $$,
    'in arrivo entro 30 giorni c''è solo il controllo di oggi, con cliente, indirizzo e telefono'
);
SELECT results_eq(
    $$ SELECT ultimo_esito, richiamare_il FROM lavoro.scadenze_in_arrivo(3650)
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' $$,
    $$ VALUES ('nessuna_risposta'::text, '2027-01-10'::date) $$,
    'la lista riporta l''ultimo richiamo'
);

-- ---------------------------------------------------------------------------
-- Annullamento con l'impianto
-- ---------------------------------------------------------------------------

UPDATE impianti.impianti SET manutenzione_mesi = NULL WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1';

SELECT is(
    (SELECT motivo_annullamento FROM lavoro.scadenze
     WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND tipo = 'manutenzione' AND stato = 'annullata'),
    'Periodicità di manutenzione non indicata',
    'togliendo la periodicità la scadenza di manutenzione si annulla'
);

UPDATE impianti.impianti SET stato = 'dismesso', dismesso_il = '2026-11-25' WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1';

SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'aperta'),
    0,
    'un impianto dismesso non ha scadenze aperte'
);

UPDATE impianti.impianti SET stato = 'attivo', dismesso_il = NULL WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1';

SELECT results_eq(
    $$ SELECT tipo, data_scadenza FROM lavoro.scadenze
       WHERE impianto_id = '01910000-0000-7000-8000-0000000000e1' AND stato = 'aperta' $$,
    $$ VALUES ('controllo_efficienza', ((SELECT d FROM oggi) + interval '24 months')::date) $$,
    'riattivato, l''impianto riprende il controllo dall''ultimo intervento che lo ha eseguito'
);

-- ---------------------------------------------------------------------------
-- Permessi
-- ---------------------------------------------------------------------------

SELECT results_eq(
    $$ SELECT r.codice, p.codice, rp.ambito
       FROM sec.ruoli_permessi rp
       JOIN sec.ruoli r USING (ruolo_id)
       JOIN sec.permessi p USING (permesso_id)
       WHERE p.codice LIKE 'scadenze.%' AND r.codice = 'tecnico' $$,
    $$ VALUES ('tecnico'::text, 'scadenze.lettura'::text, 'propri'::text) $$,
    'il Tecnico legge solo le scadenze proprie e non registra richiami'
);
SELECT is(
    (SELECT count(*)::integer FROM sec.ruoli_permessi rp
     JOIN sec.ruoli r USING (ruolo_id) JOIN sec.permessi p USING (permesso_id)
     WHERE p.codice LIKE 'scadenze.%' AND r.codice = 'amministratore_sistema'),
    0,
    'l''amministratore di sistema non vede le scadenze'
);

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi di app_api
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01910000-0000-7000-8000-00000000000a', true);

SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze WHERE azienda_id = '01910000-0000-7000-8000-00000000000b'),
    0,
    'app_api non vede le scadenze di un''altra azienda'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze_in_arrivo(30) WHERE impianto_id = '01910000-0000-7000-8000-0000000000e3'),
    0,
    'la lista in arrivo rispetta l''azienda corrente'
);
SELECT lives_ok(
    $$ INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, potenza_utile_kw, manutenzione_mesi)
       SELECT '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000d1', tipo_impianto_id, 18, 12
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    'app_api crea un impianto e il ricalcolo gira con i suoi privilegi'
);
SELECT lives_ok(
    $$ INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, tipo, stato, tecnico_utente_id, finestra) VALUES
       ('01910000-0000-7000-8000-000000000304', '01910000-0000-7000-8000-00000000000a', '01910000-0000-7000-8000-0000000000e4',
        'manutenzione', 'pianificato', '01910000-0000-7000-8000-0000000000f1', '[2026-12-04 09:00+01, 2026-12-04 10:00+01)') $$,
    'app_api pianifica una manutenzione'
);
SELECT ok(
    lavoro.chiudi_intervento('01910000-0000-7000-8000-000000000304', 1, 'risolto', NULL, '[]', true),
    'app_api chiude una manutenzione e ricalcola le scadenze'
);
SELECT is(
    (SELECT data_scadenza FROM lavoro.scadenze
     WHERE impianto_id = '01910000-0000-7000-8000-0000000000e4' AND stato = 'aperta'),
    ((SELECT d FROM oggi) + interval '48 months')::date,
    'dopo la chiusura dall''API il controllo successivo è fra 48 mesi'
);
SELECT throws_ok(
    $$ DELETE FROM lavoro.scadenze $$,
    '42501', NULL,
    'app_api non cancella le scadenze: si chiudono o si annullano'
);
SELECT throws_ok(
    $$ UPDATE lavoro.richiami SET note = 'riscritto' $$,
    '42501', NULL,
    'i richiami sono in sola aggiunta per app_api'
);
SELECT throws_ok(
    $$ INSERT INTO norma.regole_cadenza (codice, nome, regione, tipo_impianto_id, periodicita_mesi, valido_dal)
       SELECT 'prova_api', 'Dall''API', 'lombardia', tipo_impianto_id, 12, '2026-01-01'
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    '42501', NULL,
    'le regole di cadenza sono dati normativi: app_api le legge soltanto'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

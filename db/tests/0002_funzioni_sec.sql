-- Funzioni dello schema sec: azienda_corrente, aggiorna_metadati,
-- blocca_modifica_audit. Più i dati iniziali di ruoli e permessi.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(18);

-- ---------------------------------------------------------------------------
-- sec.azienda_corrente()
-- ---------------------------------------------------------------------------

SELECT has_function('sec', 'azienda_corrente', ARRAY[]::text[], 'esiste sec.azienda_corrente()');
SELECT volatility_is('sec', 'azienda_corrente', ARRAY[]::text[], 'stable', 'sec.azienda_corrente() è STABLE');

SELECT is(sec.azienda_corrente(), NULL, 'senza impostazione restituisce NULL');

SELECT set_config('app.azienda_id', '', true);
SELECT is(sec.azienda_corrente(), NULL, 'con valore vuoto (dopo un SET LOCAL concluso) restituisce NULL');

SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);
SELECT is(sec.azienda_corrente(), '01900000-0000-7000-8000-00000000000a'::uuid, 'restituisce l''azienda impostata');

SELECT set_config('app.azienda_id', 'non-un-uuid', true);
SELECT throws_ok('SELECT sec.azienda_corrente()', '22P02', NULL, 'un valore non valido è un errore, non un accesso libero');

SELECT set_config('app.azienda_id', '', true);

-- ---------------------------------------------------------------------------
-- sec.aggiorna_metadati()
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione, creato_il)
VALUES ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana', '2026-01-01 08:00+01');

SELECT is(
    (SELECT versione FROM sec.aziende WHERE azienda_id = '01900000-0000-7000-8000-00000000000a'),
    1,
    'una nuova riga parte da versione 1'
);
SELECT is(
    (SELECT modificato_il FROM sec.aziende WHERE azienda_id = '01900000-0000-7000-8000-00000000000a'),
    NULL,
    'una nuova riga non ha data di modifica'
);

UPDATE sec.aziende
SET ragione_sociale = 'Termoidraulica Alfa Srl (fittizia, rinominata)',
    creato_il = '2000-01-01 00:00+01',
    versione = 99
WHERE azienda_id = '01900000-0000-7000-8000-00000000000a';

SELECT is(
    (SELECT versione FROM sec.aziende WHERE azienda_id = '01900000-0000-7000-8000-00000000000a'),
    2,
    'UPDATE incrementa la versione di uno, ignorando il valore passato'
);
SELECT is(
    (SELECT modificato_il FROM sec.aziende WHERE azienda_id = '01900000-0000-7000-8000-00000000000a'),
    now(),
    'UPDATE imposta modificato_il'
);
SELECT is(
    (SELECT creato_il FROM sec.aziende WHERE azienda_id = '01900000-0000-7000-8000-00000000000a'),
    '2026-01-01 08:00+01'::timestamptz,
    'UPDATE non può riscrivere creato_il'
);

-- ---------------------------------------------------------------------------
-- sec.blocca_modifica_audit(): vale anche per il superutente
-- ---------------------------------------------------------------------------

INSERT INTO sec.audit_log (azienda_id, azione)
VALUES ('01900000-0000-7000-8000-00000000000a', 'prova.audit');

SELECT throws_ok(
    $$ UPDATE sec.audit_log SET azione = 'manomesso' $$,
    '42501',
    'sec.audit_log è in sola aggiunta: UPDATE non consentito',
    'il trigger blocca UPDATE sull''audit anche al superutente'
);
SELECT throws_ok(
    $$ DELETE FROM sec.audit_log $$,
    '42501',
    'sec.audit_log è in sola aggiunta: DELETE non consentito',
    'il trigger blocca DELETE sull''audit anche al superutente'
);
SELECT throws_ok(
    $$ TRUNCATE sec.audit_log $$,
    '42501',
    'sec.audit_log è in sola aggiunta: TRUNCATE non consentito',
    'il trigger blocca TRUNCATE sull''audit anche al superutente'
);

-- ---------------------------------------------------------------------------
-- Dati iniziali: ruoli e matrice dei permessi
-- ---------------------------------------------------------------------------

SELECT set_eq(
    'SELECT codice FROM sec.ruoli WHERE di_sistema',
    ARRAY['titolare', 'amministrativo', 'responsabile_tecnico', 'tecnico', 'sola_lettura', 'amministratore_sistema'],
    'esistono i sei ruoli di sistema'
);

SELECT is_empty(
    $$ SELECT p.codice
       FROM sec.ruoli_permessi rp
       JOIN sec.ruoli r    ON r.ruolo_id = rp.ruolo_id
       JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
       WHERE r.codice = 'amministratore_sistema'
         AND split_part(p.codice, '.', 1) NOT IN ('utenti', 'audit', 'sistema') $$,
    'l''amministratore di sistema non ha permessi sui dati di business'
);

SELECT is_empty(
    $$ SELECT p.codice
       FROM sec.ruoli_permessi rp
       JOIN sec.ruoli r    ON r.ruolo_id = rp.ruolo_id
       JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
       WHERE r.codice = 'sola_lettura'
         AND p.codice NOT LIKE '%.lettura' $$,
    'il ruolo sola lettura ha solo permessi di lettura'
);

SELECT set_eq(
    $$ SELECT DISTINCT r.codice
       FROM sec.ruoli_permessi rp
       JOIN sec.ruoli r    ON r.ruolo_id = rp.ruolo_id
       JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
       WHERE p.codice LIKE '%.firma' $$,
    ARRAY['titolare', 'responsabile_tecnico'],
    'firmano solo titolare e responsabile tecnico'
);

SELECT * FROM finish();

ROLLBACK;

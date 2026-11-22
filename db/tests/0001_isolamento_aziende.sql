-- Isolamento tra aziende: app_api, con la Row-Level Security, vede e modifica
-- solo le righe dell'azienda impostata con SET LOCAL app.azienda_id.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(21);

-- ---------------------------------------------------------------------------
-- Dati di prova (come superutente, senza RLS)
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, partita_iva, regione) VALUES
    ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', '00000000001', 'toscana'),
    ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          '00000000002', 'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01900000-0000-7000-8000-0000000000a1', '01900000-0000-7000-8000-00000000000a',
     'mario.rossi@alfa.example', 'Mario', 'Rossi', '$argon2id$v=19$m=65536,t=3,p=4$ZmFrZQ$ZmFrZQ'),
    ('01900000-0000-7000-8000-0000000000b1', '01900000-0000-7000-8000-00000000000b',
     'anna.bianchi@beta.example', 'Anna', 'Bianchi', '$argon2id$v=19$m=65536,t=3,p=4$ZmFrZQ$ZmFrZQ');

INSERT INTO sec.utenti_ruoli (azienda_id, utente_id, ruolo_id)
SELECT u.azienda_id, u.utente_id, r.ruolo_id
FROM sec.utenti u
JOIN sec.ruoli r ON r.codice = 'titolare' AND r.azienda_id IS NULL;

INSERT INTO sec.audit_log (azienda_id, utente_id, azione) VALUES
    ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000a1', 'prova.alfa'),
    ('01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000b1', 'prova.beta');

-- ---------------------------------------------------------------------------
-- Rete di sicurezza strutturale
-- ---------------------------------------------------------------------------

SELECT is(
    (SELECT rolbypassrls FROM pg_roles WHERE rolname = 'app_api'),
    false,
    'app_api non ha BYPASSRLS'
);

SELECT is_empty(
    $$ SELECT c.relname
       FROM pg_class c
       JOIN pg_namespace n ON n.oid = c.relnamespace
       JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'azienda_id' AND NOT a.attisdropped
       WHERE c.relkind IN ('r', 'p')
         AND n.nspname NOT IN ('pg_catalog', 'information_schema')
         AND NOT c.relrowsecurity $$,
    'ogni tabella con azienda_id ha la Row-Level Security attiva'
);

SELECT is_empty(
    $$ SELECT c.relname
       FROM pg_class c
       JOIN pg_namespace n ON n.oid = c.relnamespace
       JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'azienda_id' AND NOT a.attisdropped
       WHERE c.relkind IN ('r', 'p')
         AND n.nspname NOT IN ('pg_catalog', 'information_schema')
         AND NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid) $$,
    'ogni tabella con azienda_id ha almeno una policy'
);

-- ---------------------------------------------------------------------------
-- app_api senza azienda impostata: non vede nulla
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;

SELECT is((SELECT count(*) FROM sec.aziende), 0::bigint, 'senza app.azienda_id non vede aziende');
SELECT is((SELECT count(*) FROM sec.utenti),  0::bigint, 'senza app.azienda_id non vede utenti');
SELECT is((SELECT count(*) FROM sec.ruoli WHERE azienda_id IS NULL), 6::bigint, 'i sei ruoli di sistema sono visibili a tutti');

-- ---------------------------------------------------------------------------
-- app_api come azienda Alfa
-- ---------------------------------------------------------------------------

SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);

SELECT results_eq(
    'SELECT ragione_sociale FROM sec.aziende',
    $$ VALUES ('Termoidraulica Alfa Srl (fittizia)'::text) $$,
    'Alfa vede solo la propria azienda'
);
SELECT results_eq(
    'SELECT email FROM sec.utenti',
    $$ VALUES ('mario.rossi@alfa.example'::text) $$,
    'Alfa vede solo i propri utenti'
);
SELECT is((SELECT count(*) FROM sec.utenti_ruoli), 1::bigint, 'Alfa vede solo i ruoli dei propri utenti');
SELECT results_eq(
    $$ SELECT azione FROM sec.audit_log WHERE azione LIKE 'prova.%' $$,
    $$ VALUES ('prova.alfa'::text) $$,
    'Alfa vede solo il proprio audit'
);

SELECT throws_ok(
    $$ INSERT INTO sec.utenti (azienda_id, email, nome, cognome, password_hash)
       VALUES ('01900000-0000-7000-8000-00000000000b', 'intruso@alfa.example', 'Ugo', 'Verdi',
               '$argon2id$v=19$m=65536,t=3,p=4$ZmFrZQ$ZmFrZQ') $$,
    '42501',
    NULL,
    'Alfa non può creare utenti nell''azienda Beta'
);

SELECT throws_ok(
    $$ UPDATE sec.utenti SET azienda_id = '01900000-0000-7000-8000-00000000000b'
       WHERE utente_id = '01900000-0000-7000-8000-0000000000a1' $$,
    '42501',
    NULL,
    'Alfa non può spostare un proprio utente nell''azienda Beta'
);

SELECT throws_ok(
    $$ INSERT INTO sec.audit_log (azienda_id, azione)
       VALUES ('01900000-0000-7000-8000-00000000000b', 'falso.evento') $$,
    '42501',
    NULL,
    'Alfa non può scrivere nell''audit di Beta'
);

SELECT lives_ok(
    $$ INSERT INTO sec.audit_log (azienda_id, azione)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'prova.inserimento') $$,
    'Alfa può scrivere nel proprio audit'
);

SELECT throws_ok(
    $$ UPDATE sec.audit_log SET azione = 'manomesso' $$,
    '42501',
    NULL,
    'app_api non può modificare l''audit'
);

SELECT throws_ok(
    $$ DELETE FROM sec.audit_log $$,
    '42501',
    NULL,
    'app_api non può cancellare l''audit'
);

SELECT throws_ok(
    $$ INSERT INTO sec.ruoli (codice, nome, di_sistema) VALUES ('ruolo_pirata', 'Pirata', true) $$,
    '42501',
    NULL,
    'app_api non può creare ruoli'
);

-- UPDATE e DELETE sulle righe di Beta non trovano nulla: la RLS le nasconde.
UPDATE sec.utenti SET nome = 'Manomesso' WHERE utente_id = '01900000-0000-7000-8000-0000000000b1';
DELETE FROM sec.utenti_ruoli WHERE utente_id = '01900000-0000-7000-8000-0000000000b1';

-- ---------------------------------------------------------------------------
-- app_api come azienda Beta
-- ---------------------------------------------------------------------------

SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000b', true);

SELECT results_eq(
    'SELECT email FROM sec.utenti',
    $$ VALUES ('anna.bianchi@beta.example'::text) $$,
    'Beta vede solo i propri utenti'
);

RESET ROLE;

SELECT is(
    (SELECT nome FROM sec.utenti WHERE utente_id = '01900000-0000-7000-8000-0000000000b1'),
    'Anna',
    'l''UPDATE di Alfa non ha toccato l''utente di Beta'
);
SELECT is(
    (SELECT count(*) FROM sec.utenti_ruoli WHERE utente_id = '01900000-0000-7000-8000-0000000000b1'),
    1::bigint,
    'il DELETE di Alfa non ha tolto i ruoli dell''utente di Beta'
);

-- ---------------------------------------------------------------------------
-- app_chatbot non vede lo schema sec
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_chatbot;

SELECT throws_ok(
    'SELECT count(*) FROM sec.utenti',
    '42501',
    NULL,
    'app_chatbot non può leggere sec.utenti'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

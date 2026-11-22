-- Autenticazione: primo avvio, email unica, dati di accesso, blocco progressivo,
-- TOTP, codici di recupero, rotazione e revoca dei token di rinnovo.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(48);

-- ---------------------------------------------------------------------------
-- Primo avvio (come app_api, che ha il permesso di eseguirlo)
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;

SELECT is(sec.installazione_da_inizializzare(), true, 'senza aziende l''installazione è da inizializzare');

SELECT lives_ok(
    $$ SELECT sec.inizializza_installazione(
           'Termoidraulica Alfa Srl (fittizia)', '00000000001', 'toscana',
           ' Titolare@Alfa.example ', 'Mario', 'Rossi', '$argon2id$v=19$m=19456,t=2,p=1$ZmFrZQ$ZmFrZQ') $$,
    'app_api può inizializzare l''installazione'
);

SELECT is(sec.installazione_da_inizializzare(), false, 'dopo il primo avvio non è più da inizializzare');

SELECT throws_ok(
    $$ SELECT sec.inizializza_installazione(
           'Altra Srl (fittizia)', '00000000002', 'toscana',
           'altro@altra.example', 'Ugo', 'Verdi', '$argon2id$v=19$m=19456,t=2,p=1$ZmFrZQ$ZmFrZQ') $$,
    '55000',
    'installazione già inizializzata',
    'il primo avvio non si può ripetere'
);

RESET ROLE;

SELECT is(
    (SELECT email FROM sec.utenti),
    'Titolare@Alfa.example',
    'l''email del titolare è salvata senza spazi'
);

SELECT set_eq(
    $$ SELECT r.codice FROM sec.utenti_ruoli ur JOIN sec.ruoli r USING (ruolo_id) $$,
    ARRAY['titolare', 'amministratore_sistema'],
    'senza amministratore separato il titolare riceve entrambi i ruoli'
);

SELECT bag_eq(
    $$ SELECT azione FROM sec.audit_log $$,
    ARRAY['installazione.inizializzata', 'utenti_ruoli.insert', 'utenti_ruoli.insert'],
    'il primo avvio e l''assegnazione dei ruoli finiscono nell''audit'
);

-- Utenti di prova nell'azienda Alfa e una seconda azienda Beta.
SELECT set_config('app.azienda_id', (SELECT azienda_id::text FROM sec.aziende), true);

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash)
SELECT v.utente_id, a.azienda_id, v.email, v.nome, 'Prova', '$argon2id$v=19$m=19456,t=2,p=1$ZmFrZQ$ZmFrZQ'
FROM sec.aziende a
CROSS JOIN (VALUES
    ('01900000-0000-7000-8000-0000000000a2'::uuid, 'tecnico@alfa.example',   'Luca'),
    ('01900000-0000-7000-8000-0000000000a3'::uuid, 'misto@alfa.example',     'Sara'),
    ('01900000-0000-7000-8000-0000000000a4'::uuid, 'sospeso@alfa.example',   'Gino')
) AS v (utente_id, email, nome);

INSERT INTO sec.utenti_ruoli (azienda_id, utente_id, ruolo_id)
SELECT u.azienda_id, u.utente_id, r.ruolo_id
FROM sec.utenti u
JOIN (VALUES
    ('tecnico@alfa.example', 'tecnico'),
    ('misto@alfa.example',   'tecnico'),
    ('misto@alfa.example',   'responsabile_tecnico'),
    ('sospeso@alfa.example', 'tecnico')
) AS m (email, ruolo) ON m.email = u.email
JOIN sec.ruoli r ON r.codice = m.ruolo AND r.azienda_id IS NULL;

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione)
VALUES ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)', 'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash)
VALUES ('01900000-0000-7000-8000-0000000000b1', '01900000-0000-7000-8000-00000000000b',
        'anna@beta.example', 'Anna', 'Bianchi', '$argon2id$v=19$m=19456,t=2,p=1$ZmFrZQ$ZmFrZQ');

-- ---------------------------------------------------------------------------
-- Email unica su tutto il database
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ INSERT INTO sec.utenti (azienda_id, email, nome, cognome, password_hash)
       VALUES ('01900000-0000-7000-8000-00000000000b', 'TECNICO@alfa.example', 'Doppio', 'Prova',
               '$argon2id$v=19$m=19456,t=2,p=1$ZmFrZQ$ZmFrZQ') $$,
    '23505',
    NULL,
    'la stessa email non può esistere in due aziende, nemmeno con maiuscole diverse'
);

-- ---------------------------------------------------------------------------
-- Ruoli: secondo fattore e permessi effettivi
-- ---------------------------------------------------------------------------

SELECT is(sec.richiede_mfa((SELECT utente_id FROM sec.utenti WHERE email = 'Titolare@Alfa.example')), true,
    'il titolare deve usare il secondo fattore');
SELECT is(sec.richiede_mfa('01900000-0000-7000-8000-0000000000a2'), false,
    'il tecnico non è obbligato al secondo fattore');

SELECT results_eq(
    $$ SELECT ambito FROM sec.permessi_utente('01900000-0000-7000-8000-0000000000a2') WHERE codice = 'clienti.lettura' $$,
    $$ VALUES ('propri'::text) $$,
    'il tecnico legge solo i propri clienti'
);
SELECT results_eq(
    $$ SELECT ambito FROM sec.permessi_utente('01900000-0000-7000-8000-0000000000a3') WHERE codice = 'clienti.lettura' $$,
    $$ VALUES ('tutti'::text) $$,
    'con tecnico e responsabile tecnico vale l''ambito più ampio'
);
SELECT is_empty(
    $$ SELECT 1 FROM sec.permessi_utente('01900000-0000-7000-8000-0000000000a2') WHERE codice = 'preventivi.lettura' $$,
    'il tecnico non vede i preventivi'
);
SELECT ok(
    EXISTS (SELECT 1 FROM sec.permessi_utente((SELECT utente_id FROM sec.utenti WHERE email = 'Titolare@Alfa.example'))
            WHERE codice = 'sistema.scrittura'),
    'il titolare con anche il ruolo di amministratore ha i permessi di sistema'
);

-- ---------------------------------------------------------------------------
-- Dati di accesso: funzionano senza azienda impostata
-- ---------------------------------------------------------------------------

SELECT set_config('app.azienda_id', '', true);
SET LOCAL ROLE app_api;

SELECT results_eq(
    $$ SELECT utente_id, stato, mfa_attivo FROM sec.dati_accesso('  TECNICO@ALFA.example ') $$,
    $$ VALUES ('01900000-0000-7000-8000-0000000000a2'::uuid, 'attivo'::text, false) $$,
    'trova l''utente per email ignorando maiuscole e spazi, senza azienda impostata'
);
SELECT is_empty(
    $$ SELECT 1 FROM sec.dati_accesso('nessuno@alfa.example') $$,
    'un''email sconosciuta non restituisce nulla'
);
SELECT is((SELECT count(*) FROM sec.utenti), 0::bigint, 'fuori dalla funzione la RLS continua a nascondere gli utenti');

RESET ROLE;
SET LOCAL ROLE app_chatbot;

SELECT throws_ok(
    $$ SELECT * FROM sec.dati_accesso('tecnico@alfa.example') $$,
    '42501',
    NULL,
    'app_chatbot non può leggere i dati di accesso'
);

RESET ROLE;

-- ---------------------------------------------------------------------------
-- Blocco progressivo (sotto RLS, come fa l'API dopo aver letto l'azienda)
-- ---------------------------------------------------------------------------

SELECT set_config('app.azienda_id', (SELECT azienda_id::text FROM sec.utenti WHERE email = 'tecnico@alfa.example'), true);
SET LOCAL ROLE app_api;

SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), NULL, '1° errore: nessun blocco');
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), NULL, '2° errore: nessun blocco');
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), NULL, '3° errore: nessun blocco');
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), NULL, '4° errore: nessun blocco');
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), now() + interval '1 minute', '5° errore: blocco di 1 minuto');
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), now() + interval '2 minutes', '6° errore: blocco di 2 minuti');

UPDATE sec.utenti SET tentativi_falliti = 20 WHERE utente_id = '01900000-0000-7000-8000-0000000000a2';
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', false), now() + interval '1 hour', 'il blocco non supera un''ora');

SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000a2', true), NULL, 'un accesso riuscito non restituisce blocchi');
SELECT results_eq(
    $$ SELECT tentativi_falliti, bloccato_fino_al, ultimo_accesso_il FROM sec.utenti
       WHERE utente_id = '01900000-0000-7000-8000-0000000000a2' $$,
    $$ VALUES (0, NULL::timestamptz, now()) $$,
    'un accesso riuscito azzera tentativi e blocco e registra l''ultimo accesso'
);
SELECT is(sec.registra_esito_accesso('01900000-0000-7000-8000-0000000000b1', false), NULL,
    'non si possono bloccare utenti di un''altra azienda');

-- ---------------------------------------------------------------------------
-- TOTP: lo stesso passo non vale due volte
-- ---------------------------------------------------------------------------

SELECT is(sec.registra_passo_totp('01900000-0000-7000-8000-0000000000a2', 1000), true,  'il primo passo è accettato');
SELECT is(sec.registra_passo_totp('01900000-0000-7000-8000-0000000000a2', 1000), false, 'lo stesso passo è rifiutato');
SELECT is(sec.registra_passo_totp('01900000-0000-7000-8000-0000000000a2', 999),  false, 'un passo precedente è rifiutato');
SELECT is(sec.registra_passo_totp('01900000-0000-7000-8000-0000000000a2', 1001), true,  'il passo successivo è accettato');

-- ---------------------------------------------------------------------------
-- Codici di recupero monouso
-- ---------------------------------------------------------------------------

INSERT INTO sec.codici_recupero (azienda_id, utente_id, codice_hash)
VALUES (sec.azienda_corrente(), '01900000-0000-7000-8000-0000000000a2', sha256('codice-di-prova'::bytea));

SELECT is(sec.usa_codice_recupero('01900000-0000-7000-8000-0000000000a2', sha256('codice-di-prova'::bytea)), true,
    'un codice di recupero valido è accettato');
SELECT is(sec.usa_codice_recupero('01900000-0000-7000-8000-0000000000a2', sha256('codice-di-prova'::bytea)), false,
    'lo stesso codice di recupero non vale due volte');
SELECT is(sec.usa_codice_recupero('01900000-0000-7000-8000-0000000000a2', sha256('codice-inventato'::bytea)), false,
    'un codice inesistente è rifiutato');

-- ---------------------------------------------------------------------------
-- Token di rinnovo: rotazione, riuso, scadenza, utente sospeso, revoca
-- ---------------------------------------------------------------------------

INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, token_hash, scade_il)
VALUES (sec.azienda_corrente(), '01900000-0000-7000-8000-0000000000a2', sha256('token-a'::bytea), now() + interval '12 hours');

RESET ROLE;
SELECT set_config('app.azienda_id', '', true);
SET LOCAL ROLE app_api;

SELECT results_eq(
    $$ SELECT esito, utente_id FROM sec.ruota_sessione(sha256('token-a'::bytea), sha256('token-b'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('ok'::text, '01900000-0000-7000-8000-0000000000a2'::uuid) $$,
    'un token valido viene ruotato senza azienda impostata'
);
SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-b'::bytea), sha256('token-c'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('ok'::text) $$,
    'anche il token nuovo si può ruotare'
);
SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-a'::bytea), sha256('token-x'::bytea), 'attaccante', '10.0.0.66') $$,
    $$ VALUES ('riuso'::text) $$,
    'riusare un token già ruotato è rilevato'
);
SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-c'::bytea), sha256('token-d'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('revocato'::text) $$,
    'dopo un riuso anche l''ultimo token della catena è revocato'
);
SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-mai-visto'::bytea), sha256('token-e'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('sconosciuto'::text) $$,
    'un token sconosciuto è rifiutato'
);

RESET ROLE;

-- Dati di controllo, come superutente.
SELECT is(
    (SELECT count(*) FROM sec.sessioni_rinnovo WHERE revocata_il IS NULL),
    0::bigint,
    'dopo il riuso nessun token della catena resta valido'
);
SELECT is(
    (SELECT count(*) FROM sec.sessioni_rinnovo WHERE token_hash = sha256('token-x'::bytea)),
    0::bigint,
    'il riuso non crea un token per l''attaccante'
);

INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, token_hash, creata_il, scade_il)
SELECT azienda_id, '01900000-0000-7000-8000-0000000000a2', sha256('token-vecchio'::bytea), '2020-01-01', '2020-01-02'
FROM sec.aziende WHERE regione = 'toscana' AND partita_iva = '00000000001';

UPDATE sec.utenti SET stato = 'sospeso' WHERE utente_id = '01900000-0000-7000-8000-0000000000a4';
INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, token_hash, scade_il)
SELECT azienda_id, utente_id, sha256('token-sospeso'::bytea), now() + interval '12 hours'
FROM sec.utenti WHERE utente_id = '01900000-0000-7000-8000-0000000000a4';

SET LOCAL ROLE app_api;

SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-vecchio'::bytea), sha256('token-f'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('scaduto'::text) $$,
    'un token scaduto è rifiutato'
);
SELECT results_eq(
    $$ SELECT esito FROM sec.ruota_sessione(sha256('token-sospeso'::bytea), sha256('token-g'::bytea), 'prova', '127.0.0.1') $$,
    $$ VALUES ('utente_non_attivo'::text) $$,
    'il token di un utente sospeso è rifiutato'
);

-- Revoca all'uscita: solo i token dell'utente indicato, sotto RLS.
RESET ROLE;
INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, token_hash, scade_il)
SELECT azienda_id, utente_id, sha256('token-uscita'::bytea), now() + interval '12 hours'
FROM sec.utenti WHERE utente_id = '01900000-0000-7000-8000-0000000000a3';
SELECT set_config('app.azienda_id', (SELECT azienda_id::text FROM sec.utenti WHERE utente_id = '01900000-0000-7000-8000-0000000000a3'), true);
SET LOCAL ROLE app_api;

SELECT is(sec.revoca_sessione('01900000-0000-7000-8000-0000000000a2', sha256('token-uscita'::bytea), 'uscita'), 0,
    'un utente non può revocare i token di un altro');
SELECT is(sec.revoca_sessione('01900000-0000-7000-8000-0000000000a3', sha256('token-uscita'::bytea), 'uscita'), 1,
    'all''uscita il token dell''utente viene revocato');

-- Isolamento delle sessioni tra aziende.
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000b', true);

SELECT is((SELECT count(*) FROM sec.sessioni_rinnovo), 0::bigint, 'Beta non vede le sessioni di Alfa');
SELECT is((SELECT count(*) FROM sec.codici_recupero), 0::bigint, 'Beta non vede i codici di recupero di Alfa');

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

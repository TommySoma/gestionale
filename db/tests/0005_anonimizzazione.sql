-- Anonimizzazione di un cliente (GDPR): dati personali cancellati, contatti tolti,
-- sedi e storico dei consensi conservati senza testo libero, audit senza dati
-- personali, isolamento tra aziende, irreversibilità e permesso.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(24);

-- ---------------------------------------------------------------------------
-- Dati di prova
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01900000-0000-7000-8000-0000000000a1', '01900000-0000-7000-8000-00000000000a',
     'titolare@alfa.example', 'Anna', 'Titolare', '$argon2id$v=19$m=19456,t=2,p=1$ZmludG8$ZmludG8');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome, codice_fiscale, pec, note) VALUES
    ('01900000-0000-7000-8000-0000000000c1', '01900000-0000-7000-8000-00000000000a', 'privato',
     'Giulia', 'Neri', 'NREGLI85M41D612X', 'giulia.neri@pec.example', 'Preferisce essere chiamata la mattina');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, denominazione, partita_iva) VALUES
    ('01900000-0000-7000-8000-0000000000c2', '01900000-0000-7000-8000-00000000000b', 'azienda',
     'Officina Beta Srl (fittizia)', '01234567897');

INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale) VALUES
    ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'cellulare', '+39 333 000 0000', true),
    ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'email', 'giulia.neri@esempio.example', true);

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, descrizione, indirizzo, civico, cap, comune, provincia, note_accesso) VALUES
    ('01900000-0000-7000-8000-0000000000d1', '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1',
     'Casa di Giulia', 'Via dei Platani', '12', '50100', 'Firenze', 'FI', 'Citofono Neri');

INSERT INTO anagrafica.consensi_privacy (azienda_id, cliente_id, finalita_privacy_id, concesso, fonte, note)
SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', finalita_privacy_id, true, 'telefono', 'Parlato con Giulia Neri'
FROM anagrafica.finalita_privacy WHERE codice = 'promemoria_scadenze';

-- ---------------------------------------------------------------------------
-- Struttura, vincoli e permesso
-- ---------------------------------------------------------------------------

SELECT has_function('anagrafica', 'anonimizza_cliente', ARRAY['uuid', 'integer'], 'esiste anagrafica.anonimizza_cliente');
SELECT ok(has_function_privilege('app_api', 'anagrafica.anonimizza_cliente(uuid, integer)', 'EXECUTE'),
    'app_api può eseguire l''anonimizzazione');

SELECT set_eq(
    $$ SELECT r.codice FROM sec.ruoli_permessi rp
       JOIN sec.ruoli r    ON r.ruolo_id = rp.ruolo_id
       JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
       WHERE p.codice = 'clienti.anonimizzazione' $$,
    ARRAY['titolare'],
    'solo il titolare può anonimizzare un cliente'
);

SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, nome, cognome, anonimizzato_il, eliminato_il)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'privato', 'Mario', 'Rossi', now(), now()) $$,
    '23514', NULL,
    'un cliente anonimizzato non può avere nome e cognome'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, anonimizzato_il)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'privato', now()) $$,
    '23514', NULL,
    'un cliente anonimizzato è anche eliminato dagli elenchi'
);

-- ---------------------------------------------------------------------------
-- Anonimizzazione come app_api
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);
SELECT set_config('app.utente_id', '01900000-0000-7000-8000-0000000000a1', true);

SELECT is(anagrafica.anonimizza_cliente('01900000-0000-7000-8000-0000000000c1', 99), false,
    'con una versione vecchia il cliente non viene anonimizzato');
SELECT is(anagrafica.anonimizza_cliente('01900000-0000-7000-8000-0000000000c2', 1), false,
    'il cliente di un''altra azienda non si può anonimizzare');
SELECT is(anagrafica.anonimizza_cliente('01900000-0000-7000-8000-0000000000c1', 1), true,
    'con la versione giusta il cliente viene anonimizzato');
SELECT is(anagrafica.anonimizza_cliente('01900000-0000-7000-8000-0000000000c1', 2), false,
    'un cliente già anonimizzato non si anonimizza una seconda volta');

SELECT results_eq(
    $$ SELECT nome, cognome, codice_fiscale, pec, note, nome_ricerca
       FROM anagrafica.clienti WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    $$ VALUES (NULL::text, NULL::text, NULL::text, NULL::text, NULL::text, ''::text) $$,
    'nominativo, codice fiscale, PEC e note sono cancellati'
);
SELECT results_eq(
    $$ SELECT tipo, eliminato_il IS NOT NULL, anonimizzato_il IS NOT NULL, anonimizzato_da
       FROM anagrafica.clienti WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    $$ VALUES ('privato'::text, true, true, '01900000-0000-7000-8000-0000000000a1'::uuid) $$,
    'il cliente resta come riferimento: tipo conservato, eliminato e anonimizzato da chi ha agito'
);
SELECT is((SELECT count(*) FROM anagrafica.contatti WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1'), 0::bigint,
    'i contatti sono cancellati');
SELECT results_eq(
    $$ SELECT indirizzo, comune, descrizione, note_accesso, eliminata_il IS NOT NULL
       FROM anagrafica.sedi WHERE sede_id = '01900000-0000-7000-8000-0000000000d1' $$,
    $$ VALUES ('Via dei Platani'::text, 'Firenze'::text, NULL::text, NULL::text, true) $$,
    'la sede conserva l''indirizzo dell''impianto ma perde descrizione e note di accesso'
);
SELECT results_eq(
    $$ SELECT concesso, fonte, note FROM anagrafica.consensi_privacy
       WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    $$ VALUES (true, 'telefono'::text, NULL::text) $$,
    'lo storico dei consensi resta, senza le note'
);

SELECT results_eq(
    $$ SELECT azione, entita, utente_id FROM sec.audit_log
       WHERE entita_id = '01900000-0000-7000-8000-0000000000c1' $$,
    $$ VALUES ('cliente.anonimizzato'::text, 'clienti'::text, '01900000-0000-7000-8000-0000000000a1'::uuid) $$,
    'l''anonimizzazione è registrata nell''audit'
);
SELECT is(
    (SELECT dettaglio FROM sec.audit_log WHERE entita_id = '01900000-0000-7000-8000-0000000000c1'),
    '{"contatti_cancellati": 2, "sedi_ripulite": 1, "note_consensi_cancellate": 1}'::jsonb,
    'l''audit contiene solo conteggi, nessun dato personale'
);

-- ---------------------------------------------------------------------------
-- Irreversibilità e storico
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ UPDATE anagrafica.clienti SET nome = 'Giulia', cognome = 'Neri'
       WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    '42501', NULL,
    'un cliente anonimizzato non si modifica più'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.clienti SET eliminato_il = NULL
       WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    '42501', NULL,
    'un cliente anonimizzato non torna negli elenchi'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.consensi_privacy SET concesso = false $$,
    '42501', NULL,
    'app_api non può cambiare l''esito di un consenso'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.consensi_privacy SET note = 'altro testo' $$,
    '42501', NULL,
    'le note di un consenso si possono solo cancellare, non riscrivere'
);
SELECT throws_ok(
    $$ DELETE FROM anagrafica.consensi_privacy $$,
    '42501', NULL,
    'app_api non può cancellare lo storico dei consensi'
);

RESET ROLE;

SELECT throws_ok(
    $$ DELETE FROM anagrafica.consensi_privacy $$,
    '42501', NULL,
    'lo storico dei consensi non si cancella, nemmeno dal superutente'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.consensi_privacy SET concesso = false, note = NULL $$,
    '42501', NULL,
    'insieme alle note non si può cambiare nient''altro'
);

SELECT is(
    (SELECT denominazione FROM anagrafica.clienti WHERE cliente_id = '01900000-0000-7000-8000-0000000000c2'),
    'Officina Beta Srl (fittizia)',
    'il cliente dell''altra azienda è intatto'
);

SELECT * FROM finish();

ROLLBACK;

-- Schema anagrafica: vincoli, ricerca, contatto principale, storico dei consensi,
-- vista dei consensi correnti sotto RLS, cancellazione logica.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(30);

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende, ciascuna con un cliente
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome, codice_fiscale) VALUES
    ('01900000-0000-7000-8000-0000000000c1', '01900000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri', 'NREGLI85M41D612X');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, denominazione, codice_fiscale) VALUES
    ('01900000-0000-7000-8000-0000000000c2', '01900000-0000-7000-8000-00000000000b', 'condominio', 'Condominio Le Querce (fittizio)', '90000000001');

-- ---------------------------------------------------------------------------
-- Struttura e vincoli dei clienti
-- ---------------------------------------------------------------------------

SELECT has_schema('anagrafica', 'esiste lo schema anagrafica');
SELECT has_table('anagrafica', 'clienti', 'esiste anagrafica.clienti');
SELECT has_table('anagrafica', 'sedi', 'esiste anagrafica.sedi');
SELECT has_table('anagrafica', 'contatti', 'esiste anagrafica.contatti');
SELECT has_table('anagrafica', 'consensi_privacy', 'esiste anagrafica.consensi_privacy');

SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'privato', 'Senza nome Srl') $$,
    '23514', NULL,
    'un privato senza nome e cognome è rifiutato'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, nome, cognome)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'condominio', 'Mario', 'Rossi') $$,
    '23514', NULL,
    'un condominio senza denominazione è rifiutato'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'ente', 'Comune di Prova') $$,
    '23514', NULL,
    'un tipo di cliente sconosciuto è rifiutato'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, nome, cognome, codice_fiscale)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'privato', 'Mario', 'Rossi', 'nregli85m41d612x') $$,
    '23514', NULL,
    'il codice fiscale va salvato in maiuscolo'
);

SELECT is(
    (SELECT nome_ricerca FROM anagrafica.clienti WHERE cliente_id = '01900000-0000-7000-8000-0000000000c1'),
    'neri giulia',
    'il testo di ricerca di un privato è cognome e nome in minuscolo'
);

INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione, partita_iva)
VALUES ('01900000-0000-7000-8000-00000000000a', 'azienda', 'Officina Prova Srl (fittizia)', '01234567897');

SELECT throws_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione, partita_iva)
       VALUES ('01900000-0000-7000-8000-00000000000a', 'azienda', 'Doppione Srl', '01234567897') $$,
    '23505', NULL,
    'la stessa partita IVA non può avere due clienti attivi nella stessa azienda'
);
SELECT lives_ok(
    $$ INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione, partita_iva)
       VALUES ('01900000-0000-7000-8000-00000000000b', 'azienda', 'Officina Prova Srl (fittizia)', '01234567897') $$,
    'un''altra azienda può avere un cliente con la stessa partita IVA'
);

-- ---------------------------------------------------------------------------
-- Contatti e sedi
-- ---------------------------------------------------------------------------

INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale) VALUES
    ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'cellulare', '+39 333 000 0000', true);

SELECT throws_ok(
    $$ INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'cellulare', '+39 333 111 1111', true) $$,
    '23505', NULL,
    'un solo cellulare principale per cliente'
);
SELECT lives_ok(
    $$ INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'email', 'giulia.neri@esempio.example', true) $$,
    'un''email principale accanto al cellulare principale è ammessa'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'email', 'non-una-email') $$,
    '23514', NULL,
    'un''email senza chiocciola è rifiutata'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore)
       VALUES ('01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000c1', 'telefono', '055 000000') $$,
    '23503', NULL,
    'un contatto non può collegare il cliente di un''altra azienda'
);

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia) VALUES
    ('01900000-0000-7000-8000-0000000000d1', '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1',
     'Via dei Platani', '12/A', '50100', 'Firenze', 'FI');

SELECT is(
    (SELECT indirizzo_normalizzato FROM anagrafica.sedi WHERE sede_id = '01900000-0000-7000-8000-0000000000d1'),
    'via dei platani 12 a 50100 firenze',
    'l''indirizzo normalizzato ignora maiuscole e punteggiatura'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.sedi (azienda_id, cliente_id, indirizzo, cap, comune, provincia, latitudine)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'Via Prova', '50100', 'Firenze', 'FI', 43.77) $$,
    '23514', NULL,
    'latitudine senza longitudine è rifiutata'
);
SELECT throws_ok(
    $$ INSERT INTO anagrafica.sedi (azienda_id, cliente_id, indirizzo, cap, comune, provincia)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', 'Via Prova', '5010', 'Firenze', 'fi') $$,
    '23514', NULL,
    'CAP e provincia devono avere il formato corretto'
);

-- ---------------------------------------------------------------------------
-- Consensi: storico immutabile e consenso corrente
-- ---------------------------------------------------------------------------

INSERT INTO anagrafica.consensi_privacy (azienda_id, cliente_id, finalita_privacy_id, concesso, fonte, registrato_il)
SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1', finalita_privacy_id, v.concesso, 'modulo_firmato', v.il
FROM anagrafica.finalita_privacy
CROSS JOIN (VALUES (true, '2026-01-10 09:00+01'::timestamptz), (false, '2026-06-01 09:00+02'::timestamptz)) AS v (concesso, il)
WHERE codice = 'comunicazioni_commerciali';

INSERT INTO anagrafica.consensi_privacy (azienda_id, cliente_id, finalita_privacy_id, concesso, fonte)
SELECT '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000c2', finalita_privacy_id, true, 'email'
FROM anagrafica.finalita_privacy WHERE codice = 'promemoria_scadenze';

SELECT set_eq(
    'SELECT codice FROM anagrafica.finalita_privacy WHERE attiva',
    ARRAY['promemoria_scadenze', 'comunicazioni_commerciali'],
    'le finalità iniziali sono promemoria scadenze e comunicazioni commerciali'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.consensi_privacy SET concesso = true $$,
    '42501', NULL,
    'lo storico dei consensi non si modifica, nemmeno dal superutente'
);
SELECT throws_ok(
    $$ DELETE FROM anagrafica.consensi_privacy $$,
    '42501', NULL,
    'lo storico dei consensi non si cancella'
);

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);

SELECT results_eq(
    $$ SELECT finalita, concesso FROM anagrafica.v_consensi_correnti $$,
    $$ VALUES ('comunicazioni_commerciali'::text, false) $$,
    'il consenso corrente è l''ultimo registrato, e la vista mostra solo l''azienda corrente'
);
SELECT throws_ok(
    $$ UPDATE anagrafica.consensi_privacy SET note = 'modificato' $$,
    '42501', NULL,
    'app_api non ha il permesso di modificare i consensi'
);

-- ---------------------------------------------------------------------------
-- Isolamento e cancellazione logica (come app_api)
-- ---------------------------------------------------------------------------

SELECT is((SELECT count(*) FROM anagrafica.clienti), 2::bigint, 'Alfa vede solo i propri due clienti');
SELECT is((SELECT count(*) FROM anagrafica.contatti), 2::bigint, 'Alfa vede solo i propri contatti');

SELECT is(anagrafica.elimina_cliente('01900000-0000-7000-8000-0000000000c1', 99), false,
    'con una versione vecchia il cliente non viene eliminato');
SELECT is(anagrafica.elimina_cliente('01900000-0000-7000-8000-0000000000c2', 1), false,
    'il cliente di un''altra azienda non si può eliminare');
SELECT is(anagrafica.elimina_cliente('01900000-0000-7000-8000-0000000000c1', 1), true,
    'con la versione giusta il cliente viene eliminato');

SELECT results_eq(
    $$ SELECT c.eliminato_il IS NOT NULL, s.eliminata_il IS NOT NULL
       FROM anagrafica.clienti c JOIN anagrafica.sedi s USING (cliente_id)
       WHERE c.cliente_id = '01900000-0000-7000-8000-0000000000c1' $$,
    $$ VALUES (true, true) $$,
    'eliminare un cliente elimina logicamente anche le sue sedi'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

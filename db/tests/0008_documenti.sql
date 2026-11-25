-- Schema documenti: tipi e dimensioni ammessi, percorso senza risalite, entità
-- della stessa azienda, file immutabile, eliminazione solo logica, RLS e permessi.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(16);

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01900000-0000-7000-8000-0000000000c1', '01900000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri'),
    ('01900000-0000-7000-8000-0000000000c2', '01900000-0000-7000-8000-00000000000b', 'privato', 'Paolo', 'Verdi');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, cap, comune, provincia) VALUES
    ('01900000-0000-7000-8000-0000000000d1', '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000c1',
     'Via dei Platani 12', '50100', 'Firenze', 'FI'),
    ('01900000-0000-7000-8000-0000000000d2', '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000c2',
     'Via dei Tigli 3', '56100', 'Pisa', 'PI');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01900000-0000-7000-8000-0000000000e1', '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000d1', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento';

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01900000-0000-7000-8000-0000000000e2', '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000d2', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_climatizzazione';

-- Un documento valido di Alfa e uno di Beta.
INSERT INTO documenti.documenti (documento_id, azienda_id, entita, entita_id, categoria, nome_originale, tipo_mime, dimensione, sha256, percorso)
VALUES
    ('01900000-0000-7000-8000-000000000301', '01900000-0000-7000-8000-00000000000a', 'impianto', '01900000-0000-7000-8000-0000000000e1',
     'foto', 'caldaia.jpg', 'image/jpeg', 1024, sha256('foto'::bytea),
     '01900000-0000-7000-8000-00000000000a/01900000-0000-7000-8000-000000000301'),
    ('01900000-0000-7000-8000-000000000302', '01900000-0000-7000-8000-00000000000b', 'impianto', '01900000-0000-7000-8000-0000000000e2',
     'documento', 'libretto.pdf', 'application/pdf', 2048, sha256('pdf'::bytea),
     '01900000-0000-7000-8000-00000000000b/01900000-0000-7000-8000-000000000302');

-- Inserimento con i campi variabili; gli altri sono validi.
CREATE FUNCTION pg_temp.documento(
    p_categoria text DEFAULT 'foto', p_mime text DEFAULT 'image/png', p_dimensione bigint DEFAULT 10,
    p_sha bytea DEFAULT sha256('x'::bytea), p_percorso text DEFAULT NULL,
    p_impianto uuid DEFAULT '01900000-0000-7000-8000-0000000000e1')
RETURNS void
LANGUAGE sql
AS $$
    INSERT INTO documenti.documenti (azienda_id, entita, entita_id, categoria, nome_originale, tipo_mime, dimensione, sha256, percorso)
    VALUES ('01900000-0000-7000-8000-00000000000a', 'impianto', p_impianto, p_categoria, 'prova', p_mime, p_dimensione, p_sha,
            coalesce(p_percorso, '01900000-0000-7000-8000-00000000000a/' || uuidv7()::text))
$$;

SELECT has_table('documenti', 'documenti', 'esiste documenti.documenti');
SELECT lives_ok($$ SELECT pg_temp.documento() $$, 'una foto PNG valida si registra');
SELECT throws_ok($$ SELECT pg_temp.documento(p_categoria => 'foto', p_mime => 'application/pdf') $$, '23514', NULL, 'un PDF non è una foto');
SELECT throws_ok($$ SELECT pg_temp.documento(p_mime => 'image/gif') $$, '23514', NULL, 'il GIF non è un tipo ammesso');
SELECT throws_ok($$ SELECT pg_temp.documento(p_dimensione => 20971521) $$, '23514', NULL, 'oltre 20 MB il file è rifiutato');
SELECT throws_ok($$ SELECT pg_temp.documento(p_sha => '\x0102'::bytea) $$, '23514', NULL, 'l''impronta SHA-256 è di 32 byte');
SELECT throws_ok(
    $$ SELECT pg_temp.documento(p_percorso => '../../etc/01900000-0000-7000-8000-000000000999') $$,
    '23514', NULL, 'il percorso non risale le cartelle');
SELECT throws_ok(
    $$ SELECT pg_temp.documento(p_impianto => '01900000-0000-7000-8000-0000000000e2') $$,
    '23503', NULL, 'un documento non si collega all''impianto di un''altra azienda');
SELECT throws_ok(
    $$ SELECT pg_temp.documento(p_impianto => '01900000-0000-7000-8000-0000000000ff') $$,
    '23503', NULL, 'un documento non si collega a un impianto che non esiste');

SELECT throws_ok(
    $$ UPDATE documenti.documenti SET sha256 = sha256('altro'::bytea) WHERE documento_id = '01900000-0000-7000-8000-000000000301' $$,
    '23514', NULL, 'il file di un documento non cambia');

UPDATE documenti.documenti SET descrizione = 'Caldaia vista di fronte' WHERE documento_id = '01900000-0000-7000-8000-000000000301';
SELECT is(
    (SELECT versione FROM documenti.documenti WHERE documento_id = '01900000-0000-7000-8000-000000000301'),
    2, 'la descrizione si modifica e la versione cresce');

UPDATE documenti.documenti SET eliminato_il = now() WHERE documento_id = '01900000-0000-7000-8000-000000000301';
SELECT throws_ok(
    $$ UPDATE documenti.documenti SET eliminato_il = NULL WHERE documento_id = '01900000-0000-7000-8000-000000000301' $$,
    '23514', NULL, 'un documento eliminato non torna visibile da qui');

SELECT is(
    (SELECT ambito FROM sec.ruoli_permessi rp
     JOIN sec.ruoli r ON r.ruolo_id = rp.ruolo_id JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
     WHERE r.codice = 'tecnico' AND p.codice = 'documenti.scrittura'),
    'propri', 'il Tecnico carica documenti solo sui propri impianti');

-- Row-Level Security e privilegi (come app_api)
SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);

SELECT is((SELECT count(*) FROM documenti.documenti), 2::bigint, 'Alfa vede solo i propri documenti');
SELECT is(
    (SELECT count(*) FROM documenti.documenti WHERE documento_id = '01900000-0000-7000-8000-000000000302'),
    0::bigint, 'il documento di un''altra azienda non si vede');
SELECT throws_ok($$ DELETE FROM documenti.documenti $$, '42501', NULL, 'app_api non cancella i documenti: li elimina logicamente');

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

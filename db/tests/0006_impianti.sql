-- Schema impianti: lookup con voci di prova, vincoli di impianti, riferimenti al
-- catasto e generatori, PDR unico tra gli attivi, chiavi composte tra aziende,
-- metadati e Row-Level Security.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(27);

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende, ciascuna con un cliente e una sede
-- ---------------------------------------------------------------------------

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

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id, combustibile_id, potenza_utile_kw, pdr,
                               proprietario_cliente_id, responsabile_cliente_id)
SELECT '01900000-0000-7000-8000-0000000000e1', '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000d1',
       t.tipo_impianto_id, c.combustibile_id, 24.00, 'PDR-PROVA-0001',
       '01900000-0000-7000-8000-0000000000c1', '01900000-0000-7000-8000-0000000000c1'
FROM impianti.tipi_impianto t, impianti.combustibili c
WHERE t.codice = 'prova_riscaldamento' AND c.codice = 'prova_gas_naturale';

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01900000-0000-7000-8000-0000000000e2', '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000d2', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_climatizzazione';

-- ---------------------------------------------------------------------------
-- Struttura e lookup
-- ---------------------------------------------------------------------------

SELECT has_schema('impianti', 'esiste lo schema impianti');
SELECT has_table('impianti', 'impianti', 'esiste impianti.impianti');
SELECT has_table('impianti', 'generatori', 'esiste impianti.generatori');
SELECT has_table('impianti', 'riferimenti_catasto', 'esiste impianti.riferimenti_catasto');

SELECT is_empty(
    $$ SELECT codice FROM impianti.tipi_impianto WHERE NOT da_verificare
       UNION ALL SELECT codice FROM impianti.tipi_generatore WHERE NOT da_verificare
       UNION ALL SELECT codice FROM impianti.combustibili WHERE NOT da_verificare $$,
    'le voci iniziali delle lookup sono tutte di prova, da verificare'
);
SELECT throws_ok(
    $$ INSERT INTO impianti.combustibili (codice, nome, valido_dal, da_verificare)
       VALUES ('gasolio', 'Gasolio', '2026-01-01', false) $$,
    '23514', NULL,
    'una voce verificata senza fonte è rifiutata'
);
SELECT throws_ok(
    $$ INSERT INTO impianti.tipi_impianto (codice, nome, valido_dal, valido_al)
       VALUES ('prova_date', 'Date al contrario', '2026-06-01', '2026-01-01') $$,
    '23514', NULL,
    'la validità non può finire prima di iniziare'
);

-- ---------------------------------------------------------------------------
-- Vincoli degli impianti
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id)
       SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000d2', tipo_impianto_id
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    '23503', NULL,
    'un impianto non può stare nella sede di un''altra azienda'
);
SELECT throws_ok(
    $$ UPDATE impianti.impianti SET proprietario_cliente_id = '01900000-0000-7000-8000-0000000000c2'
       WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1' $$,
    '23503', NULL,
    'il proprietario non può essere il cliente di un''altra azienda'
);
SELECT throws_ok(
    $$ UPDATE impianti.impianti SET stato = 'dismesso'
       WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1' $$,
    '23514', NULL,
    'un impianto dismesso ha sempre la data di dismissione'
);
SELECT throws_ok(
    $$ UPDATE impianti.impianti SET potenza_utile_kw = 0
       WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1' $$,
    '23514', NULL,
    'la potenza utile deve essere positiva'
);
SELECT throws_ok(
    $$ UPDATE impianti.impianti SET pdr = 'PDR CON SPAZI'
       WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1' $$,
    '23514', NULL,
    'il PDR non contiene spazi'
);

SELECT throws_ok(
    $$ INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, pdr)
       SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000d1', tipo_impianto_id, 'PDR-PROVA-0001'
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    '23505', NULL,
    'lo stesso PDR non può avere due impianti attivi nella stessa azienda'
);
SELECT lives_ok(
    $$ INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, pdr, stato, dismesso_il)
       SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000d1', tipo_impianto_id, 'PDR-PROVA-0001',
              'dismesso', '2025-12-31'
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    'un impianto dismesso può avere lo stesso PDR di quello che lo ha sostituito'
);
SELECT lives_ok(
    $$ INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, pdr)
       SELECT '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000d2', tipo_impianto_id, 'PDR-PROVA-0001'
       FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento' $$,
    'un''altra azienda può registrare lo stesso PDR'
);

-- ---------------------------------------------------------------------------
-- Riferimenti al catasto e generatori
-- ---------------------------------------------------------------------------

INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice) VALUES
    ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000e1', 'toscana', 'siert', 'CAT-PROVA-1');

SELECT throws_ok(
    $$ INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000e1', 'lombardia', 'siert', 'CAT-PROVA-2') $$,
    '23514', NULL,
    'in Lombardia il sistema è CURIT, non SIERT'
);
SELECT throws_ok(
    $$ INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000e1', 'toscana', 'siert', 'CAT-PROVA-3') $$,
    '23505', NULL,
    'un impianto ha un solo codice per regione'
);
SELECT lives_ok(
    $$ INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice)
       VALUES ('01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000e2', 'toscana', 'siert', 'CAT-PROVA-1') $$,
    'lo stesso codice catasto può comparire in un''altra azienda'
);

INSERT INTO impianti.generatori (azienda_id, impianto_id, tipo_generatore_id, marca, modello, matricola, potenza_kw, installato_il)
SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000e1', tipo_generatore_id,
       'Marca Prova', 'Modello 24', 'MAT-0001', 24.00, '2020-10-01'
FROM impianti.tipi_generatore WHERE codice = 'prova_caldaia';

SELECT lives_ok(
    $$ INSERT INTO impianti.generatori (azienda_id, impianto_id, tipo_generatore_id, matricola)
       SELECT '01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-0000000000e1', tipo_generatore_id, 'MAT-0002'
       FROM impianti.tipi_generatore WHERE codice = 'prova_pompa_di_calore' $$,
    'un impianto può avere più generatori'
);
SELECT throws_ok(
    $$ UPDATE impianti.generatori SET dismesso_il = '2019-01-01' WHERE matricola = 'MAT-0001' $$,
    '23514', NULL,
    'un generatore non si dismette prima di essere installato'
);
SELECT throws_ok(
    $$ INSERT INTO impianti.generatori (azienda_id, impianto_id, tipo_generatore_id)
       SELECT '01900000-0000-7000-8000-00000000000b', '01900000-0000-7000-8000-0000000000e1', tipo_generatore_id
       FROM impianti.tipi_generatore WHERE codice = 'prova_caldaia' $$,
    '23503', NULL,
    'un generatore non può collegarsi all''impianto di un''altra azienda'
);

-- ---------------------------------------------------------------------------
-- Metadati e Row-Level Security (come app_api)
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);

SELECT is((SELECT count(*) FROM impianti.impianti), 2::bigint, 'Alfa vede solo i propri impianti');
SELECT is((SELECT count(*) FROM impianti.riferimenti_catasto), 1::bigint, 'Alfa vede solo i propri riferimenti al catasto');
SELECT is((SELECT count(*) FROM impianti.tipi_impianto), 3::bigint, 'le lookup sono comuni a tutte le aziende');

UPDATE impianti.impianti SET note = 'Caldaia in cantina' WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1';

SELECT is(
    (SELECT versione FROM impianti.impianti WHERE impianto_id = '01900000-0000-7000-8000-0000000000e1'),
    2,
    'ogni modifica incrementa la versione'
);
SELECT is(
    (SELECT count(*) FROM impianti.impianti WHERE impianto_id = '01900000-0000-7000-8000-0000000000e2'),
    0::bigint,
    'l''impianto di un''altra azienda non si vede'
);
SELECT throws_ok(
    $$ DELETE FROM impianti.impianti $$,
    '42501', NULL,
    'app_api non cancella gli impianti: li dismette o li elimina logicamente'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

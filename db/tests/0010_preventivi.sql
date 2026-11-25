-- Listino e preventivi: codici unici, storico dei prezzi, totali dalle righe,
-- numerazione all'emissione senza buchi, blocco dopo l'emissione, passaggi di
-- stato, conversione in intervento, Row-Level Security.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(42);

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende, clienti, sedi e impianti
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01930000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01930000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01930000-0000-7000-8000-0000000000c1', '01930000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri'),
    ('01930000-0000-7000-8000-0000000000c2', '01930000-0000-7000-8000-00000000000a', 'privato', 'Paolo', 'Verdi'),
    ('01930000-0000-7000-8000-0000000000c3', '01930000-0000-7000-8000-00000000000b', 'privato', 'Luca', 'Gialli');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, cap, comune, provincia) VALUES
    ('01930000-0000-7000-8000-0000000000d1', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c1', 'Via dei Platani 12', '50100', 'Firenze', 'FI'),
    ('01930000-0000-7000-8000-0000000000d2', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c2', 'Via dei Tigli 3', '50100', 'Firenze', 'FI'),
    ('01930000-0000-7000-8000-0000000000d3', '01930000-0000-7000-8000-00000000000b', '01930000-0000-7000-8000-0000000000c3', 'Via dei Pini 5', '56100', 'Pisa', 'PI');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT v.impianto, v.azienda, v.sede, t.tipo_impianto_id
FROM (VALUES
    ('01930000-0000-7000-8000-0000000000e1'::uuid, '01930000-0000-7000-8000-00000000000a'::uuid, '01930000-0000-7000-8000-0000000000d1'::uuid),
    ('01930000-0000-7000-8000-0000000000e2'::uuid, '01930000-0000-7000-8000-00000000000a'::uuid, '01930000-0000-7000-8000-0000000000d2'::uuid),
    ('01930000-0000-7000-8000-0000000000e3'::uuid, '01930000-0000-7000-8000-00000000000b'::uuid, '01930000-0000-7000-8000-0000000000d3'::uuid)
) AS v (impianto, azienda, sede)
CROSS JOIN impianti.tipi_impianto t
WHERE t.codice = 'prova_climatizzazione';

-- ---------------------------------------------------------------------------
-- Struttura
-- ---------------------------------------------------------------------------

SELECT has_table('commerciale', 'listino_voci', 'esiste commerciale.listino_voci');
SELECT has_table('commerciale', 'listino_prezzi', 'esiste commerciale.listino_prezzi');
SELECT has_table('commerciale', 'preventivi', 'esiste commerciale.preventivi');
SELECT has_table('commerciale', 'preventivi_righe', 'esiste commerciale.preventivi_righe');
SELECT has_function('commerciale', 'emetti_preventivo', ARRAY['uuid', 'integer'], 'esiste commerciale.emetti_preventivo');
SELECT has_function('commerciale', 'converti_preventivo', ARRAY['uuid', 'integer', 'text'], 'esiste commerciale.converti_preventivo');

-- ---------------------------------------------------------------------------
-- Listino
-- ---------------------------------------------------------------------------

INSERT INTO commerciale.listino_voci (voce_id, azienda_id, codice, descrizione, tipo, unita, aliquota_iva) VALUES
    ('01930000-0000-7000-8000-000000000a01', '01930000-0000-7000-8000-00000000000a', 'MAN-01', 'Manodopera tecnico (prova)', 'manodopera', 'h', 22),
    ('01930000-0000-7000-8000-000000000a02', '01930000-0000-7000-8000-00000000000a', 'FIL-01', 'Filtro di prova', 'materiale', 'pz', 22);

SELECT throws_ok(
    $$ INSERT INTO commerciale.listino_voci (azienda_id, codice, descrizione, tipo, unita)
       VALUES ('01930000-0000-7000-8000-00000000000a', 'man-01', 'Doppione', 'materiale', 'pz') $$,
    '23505', NULL,
    'il codice di una voce è unico per azienda, senza badare alle maiuscole'
);
SELECT lives_ok(
    $$ INSERT INTO commerciale.listino_voci (azienda_id, codice, descrizione, tipo, unita)
       VALUES ('01930000-0000-7000-8000-00000000000b', 'MAN-01', 'Stesso codice, altra azienda', 'manodopera', 'h') $$,
    'due aziende possono usare lo stesso codice'
);
SELECT throws_ok(
    $$ INSERT INTO commerciale.listino_voci (azienda_id, codice, descrizione, tipo, unita)
       VALUES ('01930000-0000-7000-8000-00000000000a', 'MAN-02', 'Manodopera a pezzi', 'manodopera', 'pz') $$,
    '23514', NULL,
    'la manodopera del listino si conta in ore'
);

INSERT INTO commerciale.listino_prezzi (azienda_id, voce_id, prezzo, valido_dal) VALUES
    ('01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-000000000a01', 40.00, '2026-01-01'),
    ('01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-000000000a01', 45.00, '2027-01-01');

SELECT is(commerciale.prezzo_voce('01930000-0000-7000-8000-000000000a01', '2026-06-30'), 40.00, 'vale il prezzo in vigore alla data');
SELECT is(commerciale.prezzo_voce('01930000-0000-7000-8000-000000000a01', '2027-01-01'), 45.00, 'dal giorno del cambio vale il prezzo nuovo');
SELECT is(commerciale.prezzo_voce('01930000-0000-7000-8000-000000000a01', '2025-12-31'), NULL, 'prima del primo prezzo la voce non ha prezzo');
SELECT throws_ok(
    $$ INSERT INTO commerciale.listino_prezzi (azienda_id, voce_id, prezzo, valido_dal)
       VALUES ('01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-000000000a01', 50.00, '2027-01-01') $$,
    '23505', NULL,
    'una voce ha un solo prezzo per data di validità'
);

-- ---------------------------------------------------------------------------
-- Bozza e righe
-- ---------------------------------------------------------------------------

INSERT INTO commerciale.preventivi (preventivo_id, azienda_id, cliente_id, sede_id, impianto_id, oggetto) VALUES
    ('01930000-0000-7000-8000-000000000b01', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c1',
     '01930000-0000-7000-8000-0000000000d1', '01930000-0000-7000-8000-0000000000e1', 'Sostituzione del filtro (prova)');

SELECT throws_ok(
    $$ INSERT INTO commerciale.preventivi (azienda_id, cliente_id, impianto_id, oggetto)
       VALUES ('01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c1', '01930000-0000-7000-8000-0000000000e2', 'Impianto sbagliato') $$,
    '23514', NULL,
    'l''impianto deve essere del cliente del preventivo'
);
SELECT throws_ok(
    $$ INSERT INTO commerciale.preventivi (azienda_id, cliente_id, stato, anno, numero, emesso_il, oggetto)
       VALUES ('01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c1', 'emesso', 2026, 99, '2026-11-25', 'Già emesso') $$,
    '23514', NULL,
    'un preventivo nasce in bozza, senza numero'
);

SELECT is(
    commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b01', 99, '[]'),
    false,
    'le righe con una versione superata non si salvano'
);
SELECT ok(
    commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b01',
        (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01'),
        '[{"voce_id": "01930000-0000-7000-8000-000000000a01", "tipo": "manodopera", "codice": "MAN-01", "descrizione": "Manodopera tecnico (prova)", "unita": "h", "quantita": 1.5, "prezzo_unitario": 45, "aliquota_iva": 22},
          {"voce_id": "01930000-0000-7000-8000-000000000a02", "tipo": "materiale", "codice": "FIL-01", "descrizione": "Filtro di prova", "unita": "pz", "quantita": 2, "prezzo_unitario": 12.50, "sconto_percentuale": 10, "aliquota_iva": 10}]'),
    'le righe di una bozza si salvano'
);
SELECT results_eq(
    $$ SELECT imponibile, iva, totale FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    $$ VALUES (90.00::numeric(12,2), 17.10::numeric(12,2), 107.10::numeric(12,2)) $$,
    'i totali si ricalcolano dalle righe, con l''IVA per aliquota'
);

-- Due righe da 0,15 al 10%: riga per riga sarebbero 0,02 + 0,02; per aliquota è 0,03.
INSERT INTO commerciale.preventivi (preventivo_id, azienda_id, cliente_id, oggetto) VALUES
    ('01930000-0000-7000-8000-000000000b05', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c2', 'Arrotondamenti (prova)');
SELECT commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b05', 1,
    '[{"tipo": "materiale", "descrizione": "Minuteria A", "unita": "pz", "quantita": 1, "prezzo_unitario": 0.15, "aliquota_iva": 10},
      {"tipo": "materiale", "descrizione": "Minuteria B", "unita": "pz", "quantita": 1, "prezzo_unitario": 0.15, "aliquota_iva": 10}]');
SELECT is(
    (SELECT iva FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b05'),
    0.03::numeric(12,2),
    'l''IVA si arrotonda sulla somma di ogni aliquota, non riga per riga'
);

-- ---------------------------------------------------------------------------
-- Emissione e numerazione
-- ---------------------------------------------------------------------------

INSERT INTO commerciale.preventivi (preventivo_id, azienda_id, cliente_id, oggetto) VALUES
    ('01930000-0000-7000-8000-000000000b02', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c2', 'Bozza vuota (prova)'),
    ('01930000-0000-7000-8000-000000000b03', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c2', 'Bozza da eliminare (prova)'),
    ('01930000-0000-7000-8000-000000000b04', '01930000-0000-7000-8000-00000000000a', '01930000-0000-7000-8000-0000000000c2', 'Preventivo da rifiutare (prova)');

SELECT throws_ok(
    $$ SELECT commerciale.emetti_preventivo('01930000-0000-7000-8000-000000000b02', 1) $$,
    '23514', NULL,
    'un preventivo senza righe non si emette'
);
SELECT lives_ok(
    $$ DELETE FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b03' $$,
    'una bozza si elimina'
);

SELECT ok(
    commerciale.emetti_preventivo('01930000-0000-7000-8000-000000000b01',
        (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01')),
    'la bozza si emette'
);
SELECT results_eq(
    $$ SELECT stato, numero, anno, emesso_il, scade_il - emesso_il FROM commerciale.preventivi
       WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    $$ VALUES ('emesso'::text, 1, extract(year FROM (now() AT TIME ZONE 'Europe/Rome'))::integer,
               (now() AT TIME ZONE 'Europe/Rome')::date, 30) $$,
    'all''emissione arrivano numero, anno, data e scadenza della validità'
);

SELECT commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b04', 1,
    '[{"tipo": "materiale", "descrizione": "Ricambio di prova", "unita": "pz", "quantita": 1, "prezzo_unitario": 100, "aliquota_iva": 22}]');
SELECT commerciale.emetti_preventivo('01930000-0000-7000-8000-000000000b04',
    (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b04'));

SELECT is(
    (SELECT numero FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b04'),
    2,
    'le bozze eliminate o non emesse non consumano numeri'
);
SELECT throws_ok(
    $$ DELETE FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    '55000', NULL,
    'un preventivo emesso non si elimina: si annulla'
);
SELECT throws_ok(
    $$ UPDATE commerciale.preventivi SET oggetto = 'Ripensamento' WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    '55000', NULL,
    'un preventivo emesso non cambia contenuto'
);
SELECT throws_ok(
    $$ SELECT commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b01',
           (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01'), '[]') $$,
    '55000', NULL,
    'le righe di un preventivo emesso non si sostituiscono'
);
SELECT throws_ok(
    $$ DELETE FROM commerciale.preventivi_righe WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    '55000', NULL,
    'le righe di un preventivo emesso non si cancellano nemmeno direttamente'
);
SELECT throws_ok(
    $$ UPDATE commerciale.preventivi SET stato = 'bozza' WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    '55000', NULL,
    'un preventivo emesso non torna in bozza'
);

-- ---------------------------------------------------------------------------
-- Rifiuto, accettazione e conversione
-- ---------------------------------------------------------------------------

UPDATE commerciale.preventivi SET stato = 'rifiutato', rifiutato_il = now()
WHERE preventivo_id = '01930000-0000-7000-8000-000000000b04';

SELECT throws_ok(
    $$ UPDATE commerciale.preventivi SET stato = 'accettato', accettato_il = now()
       WHERE preventivo_id = '01930000-0000-7000-8000-000000000b04' $$,
    '55000', NULL,
    'un preventivo rifiutato è finale'
);

SELECT throws_ok(
    $$ SELECT commerciale.converti_preventivo('01930000-0000-7000-8000-000000000b01',
           (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01'), 'riparazione') $$,
    '55000', NULL,
    'l''intervento nasce solo da un preventivo accettato'
);

UPDATE commerciale.preventivi SET stato = 'accettato', accettato_il = now()
WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01';

SELECT is(
    commerciale.converti_preventivo('01930000-0000-7000-8000-000000000b01', 1, 'riparazione'),
    NULL,
    'la conversione con una versione superata non fa nulla'
);
SELECT isnt(
    commerciale.converti_preventivo('01930000-0000-7000-8000-000000000b01',
        (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01'), 'riparazione'),
    NULL,
    'il preventivo accettato genera l''intervento'
);
SELECT results_eq(
    $$ SELECT i.stato, i.tipo, i.impianto_id, i.descrizione
       FROM commerciale.preventivi p JOIN lavoro.interventi i ON i.intervento_id = p.intervento_id
       WHERE p.preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    $$ VALUES ('da_pianificare'::text, 'riparazione'::text, '01930000-0000-7000-8000-0000000000e1'::uuid,
               format('Dal preventivo 1/%s: Sostituzione del filtro (prova)', extract(year FROM (now() AT TIME ZONE 'Europe/Rome'))::integer)) $$,
    'l''intervento è da pianificare, sull''impianto del preventivo, e ne ricorda il numero'
);
SELECT results_eq(
    $$ SELECT r.posizione, r.codice, r.quantita, r.prezzo_unitario, r.sconto_percentuale, r.importo
       FROM commerciale.preventivi p JOIN lavoro.interventi_righe r ON r.intervento_id = p.intervento_id
       WHERE p.preventivo_id = '01930000-0000-7000-8000-000000000b01' ORDER BY r.posizione $$,
    $$ VALUES (1, 'MAN-01'::text, 1.5::numeric(10,3), 45.00::numeric(12,2), 0.00::numeric(5,2), 67.50::numeric(12,2)),
              (2, 'FIL-01'::text, 2::numeric(10,3), 12.50::numeric(12,2), 10.00::numeric(5,2), 22.50::numeric(12,2)) $$,
    'le righe passano all''intervento con i prezzi del preventivo'
);
SELECT throws_ok(
    $$ SELECT commerciale.converti_preventivo('01930000-0000-7000-8000-000000000b01',
           (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01'), 'riparazione') $$,
    '55000', NULL,
    'un preventivo genera un solo intervento'
);
SELECT throws_ok(
    $$ UPDATE commerciale.preventivi SET stato = 'annullato', annullato_il = now(), motivo_annullamento = 'Ripensamento'
       WHERE preventivo_id = '01930000-0000-7000-8000-000000000b01' $$,
    '55000', NULL,
    'un preventivo che ha generato l''intervento non si annulla'
);

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi di app_api
-- ---------------------------------------------------------------------------

INSERT INTO commerciale.preventivi (azienda_id, cliente_id, oggetto) VALUES
    ('01930000-0000-7000-8000-00000000000b', '01930000-0000-7000-8000-0000000000c3', 'Preventivo dell''altra azienda (prova)');

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01930000-0000-7000-8000-00000000000a', true);

SELECT is(
    (SELECT count(*)::integer FROM commerciale.preventivi WHERE azienda_id = '01930000-0000-7000-8000-00000000000b'),
    0,
    'app_api non vede i preventivi di un''altra azienda'
);
SELECT is(
    (SELECT count(*)::integer FROM commerciale.listino_voci WHERE azienda_id = '01930000-0000-7000-8000-00000000000b'),
    0,
    'app_api non vede il listino di un''altra azienda'
);
SELECT lives_ok(
    $$ SELECT commerciale.salva_righe_preventivo('01930000-0000-7000-8000-000000000b02', 1,
           '[{"tipo": "manodopera", "descrizione": "Sopralluogo (prova)", "unita": "h", "quantita": 1, "prezzo_unitario": 40, "aliquota_iva": 22}]');
       SELECT commerciale.emetti_preventivo('01930000-0000-7000-8000-000000000b02',
           (SELECT versione FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b02')) $$,
    'app_api salva le righe ed emette un preventivo'
);
SELECT is(
    (SELECT numero FROM commerciale.preventivi WHERE preventivo_id = '01930000-0000-7000-8000-000000000b02'),
    3,
    'la numerazione continua anche dall''API'
);
SELECT throws_ok(
    $$ UPDATE commerciale.listino_prezzi SET prezzo = 1 $$,
    '42501', NULL,
    'lo storico dei prezzi è in sola aggiunta per app_api'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

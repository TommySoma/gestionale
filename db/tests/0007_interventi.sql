-- Schema lavoro: numerazione senza buchi, vincoli degli interventi, calendario
-- senza sovrapposizioni per tecnico, chiusura con righe, blocco dopo la
-- chiusura e Row-Level Security.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(35);

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende, ciascuna con sede, impianto e tecnici
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01900000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01900000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01900000-0000-7000-8000-0000000000f1', '01900000-0000-7000-8000-00000000000a', 'tecnico1@alfa.example', 'Marco', 'Rossi', '$argon2id$prova'),
    ('01900000-0000-7000-8000-0000000000f2', '01900000-0000-7000-8000-00000000000a', 'tecnico2@alfa.example', 'Sara',  'Bianchi', '$argon2id$prova'),
    ('01900000-0000-7000-8000-0000000000f3', '01900000-0000-7000-8000-00000000000b', 'tecnico@beta.example',  'Luca',  'Gialli', '$argon2id$prova');

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

-- ---------------------------------------------------------------------------
-- Struttura
-- ---------------------------------------------------------------------------

SELECT has_schema('lavoro', 'esiste lo schema lavoro');
SELECT has_table('lavoro', 'interventi', 'esiste lavoro.interventi');
SELECT has_table('lavoro', 'interventi_righe', 'esiste lavoro.interventi_righe');
SELECT has_function('commerciale', 'prossimo_numero', ARRAY['uuid', 'text', 'integer'], 'esiste commerciale.prossimo_numero');

-- ---------------------------------------------------------------------------
-- Numerazione
-- ---------------------------------------------------------------------------

INSERT INTO lavoro.interventi (intervento_id, azienda_id, anno, impianto_id, tipo) VALUES
    ('01900000-0000-7000-8000-000000000101', '01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e1', 'manutenzione'),
    ('01900000-0000-7000-8000-000000000102', '01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e1', 'riparazione'),
    ('01900000-0000-7000-8000-000000000201', '01900000-0000-7000-8000-00000000000b', 2026, '01900000-0000-7000-8000-0000000000e2', 'manutenzione'),
    ('01900000-0000-7000-8000-000000000103', '01900000-0000-7000-8000-00000000000a', 2025, '01900000-0000-7000-8000-0000000000e1', 'sopralluogo');

SELECT results_eq(
    $$ SELECT numero FROM lavoro.interventi
       WHERE azienda_id = '01900000-0000-7000-8000-00000000000a' AND anno = 2026 ORDER BY numero $$,
    ARRAY[1, 2],
    'i numeri partono da 1 e crescono di uno'
);
SELECT is(
    (SELECT numero FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000201'),
    1,
    'ogni azienda ha la propria numerazione'
);
SELECT is(
    (SELECT numero FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000103'),
    1,
    'ogni anno ricomincia da 1'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.interventi (azienda_id, anno, numero, impianto_id, tipo)
       VALUES ('01900000-0000-7000-8000-00000000000a', 2026, 99, '01900000-0000-7000-8000-0000000000e1', 'altro') $$,
    '23514', NULL,
    'il numero non si sceglie: lo assegna il database'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.interventi (azienda_id, anno, impianto_id, tipo)
       VALUES ('01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e2', 'altro') $$,
    '23503', NULL,
    'un intervento non può riguardare l''impianto di un''altra azienda'
);

INSERT INTO lavoro.interventi (intervento_id, azienda_id, anno, impianto_id, tipo) VALUES
    ('01900000-0000-7000-8000-000000000104', '01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e1', 'altro');

SELECT is(
    (SELECT numero FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000104'),
    3,
    'un salvataggio fallito non consuma il numero: niente buchi'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET numero = 50 WHERE intervento_id = '01900000-0000-7000-8000-000000000104' $$,
    '23514', NULL,
    'il numero di un intervento non si modifica'
);

-- ---------------------------------------------------------------------------
-- Pianificazione e calendario
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f1'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '23514', NULL,
    'un intervento pianificato ha sempre una finestra'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET finestra = '[2026-12-01 10:00+01, 2026-12-01 12:00+01)'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '23514', NULL,
    'un intervento da pianificare non ha una finestra'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f1',
              finestra = '(2026-12-01 10:00+01, 2026-12-01 12:00+01]'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '23514', NULL,
    'la finestra include l''inizio ed esclude la fine'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f3',
              finestra = '[2026-12-01 10:00+01, 2026-12-01 12:00+01)'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '23503', NULL,
    'non si assegna l''intervento al tecnico di un''altra azienda'
);

UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f1',
       finestra = '[2026-12-01 10:00+01, 2026-12-01 12:00+01)'
WHERE intervento_id = '01900000-0000-7000-8000-000000000101';

SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f1',
              finestra = '[2026-12-01 11:00+01, 2026-12-01 13:00+01)'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000102' $$,
    '23P01', NULL,
    'un tecnico non ha due interventi sovrapposti'
);
SELECT lives_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f2',
              finestra = '[2026-12-01 11:00+01, 2026-12-01 13:00+01)'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000102' $$,
    'due tecnici diversi lavorano alla stessa ora'
);
SELECT lives_ok(
    $$ UPDATE lavoro.interventi SET stato = 'pianificato', tecnico_utente_id = '01900000-0000-7000-8000-0000000000f1',
              finestra = '[2026-12-01 12:00+01, 2026-12-01 14:00+01)'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000104' $$,
    'un intervento può iniziare quando finisce il precedente'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET stato = 'annullato', annullato_il = now()
       WHERE intervento_id = '01900000-0000-7000-8000-000000000104' $$,
    '23514', NULL,
    'un intervento annullato ha sempre il motivo'
);

UPDATE lavoro.interventi SET stato = 'annullato', annullato_il = now(), motivo_annullamento = 'Il cliente ha rinviato'
WHERE intervento_id = '01900000-0000-7000-8000-000000000104';

SELECT lives_ok(
    $$ INSERT INTO lavoro.interventi (azienda_id, anno, impianto_id, tipo, stato, tecnico_utente_id, finestra)
       VALUES ('01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e1', 'riparazione', 'pianificato',
               '01900000-0000-7000-8000-0000000000f1', '[2026-12-01 12:00+01, 2026-12-01 13:00+01)') $$,
    'un intervento annullato libera il calendario'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET note_chiusura = 'ripensamento'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000104' $$,
    '55000', NULL,
    'un intervento annullato non si modifica'
);

-- ---------------------------------------------------------------------------
-- Chiusura con righe
-- ---------------------------------------------------------------------------

SELECT is(
    lavoro.chiudi_intervento('01900000-0000-7000-8000-000000000101', 1, 'risolto', NULL, '[]'),
    false,
    'la chiusura con una versione superata non fa nulla'
);
SELECT throws_ok(
    $$ SELECT lavoro.chiudi_intervento('01900000-0000-7000-8000-000000000103', 1, 'risolto', NULL, '[]') $$,
    '55000', NULL,
    'non si chiude un intervento ancora da pianificare'
);
SELECT throws_ok(
    $$ SELECT lavoro.chiudi_intervento(
           '01900000-0000-7000-8000-000000000101',
           (SELECT versione FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000101'),
           'risolto', NULL,
           '[{"tipo": "manodopera", "descrizione": "Manutenzione ordinaria", "unita": "pz", "quantita": 1}]') $$,
    '23514', NULL,
    'la manodopera si conta in ore'
);
SELECT is(
    lavoro.chiudi_intervento(
        '01900000-0000-7000-8000-000000000101',
        (SELECT versione FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000101'),
        'risolto', 'Pulizia del bruciatore',
        '[{"tipo": "manodopera", "descrizione": "Manutenzione ordinaria", "unita": "h", "quantita": 1.5, "prezzo_unitario": 40, "aliquota_iva": 22},
          {"tipo": "materiale", "codice": "GUA-01", "descrizione": "Guarnizione di prova", "unita": "pz", "quantita": 2,
           "prezzo_unitario": 4.99, "sconto_percentuale": 10, "aliquota_iva": 22}]'),
    true,
    'la chiusura con la versione giusta riesce'
);
SELECT results_eq(
    $$ SELECT posizione, importo FROM lavoro.interventi_righe
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' ORDER BY posizione $$,
    $$ VALUES (1, 60.00::numeric(12, 2)), (2, 8.98::numeric(12, 2)) $$,
    'le righe seguono l''ordine ricevuto e l''importo tiene conto dello sconto'
);
SELECT is(
    (SELECT stato || '/' || esito FROM lavoro.interventi WHERE intervento_id = '01900000-0000-7000-8000-000000000101'),
    'completato/risolto',
    'l''intervento chiuso è completato con il suo esito'
);
SELECT throws_ok(
    $$ UPDATE lavoro.interventi SET note_chiusura = 'aggiunta dopo'
       WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '55000', NULL,
    'un intervento completato non si modifica'
);
SELECT throws_ok(
    $$ INSERT INTO lavoro.interventi_righe (azienda_id, intervento_id, posizione, tipo, descrizione, unita, quantita)
       VALUES ('01900000-0000-7000-8000-00000000000a', '01900000-0000-7000-8000-000000000101', 3, 'materiale', 'Aggiunta', 'pz', 1) $$,
    '55000', NULL,
    'alle righe di un intervento completato non si aggiunge nulla'
);
SELECT throws_ok(
    $$ DELETE FROM lavoro.interventi_righe WHERE intervento_id = '01900000-0000-7000-8000-000000000101' $$,
    '55000', NULL,
    'le righe di un intervento completato non si cancellano'
);

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi (come app_api)
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01900000-0000-7000-8000-00000000000a', true);

SELECT is((SELECT count(*) FROM lavoro.interventi), 5::bigint, 'Alfa vede solo i propri interventi');
SELECT is((SELECT count(*) FROM commerciale.contatori), 2::bigint, 'Alfa vede solo i propri contatori');
SELECT lives_ok(
    $$ INSERT INTO lavoro.interventi (azienda_id, anno, impianto_id, tipo)
       VALUES ('01900000-0000-7000-8000-00000000000a', 2026, '01900000-0000-7000-8000-0000000000e1', 'manutenzione') $$,
    'app_api crea un intervento e il database lo numera'
);
SELECT throws_ok(
    $$ SELECT commerciale.prossimo_numero('01900000-0000-7000-8000-00000000000b', 'intervento', 2026) $$,
    '42501', NULL,
    'app_api non consuma i numeri di un''altra azienda'
);
SELECT throws_ok(
    $$ DELETE FROM lavoro.interventi $$,
    '42501', NULL,
    'app_api non cancella gli interventi: li annulla'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

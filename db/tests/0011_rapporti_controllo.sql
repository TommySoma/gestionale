-- Rapporti di controllo, bollini, tracciato SIERT e invii: dati di riferimento,
-- validazione contro il tracciato, assegnazione del bollino senza doppio uso,
-- passaggi di stato, immutabilità dopo la firma, invii senza doppioni, audit,
-- Row-Level Security.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(59);

-- ---------------------------------------------------------------------------
-- Dati di prova: due aziende, un cliente, una sede, due impianti con generatore
-- ---------------------------------------------------------------------------

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione, codice_ditta_siert) VALUES
    ('01940000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana', 12345),
    ('01940000-0000-7000-8000-00000000000b', 'Clima Beta Snc (fittizia)',          'toscana', NULL);

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01940000-0000-7000-8000-0000000000c1', '01940000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia, codice_istat_comune) VALUES
    ('01940000-0000-7000-8000-0000000000d1', '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000c1',
     'Via dei Platani', '12', '50100', 'Firenze', 'FI', '048017');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT v.impianto, '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000d1', t.tipo_impianto_id
FROM (VALUES ('01940000-0000-7000-8000-0000000000e1'::uuid), ('01940000-0000-7000-8000-0000000000e2'::uuid)) AS v (impianto)
CROSS JOIN impianti.tipi_impianto t
WHERE t.codice = 'prova_riscaldamento';

INSERT INTO impianti.generatori (generatore_id, azienda_id, impianto_id, tipo_generatore_id, sigla_catasto)
SELECT v.generatore, '01940000-0000-7000-8000-00000000000a', v.impianto, t.tipo_generatore_id, 'GT01'
FROM (VALUES
    ('01940000-0000-7000-8000-0000000000f1'::uuid, '01940000-0000-7000-8000-0000000000e1'::uuid),
    ('01940000-0000-7000-8000-0000000000f2'::uuid, '01940000-0000-7000-8000-0000000000e2'::uuid)
) AS v (generatore, impianto)
CROSS JOIN impianti.tipi_generatore t
WHERE t.codice = 'prova_caldaia';

-- Un RCEE tipo 1 completo e corretto secondo il tracciato v3.0.
CREATE TEMPORARY TABLE dati_validi (d jsonb) ON COMMIT DROP;
INSERT INTO dati_validi VALUES ('{
    "codice_catasto": "0123456789", "potenza_totale": 24.5, "comune": "048017",
    "indirizzo_impianto": "VIA DEI PLATANI, 12", "responsabile_impianto": "NERI GIULIA",
    "codice_fiscale": "NREGLI80A41D612X", "titolo_responsabile": "PROPRIETARIO",
    "dichiarazione_conformita": "3", "libretto_uso_manutenzione": "3", "libretto_climatizzazione": "3",
    "libretto_compilato_correttamente": "3", "durezza_acqua": 25, "trattamento_riscaldamento": "17",
    "trattamento_acs": "16", "locale_idoneo": "16", "generatore_idoneo_installazione_esterna": "3",
    "aperture_ventilazione_libere": "9", "aperture_ventilazione_adeguate": "9", "canale_fumo_idoneo": "3",
    "regolazione_temperatura_funzionante": "3", "assenza_perdite_combustibile": "16",
    "idonea_tenuta_impianto": "3", "data_installazione": "2018-03-15", "fabbricante": "MARCA DI PROVA",
    "modello": "MODELLO DI PROVA", "matricola": "PROVA-0001", "gruppo": "GT01",
    "tipo_generatore": "GRUPPO TERMICO SINGOLO", "potenza_focolare": 25.8, "potenza_utile": 24.5,
    "climatizzazione_invernale": "SI", "produzione_acs": "SI", "combustibile": "Gas naturale",
    "evacuazione_fumi": "Forzata", "dispositivi_comando_funzionanti": "3", "dispositivi_sicurezza_integri": "3",
    "valvola_sicurezza": "3", "pulizia_scambiatore": "3", "presenza_riflussi": "4", "risultati_uni_10389": "3",
    "temperatura_fumi": 68.2, "temperatura_aria_comburente": 18.5, "o2": 3.1, "co2": 9.8, "co": 12,
    "rendimento_combustione": 97.4, "rendimento_minimo": 90.4, "funziona_in_sicurezza": "21",
    "orario_arrivo": "09:15", "orario_partenza": "10:05", "manutentore": "ROSSI MARIO"
}');

CREATE TEMPORARY TABLE oggi (d date) ON COMMIT DROP;
INSERT INTO oggi VALUES ((now() AT TIME ZONE 'Europe/Rome')::date);

-- ---------------------------------------------------------------------------
-- Struttura e dati di riferimento
-- ---------------------------------------------------------------------------

SELECT has_table('norma', 'rapporti_controllo', 'esiste norma.rapporti_controllo');
SELECT has_table('norma', 'bollini', 'esiste norma.bollini');
SELECT has_table('norma', 'invii_catasto', 'esiste norma.invii_catasto');
SELECT has_function('norma', 'valida_rapporto', ARRAY['uuid'], 'esiste norma.valida_rapporto');
SELECT has_function('norma', 'assegna_bollino', ARRAY['uuid', 'integer', 'uuid'], 'esiste norma.assegna_bollino');

SELECT is(
    (SELECT count(*)::integer FROM norma.tracciati_campi t JOIN norma.modelli_rapporto m USING (modello_rapporto_id) WHERE m.codice = 'tipo_1' AND t.versione = '3.0'),
    67, 'il tracciato v3.0 del tipo 1 ha 67 colonne'
);
SELECT is(
    (SELECT count(*)::integer FROM norma.tracciati_campi t JOIN norma.modelli_rapporto m USING (modello_rapporto_id) WHERE m.codice = 'tipo_2' AND t.versione = '3.0'),
    67, 'il tracciato v3.0 del tipo 2 ha 67 colonne'
);
SELECT is(
    (SELECT count(*)::integer FROM (
        SELECT modello_rapporto_id FROM norma.tracciati_campi
        GROUP BY modello_rapporto_id, versione
        HAVING min(posizione) <> 1 OR max(posizione) <> count(*)
    ) s),
    0, 'le posizioni delle colonne vanno da 1 senza buchi'
);
SELECT is(
    (SELECT count(*)::integer FROM norma.tracciati_campi WHERE numero_campo = 46),
    0, 'il campo 46, assente nella guida, non è nel tracciato'
);
SELECT is(
    (SELECT count(*)::integer FROM norma.tipi_bollino WHERE regione = 'toscana' AND NOT da_verificare),
    15, 'quindici tipi di bollino toscani verificati'
);
SELECT is(
    (SELECT giorni FROM norma.regole_trasmissione WHERE regione = 'toscana' AND valido_al IS NULL),
    60, 'in Toscana il RCEE si trasmette entro 60 giorni'
);

-- ---------------------------------------------------------------------------
-- Bollini
-- ---------------------------------------------------------------------------

INSERT INTO norma.bollini (bollino_id, azienda_id, tipo_bollino_id, codice, acquistato_il)
SELECT v.bollino, '01940000-0000-7000-8000-00000000000a', t.tipo_bollino_id, v.codice, v.acquisto
FROM (VALUES
    ('01940000-0000-7000-8000-000000000b01'::uuid, 'gt_fino_35', '2026000000002', '2026-02-01'::date),
    ('01940000-0000-7000-8000-000000000b02'::uuid, 'gt_fino_35', '2026000000001', '2026-01-10'::date),
    ('01940000-0000-7000-8000-000000000b03'::uuid, 'gf_fino_35', '2026000000003', '2026-01-10'::date)
) AS v (bollino, tipo, codice, acquisto)
JOIN norma.tipi_bollino t ON t.codice = v.tipo;

SELECT throws_ok(
    $$ INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il)
       SELECT '01940000-0000-7000-8000-00000000000a', tipo_bollino_id, '202600000001', '2026-01-10'
       FROM norma.tipi_bollino WHERE codice = 'gt_fino_35' $$,
    '23514', NULL,
    'il codice del bollino ha 13 cifre'
);
SELECT throws_ok(
    $$ INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il)
       SELECT '01940000-0000-7000-8000-00000000000a', tipo_bollino_id, '2026000000001', '2026-01-10'
       FROM norma.tipi_bollino WHERE codice = 'gt_fino_35' $$,
    '23505', NULL,
    'lo stesso bollino non si registra due volte'
);
SELECT lives_ok(
    $$ INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il)
       SELECT '01940000-0000-7000-8000-00000000000b', tipo_bollino_id, '2026000000001', '2026-01-10'
       FROM norma.tipi_bollino WHERE codice = 'gt_fino_35' $$,
    'il vincolo sul codice è per azienda'
);
SELECT throws_ok(
    $$ INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il, stato)
       SELECT '01940000-0000-7000-8000-00000000000a', tipo_bollino_id, '2026000000009', '2026-01-10', 'usato'
       FROM norma.tipi_bollino WHERE codice = 'gt_fino_35' $$,
    '23514', NULL,
    'un bollino si registra disponibile'
);
SELECT throws_ok(
    $$ UPDATE norma.bollini SET stato = 'assegnato' WHERE bollino_id = '01940000-0000-7000-8000-000000000b01' $$,
    '23514', NULL,
    'un bollino non si assegna senza il rapporto'
);

-- ---------------------------------------------------------------------------
-- Creazione del rapporto
-- ---------------------------------------------------------------------------

INSERT INTO norma.rapporti_controllo (rapporto_controllo_id, azienda_id, impianto_id, generatore_id, modello_rapporto_id, data_controllo, dati)
SELECT v.rapporto, '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000e1', '01940000-0000-7000-8000-0000000000f1',
       m.modello_rapporto_id, (SELECT d FROM oggi) - v.giorni_fa, v.dati
FROM (VALUES
    ('01940000-0000-7000-8000-000000000a01'::uuid, 5,  (SELECT d FROM dati_validi)),
    ('01940000-0000-7000-8000-000000000a02'::uuid, 5,  '{}'::jsonb),
    ('01940000-0000-7000-8000-000000000a03'::uuid, 5,  (SELECT d FROM dati_validi)),
    ('01940000-0000-7000-8000-000000000a04'::uuid, 5,  (SELECT d FROM dati_validi)),
    ('01940000-0000-7000-8000-000000000a05'::uuid, 61, (SELECT d FROM dati_validi))
) AS v (rapporto, giorni_fa, dati)
CROSS JOIN norma.modelli_rapporto m
WHERE m.codice = 'tipo_1';

SELECT throws_ok(
    $$ INSERT INTO norma.rapporti_controllo (azienda_id, impianto_id, generatore_id, modello_rapporto_id, data_controllo)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000e1', '01940000-0000-7000-8000-0000000000f2',
              modello_rapporto_id, '2026-11-01' FROM norma.modelli_rapporto WHERE codice = 'tipo_1' $$,
    '23514', NULL,
    'il generatore deve essere dell''impianto del rapporto'
);
SELECT throws_ok(
    $$ INSERT INTO norma.rapporti_controllo (azienda_id, impianto_id, generatore_id, modello_rapporto_id, data_controllo)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000e1', '01940000-0000-7000-8000-0000000000f1',
              modello_rapporto_id, (now() AT TIME ZONE 'Europe/Rome')::date + 1 FROM norma.modelli_rapporto WHERE codice = 'tipo_1' $$,
    '23514', NULL,
    'la data del controllo non è futura'
);
SELECT throws_ok(
    $$ INSERT INTO norma.rapporti_controllo (azienda_id, impianto_id, generatore_id, modello_rapporto_id, data_controllo, stato, firmato_il)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-0000000000e1', '01940000-0000-7000-8000-0000000000f1',
              modello_rapporto_id, '2026-11-01', 'firmato', now() FROM norma.modelli_rapporto WHERE codice = 'tipo_1' $$,
    '23514', NULL,
    'un rapporto nasce in bozza'
);
SELECT is(
    (SELECT termine_trasmissione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01'),
    (SELECT d FROM oggi) - 5 + 60,
    'il termine di trasmissione è la data del controllo più 60 giorni'
);

-- ---------------------------------------------------------------------------
-- Validazione contro il tracciato
-- ---------------------------------------------------------------------------

SELECT ok(
    (SELECT count(*) FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a02') WHERE bloccante) > 40,
    'un rapporto vuoto ha un errore per ogni campo obbligatorio'
);
SELECT is(
    (SELECT array_agg(campo || ': ' || messaggio) FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a01') WHERE bloccante),
    ARRAY['NUMERO_BOLLINO: nessun bollino assegnato'],
    'a un rapporto completo manca solo il bollino'
);

-- ---------------------------------------------------------------------------
-- Assegnazione del bollino
-- ---------------------------------------------------------------------------

SELECT is(
    norma.assegna_bollino('01940000-0000-7000-8000-000000000a01', 99,
        (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35')),
    NULL,
    'con una versione superata il bollino non si assegna'
);
SELECT throws_ok(
    $$ SELECT norma.assegna_bollino('01940000-0000-7000-8000-000000000a01',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01'),
           (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gf_fino_35')) $$,
    '23514', NULL,
    'un bollino GF non va su un RCEE tipo 1'
);
SELECT is(
    norma.assegna_bollino('01940000-0000-7000-8000-000000000a01',
        (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01'),
        (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35')),
    '01940000-0000-7000-8000-000000000b02'::uuid,
    'si assegna il bollino disponibile acquistato per primo'
);
SELECT is(
    (SELECT stato FROM norma.bollini WHERE bollino_id = '01940000-0000-7000-8000-000000000b02'),
    'assegnato', 'il bollino diventa assegnato'
);
SELECT is(
    norma.errori_bloccanti('01940000-0000-7000-8000-000000000a01'),
    NULL,
    'con il bollino il rapporto non ha errori bloccanti'
);
SELECT throws_ok(
    $$ SELECT norma.assegna_bollino('01940000-0000-7000-8000-000000000a01',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01'),
           (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35')) $$,
    '55000', NULL,
    'un rapporto con il bollino non ne riceve un secondo'
);
SELECT is(
    norma.assegna_bollino('01940000-0000-7000-8000-000000000a03',
        (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a03'),
        (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35')),
    '01940000-0000-7000-8000-000000000b01'::uuid,
    'un altro rapporto riceve il bollino successivo, mai lo stesso'
);
SELECT throws_ok(
    $$ SELECT norma.assegna_bollino('01940000-0000-7000-8000-000000000a04',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a04'),
           (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35')) $$,
    'P0002', NULL,
    'finiti i bollini del tipo, l''assegnazione fallisce'
);

-- ---------------------------------------------------------------------------
-- Errori e avvisi sui singoli campi
-- ---------------------------------------------------------------------------

UPDATE norma.rapporti_controllo SET dati = dati || '{"potenza_totale": 40}'
WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a03';

SELECT is(
    (SELECT array_agg(campo || '/' || bloccante::text) FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a03')),
    ARRAY['NUMERO_BOLLINO/false'],
    'una potenza fuori dalla fascia del bollino è un avviso, non un errore'
);

UPDATE norma.rapporti_controllo
SET dati = dati || '{"combustibile": "GPL", "evacuazione_fumi": "Naturale", "titolo_responsabile": "INQUILINO", "data_installazione": "2018-02-30", "osservazioni": "riga\tcon tabulazione"}'
WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a04';

SELECT ok(
    EXISTS (SELECT 1 FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a04') WHERE campo = 'DEPRESSIONE' AND bloccante),
    'con GPL e tiraggio naturale la depressione è obbligatoria'
);
SELECT ok(
    EXISTS (SELECT 1 FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a04') WHERE campo = 'TITOLO_RESPONSABILE' AND bloccante),
    'un valore fuori dall''elenco ammesso è un errore'
);
SELECT ok(
    EXISTS (SELECT 1 FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a04') WHERE campo = 'DATA_INSTALLAZIONE' AND bloccante),
    'una data inesistente è un errore'
);
SELECT ok(
    EXISTS (SELECT 1 FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a04') WHERE campo = 'OSSERVAZIONI' AND bloccante),
    'una tabulazione nel testo romperebbe il file ed è un errore'
);
SELECT ok(
    EXISTS (SELECT 1 FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a05') WHERE campo = 'DATA_CONTROLLO' AND bloccante),
    'oltre i 60 giorni il termine è superato ed è un errore'
);

-- ---------------------------------------------------------------------------
-- Convalida, firma e immutabilità
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ SELECT norma.convalida_rapporto('01940000-0000-7000-8000-000000000a02',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a02')) $$,
    '23514', NULL,
    'un rapporto con errori bloccanti non si convalida'
);
SELECT ok(
    norma.convalida_rapporto('01940000-0000-7000-8000-000000000a01',
        (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01')),
    'un rapporto senza errori si convalida'
);
SELECT throws_ok(
    $$ UPDATE norma.rapporti_controllo SET dati = dati || '{"co": 50}' WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01' $$,
    '55000', NULL,
    'un rapporto convalidato non si modifica'
);
SELECT ok(
    norma.firma_rapporto('01940000-0000-7000-8000-000000000a01',
        (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01')),
    'un rapporto convalidato si firma'
);
SELECT is(
    (SELECT stato FROM norma.bollini WHERE bollino_id = '01940000-0000-7000-8000-000000000b02'),
    'usato', 'con la firma il bollino è usato'
);
SELECT throws_ok(
    $$ UPDATE norma.rapporti_controllo SET data_controllo = data_controllo - 1 WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01' $$,
    '55000', NULL,
    'dopo la firma i dati non cambiano'
);
SELECT throws_ok(
    $$ SELECT norma.libera_bollino('01940000-0000-7000-8000-000000000a01',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01')) $$,
    '55000', NULL,
    'dopo la firma il bollino non si libera'
);
SELECT throws_ok(
    $$ UPDATE norma.bollini SET stato = 'disponibile' WHERE bollino_id = '01940000-0000-7000-8000-000000000b02' $$,
    '55000', NULL,
    'un bollino usato non torna disponibile'
);

-- ---------------------------------------------------------------------------
-- Annullamento
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ SELECT norma.annulla_rapporto('01940000-0000-7000-8000-000000000a05',
           (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a05'), '  ') $$,
    '23514', NULL,
    'un rapporto si annulla solo con un motivo'
);
SELECT ok(
    norma.annulla_rapporto('01940000-0000-7000-8000-000000000a03',
        (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a03'), 'Controllo da ripetere'),
    'un rapporto in bozza si annulla'
);
SELECT is(
    (SELECT stato FROM norma.bollini WHERE bollino_id = '01940000-0000-7000-8000-000000000b01'),
    'disponibile', 'annullata la bozza, il suo bollino torna disponibile'
);
SELECT throws_ok(
    $$ UPDATE norma.rapporti_controllo SET stato = 'bozza' WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a03' $$,
    '55000', NULL,
    'un rapporto annullato resta annullato'
);

-- ---------------------------------------------------------------------------
-- Invii al catasto
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$ INSERT INTO norma.invii_catasto (azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato, nome_file, contenuto, impronta_sha256)
       SELECT '01940000-0000-7000-8000-00000000000a', 'toscana', 'siert', 'import_massivo_rcee', modello_rapporto_id, '3.0',
              'rcee_tipo_1.txt', 'contenuto di prova', sha256('altro'::bytea)
       FROM norma.modelli_rapporto WHERE codice = 'tipo_1' $$,
    '23514', NULL,
    'l''impronta deve corrispondere al contenuto'
);

INSERT INTO norma.invii_catasto (invio_catasto_id, azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato, nome_file, contenuto, impronta_sha256)
SELECT v.invio, '01940000-0000-7000-8000-00000000000a', 'toscana', 'siert', 'import_massivo_rcee', m.modello_rapporto_id, '3.0',
       'rcee_tipo_1.txt', 'contenuto di prova', sha256(convert_to('contenuto di prova', 'UTF8'))
FROM (VALUES ('01940000-0000-7000-8000-000000000c01'::uuid), ('01940000-0000-7000-8000-000000000c02'::uuid)) AS v (invio)
CROSS JOIN norma.modelli_rapporto m
WHERE m.codice = 'tipo_1';

SELECT lives_ok(
    $$ INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-000000000c01', rapporto_controllo_id, 1, versione
       FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01' $$,
    'un rapporto firmato entra in un invio'
);
SELECT throws_ok(
    $$ INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-000000000c01', rapporto_controllo_id, 2, versione
       FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a05' $$,
    '23514', NULL,
    'un rapporto in bozza non entra in un invio'
);
SELECT throws_ok(
    $$ INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
       SELECT '01940000-0000-7000-8000-00000000000a', '01940000-0000-7000-8000-000000000c02', rapporto_controllo_id, 1, versione
       FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a01' $$,
    '23505', NULL,
    'un rapporto non sta in due invii vivi: i reinvii non creano doppioni'
);
SELECT throws_ok(
    $$ UPDATE norma.invii_catasto SET contenuto = 'altro' WHERE invio_catasto_id = '01940000-0000-7000-8000-000000000c01' $$,
    '23514', NULL,
    'il file di un invio non cambia'
);

-- ---------------------------------------------------------------------------
-- Audit
-- ---------------------------------------------------------------------------

SELECT ok(
    EXISTS (
        SELECT 1 FROM sec.audit_log
        WHERE entita = 'rapporti_controllo' AND entita_id = '01940000-0000-7000-8000-000000000a01'
          AND dettaglio ->> 'stato' = 'firmato' AND dettaglio ->> 'stato_precedente' = 'convalidato'
    ),
    'la firma del rapporto è nel registro attività'
);
SELECT is(
    (SELECT count(*)::integer FROM sec.audit_log WHERE entita = 'rapporti_controllo' AND dettaglio ? 'dati'),
    0, 'il registro attività non copia i dati del rapporto'
);

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi di app_api
-- ---------------------------------------------------------------------------

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01940000-0000-7000-8000-00000000000a', true);

SELECT is(
    (SELECT count(*)::integer FROM norma.bollini WHERE azienda_id = '01940000-0000-7000-8000-00000000000b'),
    0, 'app_api non vede i bollini di un''altra azienda'
);
SELECT lives_ok(
    $$ SELECT * FROM norma.valida_rapporto('01940000-0000-7000-8000-000000000a04') $$,
    'app_api valida un rapporto'
);
SELECT throws_ok(
    $$ DELETE FROM norma.rapporti_controllo WHERE rapporto_controllo_id = '01940000-0000-7000-8000-000000000a04' $$,
    '42501', NULL,
    'app_api non cancella i rapporti: si annullano'
);
SELECT throws_ok(
    $$ UPDATE norma.tracciati_campi SET obbligatorio = false $$,
    '42501', NULL,
    'il tracciato è in sola lettura per app_api'
);

RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

-- Bozza del rapporto precompilata dalle anagrafiche ed esiti del report SIERT.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(18);

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione, codice_ditta_siert) VALUES
    ('01950000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana', 12345);

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01950000-0000-7000-8000-0000000000f9', '01950000-0000-7000-8000-00000000000a', 'tecnico@alfa.example', 'Marco', 'Rossi', '$argon2id$prova');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome, codice_fiscale) VALUES
    ('01950000-0000-7000-8000-0000000000c1', '01950000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri', 'NREGLI80A41D612X');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia, codice_istat_comune) VALUES
    ('01950000-0000-7000-8000-0000000000d1', '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-0000000000c1',
     'Via dei Platani', '12', '50100', 'Firenze', 'FI', '048017');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id, potenza_utile_kw)
SELECT '01950000-0000-7000-8000-0000000000e1', '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-0000000000d1', tipo_impianto_id, 24.5
FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento';

INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice) VALUES
    ('01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-0000000000e1', 'toscana', 'siert', '0123456789');

INSERT INTO impianti.generatori (generatore_id, azienda_id, impianto_id, tipo_generatore_id, marca, modello, matricola, potenza_kw, installato_il, sigla_catasto)
SELECT '01950000-0000-7000-8000-0000000000f1', '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-0000000000e1',
       tipo_generatore_id, 'Marca di prova', 'Modello di prova', 'PROVA-0001', 24.5, '2018-03-15', 'GT01'
FROM impianti.tipi_generatore WHERE codice = 'prova_caldaia';

INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, generatore_id, tipo, tecnico_utente_id) VALUES
    ('01950000-0000-7000-8000-0000000000b1', '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-0000000000e1',
     '01950000-0000-7000-8000-0000000000f1', 'manutenzione', '01950000-0000-7000-8000-0000000000f9');

-- ---------------------------------------------------------------------------
-- Bozza precompilata
-- ---------------------------------------------------------------------------

SELECT has_function('norma', 'crea_rapporto', ARRAY['uuid', 'uuid', 'uuid', 'text', 'date', 'uuid'], 'esiste norma.crea_rapporto');
SELECT has_function('norma', 'registra_esiti_invio', ARRAY['uuid', 'integer', 'jsonb'], 'esiste norma.registra_esiti_invio');

CREATE TEMPORARY TABLE bozza (id uuid) ON COMMIT DROP;
INSERT INTO bozza
SELECT norma.crea_rapporto('01950000-0000-7000-8000-0000000000e1', '01950000-0000-7000-8000-0000000000f1',
                           '01950000-0000-7000-8000-0000000000b1', 'tipo_1',
                           (now() AT TIME ZONE 'Europe/Rome')::date - 2, '01950000-0000-7000-8000-0000000000f9');

SELECT is(
    (SELECT dati FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza)),
    '{"codice_catasto": "0123456789", "potenza_totale": 24.50, "comune": "048017", "indirizzo_impianto": "Via dei Platani, 12",
      "responsabile_impianto": "Neri Giulia", "codice_fiscale": "NREGLI80A41D612X", "data_installazione": "2018-03-15",
      "fabbricante": "Marca di prova", "modello": "Modello di prova", "matricola": "PROVA-0001", "gruppo": "GT01",
      "potenza_utile": 24.50, "manutentore": "Rossi Marco"}'::jsonb,
    'la bozza copia catasto, sede, responsabile, generatore e tecnico senza trasformarli'
);
SELECT is(
    (SELECT (stato, tecnico_id) FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza))::text,
    '(bozza,01950000-0000-7000-8000-0000000000f9)',
    'la bozza nasce in bozza con il tecnico'
);
SELECT throws_ok(
    $$ SELECT norma.crea_rapporto('01950000-0000-7000-8000-0000000000e1', '01950000-0000-7000-8000-0000000000f1',
                                  '01950000-0000-7000-8000-0000000000b1', 'tipo_1',
                                  (now() AT TIME ZONE 'Europe/Rome')::date, NULL) $$,
    '23505', NULL,
    'un generatore ha un solo rapporto vivo per intervento'
);
SELECT throws_ok(
    $$ SELECT norma.crea_rapporto('01950000-0000-7000-8000-0000000000e1', '01950000-0000-7000-8000-0000000000f1', NULL, 'tipo_9', '2026-11-01', NULL) $$,
    '22023', NULL,
    'un modello sconosciuto è rifiutato'
);
SELECT throws_ok(
    $$ SELECT norma.crea_rapporto('01950000-0000-7000-8000-0000000000e1', '01950000-0000-7000-8000-0000000000e1', NULL, 'tipo_1', '2026-11-01', NULL) $$,
    'P0002', NULL,
    'un generatore di un altro impianto non è trovato'
);

-- ---------------------------------------------------------------------------
-- Esiti del report
-- ---------------------------------------------------------------------------

-- Il rapporto completo, con bollino, firmato e messo in un invio caricato.
UPDATE norma.rapporti_controllo
SET dati = dati || '{"titolo_responsabile": "PROPRIETARIO", "dichiarazione_conformita": "3", "libretto_uso_manutenzione": "3",
    "libretto_climatizzazione": "3", "libretto_compilato_correttamente": "3", "trattamento_riscaldamento": "17",
    "trattamento_acs": "16", "locale_idoneo": "16", "generatore_idoneo_installazione_esterna": "3",
    "aperture_ventilazione_libere": "9", "aperture_ventilazione_adeguate": "9", "canale_fumo_idoneo": "3",
    "regolazione_temperatura_funzionante": "3", "assenza_perdite_combustibile": "16", "idonea_tenuta_impianto": "3",
    "tipo_generatore": "GRUPPO TERMICO SINGOLO", "potenza_focolare": 25.8, "climatizzazione_invernale": "SI",
    "produzione_acs": "SI", "combustibile": "Gas naturale", "evacuazione_fumi": "Forzata",
    "dispositivi_comando_funzionanti": "3", "dispositivi_sicurezza_integri": "3", "valvola_sicurezza": "3",
    "pulizia_scambiatore": "3", "presenza_riflussi": "4", "risultati_uni_10389": "3", "temperatura_fumi": 68.2,
    "temperatura_aria_comburente": 18.5, "o2": 3.1, "co2": 9.8, "co": 12, "rendimento_combustione": 97.4,
    "funziona_in_sicurezza": "21"}'
WHERE rapporto_controllo_id = (SELECT id FROM bozza);

INSERT INTO norma.bollini (azienda_id, tipo_bollino_id, codice, acquistato_il)
SELECT '01950000-0000-7000-8000-00000000000a', tipo_bollino_id, '2026000000101', '2026-01-10'
FROM norma.tipi_bollino WHERE codice = 'gt_fino_35';

SELECT norma.assegna_bollino(id, (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = id),
                             (SELECT tipo_bollino_id FROM norma.tipi_bollino WHERE codice = 'gt_fino_35'))
FROM bozza;
SELECT norma.convalida_rapporto(id, (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = id)) FROM bozza;
SELECT norma.firma_rapporto(id, (SELECT versione FROM norma.rapporti_controllo WHERE rapporto_controllo_id = id)) FROM bozza;

INSERT INTO norma.invii_catasto (invio_catasto_id, azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato, nome_file, contenuto, impronta_sha256)
SELECT '01950000-0000-7000-8000-000000000c01', '01950000-0000-7000-8000-00000000000a', 'toscana', 'siert', 'import_massivo_rcee',
       modello_rapporto_id, '3.0', 'rcee_tipo_1.txt', 'riga di prova', sha256(convert_to('riga di prova', 'UTF8'))
FROM norma.modelli_rapporto WHERE codice = 'tipo_1';

INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
SELECT '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-000000000c01', rapporto_controllo_id, 1, versione
FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza);

SELECT throws_ok(
    $$ SELECT norma.registra_esiti_invio('01950000-0000-7000-8000-000000000c01', 1, '[]') $$,
    '55000', NULL,
    'gli esiti si registrano solo dopo il caricamento sul portale'
);

UPDATE norma.invii_catasto SET stato = 'caricato', caricato_il = now() WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c01';

SELECT is(
    norma.registra_esiti_invio('01950000-0000-7000-8000-000000000c01', 1, '[]'),
    false,
    'con una versione superata gli esiti non si registrano'
);
SELECT throws_ok(
    $$ SELECT norma.registra_esiti_invio('01950000-0000-7000-8000-000000000c01',
           (SELECT versione FROM norma.invii_catasto WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c01'), '[]') $$,
    '23514', NULL,
    'ogni riga dell''invio deve avere un esito'
);
SELECT ok(
    norma.registra_esiti_invio('01950000-0000-7000-8000-000000000c01',
        (SELECT versione FROM norma.invii_catasto WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c01'),
        jsonb_build_array(jsonb_build_object('rapporto_controllo_id', (SELECT id FROM bozza), 'esito', 'scartato', 'causa', 'Generatore non trovato sul CIT'))),
    'gli esiti del report si registrano'
);
SELECT is(
    (SELECT stato FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza)),
    'scartato', 'un rapporto scartato dal SIERT diventa scartato'
);
SELECT is(
    (SELECT stato FROM norma.invii_catasto WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c01'),
    'elaborato', 'con gli esiti l''invio è elaborato'
);
SELECT lives_ok(
    $$ INSERT INTO norma.invii_catasto (invio_catasto_id, azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato, nome_file, contenuto, impronta_sha256)
       SELECT '01950000-0000-7000-8000-000000000c02', '01950000-0000-7000-8000-00000000000a', 'toscana', 'siert', 'import_massivo_rcee',
              modello_rapporto_id, '3.0', 'rcee_tipo_1_bis.txt', 'riga', sha256(convert_to('riga', 'UTF8'))
       FROM norma.modelli_rapporto WHERE codice = 'tipo_1';
       INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
       SELECT '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-000000000c02', rapporto_controllo_id, 1, versione
       FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza) $$,
    'un rapporto scartato entra in un invio nuovo'
);

-- ---------------------------------------------------------------------------
-- Annullamento di un invio mai caricato
-- ---------------------------------------------------------------------------

SELECT has_function('norma', 'annulla_invio', ARRAY['uuid', 'integer'], 'esiste norma.annulla_invio');
SELECT ok(
    norma.annulla_invio('01950000-0000-7000-8000-000000000c02',
        (SELECT versione FROM norma.invii_catasto WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c02')),
    'un invio preparato si annulla'
);
SELECT throws_ok(
    $$ SELECT norma.annulla_invio('01950000-0000-7000-8000-000000000c01',
           (SELECT versione FROM norma.invii_catasto WHERE invio_catasto_id = '01950000-0000-7000-8000-000000000c01')) $$,
    '55000', NULL,
    'un invio elaborato non si annulla'
);
SELECT lives_ok(
    $$ INSERT INTO norma.invii_catasto (invio_catasto_id, azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato, nome_file, contenuto, impronta_sha256)
       SELECT '01950000-0000-7000-8000-000000000c03', '01950000-0000-7000-8000-00000000000a', 'toscana', 'siert', 'import_massivo_rcee',
              modello_rapporto_id, '3.0', 'rcee_tipo_1_ter.txt', 'riga', sha256(convert_to('riga', 'UTF8'))
       FROM norma.modelli_rapporto WHERE codice = 'tipo_1';
       INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto)
       SELECT '01950000-0000-7000-8000-00000000000a', '01950000-0000-7000-8000-000000000c03', rapporto_controllo_id, 1, versione
       FROM norma.rapporti_controllo WHERE rapporto_controllo_id = (SELECT id FROM bozza) $$,
    'annullato l''invio, i suoi rapporti entrano in un invio nuovo'
);

SELECT * FROM finish();

ROLLBACK;

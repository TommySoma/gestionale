-- Registro F-Gas: GWP, obbligo e frequenza dei controlli, certificati, registro,
-- scadenze del controllo delle perdite e dati per la Banca dati FGAS.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(24);

CREATE TEMP TABLE oggi AS SELECT (now() AT TIME ZONE 'Europe/Rome')::date AS d;
GRANT SELECT ON oggi TO app_api;

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01990000-0000-7000-8000-00000000000a', 'Clima Alfa Srl (fittizia)', 'toscana'),
    ('01990000-0000-7000-8000-00000000000b', 'Clima Beta Srl (fittizia)', 'toscana');

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01990000-0000-7000-8000-0000000000f1', '01990000-0000-7000-8000-00000000000a', 'certificato@alfa.example', 'Marco', 'Rossi', '$argon2id$prova'),
    ('01990000-0000-7000-8000-0000000000f2', '01990000-0000-7000-8000-00000000000a', 'senza@alfa.example', 'Luca', 'Bianchi', '$argon2id$prova');

INSERT INTO sec.certificazioni (azienda_id, utente_id, tipo, numero, ente, scade_il) VALUES
    ('01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000f1', 'fgas_persona', 'PERS-PROVA-1', 'Ente di prova', (SELECT d FROM oggi) + 365),
    ('01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000f2', 'fgas_persona', 'PERS-SCADUTO', 'Ente di prova', (SELECT d FROM oggi) - 1),
    ('01990000-0000-7000-8000-00000000000a', NULL, 'fgas_impresa', 'IMPR-PROVA-1', 'Ente di prova', NULL);

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome, codice_fiscale) VALUES
    ('01990000-0000-7000-8000-0000000000c1', '01990000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri', 'NREGLI80A41D612X');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia) VALUES
    ('01990000-0000-7000-8000-0000000000d1', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000c1',
     'Via del Freddo', '7', '50100', 'Firenze', 'FI');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id)
SELECT '01990000-0000-7000-8000-0000000000e1', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000d1', tipo_impianto_id
FROM impianti.tipi_impianto WHERE codice = 'prova_climatizzazione';

INSERT INTO impianti.generatori (generatore_id, azienda_id, impianto_id, tipo_generatore_id, matricola)
SELECT g.id, '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000e1', t.tipo_generatore_id, g.matricola
FROM impianti.tipi_generatore t,
     (VALUES ('01990000-0000-7000-8000-000000000071'::uuid, 'SPLIT-1'), ('01990000-0000-7000-8000-000000000072'::uuid, 'SPLIT-2')) AS g (id, matricola)
WHERE t.codice = 'prova_climatizzatore';

-- ---------------------------------------------------------------------------
-- Gas e frequenze
-- ---------------------------------------------------------------------------

SELECT results_eq(
    $$SELECT codice, gwp FROM norma.gas_fluorurati WHERE miscela ORDER BY codice$$,
    $$VALUES ('R404A', 3921.600::numeric(12,3)), ('R407C', 1773.850), ('R410A', 2087.500)$$,
    'il GWP delle miscele è la media pesata dei componenti (allegato VI)'
);
SELECT ok(
    (SELECT bool_and(da_verificare) FROM norma.gas_fluorurati WHERE miscela)
    AND (SELECT NOT bool_or(da_verificare) FROM norma.gas_fluorurati WHERE NOT miscela),
    'i gas puri sono verificati sul regolamento, le miscele no'
);

-- Apparecchiature di prova su un generatore libero, una alla volta.
CREATE TEMP TABLE casi (n int, gas text, kg numeric, ermetica boolean, residenziale boolean, rilevamento boolean, atteso int);
INSERT INTO casi VALUES
    (1, 'R32',     3,   false, false, false, NULL),
    (2, 'R410A',   3,   false, false, false, 12),
    (3, 'R410A',   3,   false, false, true,  24),
    (4, 'R410A',   30,  false, false, false, 6),
    (5, 'R410A',   300, false, false, false, 3),
    (6, 'R410A',   4,   true,  false, false, NULL),
    (7, 'R410A',   5,   true,  false, false, 12),
    (8, 'R410A',   2.5, true,  true,  false, NULL),
    (9, 'R1234yf', 2,   false, false, false, 12),
    (10, 'R1234yf', 1.5, true, false, false, NULL);

CREATE FUNCTION pg_temp.frequenza(p_gas text, p_kg numeric, p_ermetica boolean, p_residenziale boolean, p_rilevamento boolean)
RETURNS integer LANGUAGE plpgsql AS $$
DECLARE v_id uuid; v_mesi integer;
BEGIN
    INSERT INTO impianti.apparecchiature_fgas (azienda_id, generatore_id, gas_id, carica_kg, ermetica_etichettata, edificio_residenziale, sistema_rilevamento)
    SELECT '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-000000000072', gas_id, p_kg, p_ermetica, p_residenziale, p_rilevamento
    FROM norma.gas_fluorurati WHERE codice = p_gas
    RETURNING apparecchiatura_fgas_id INTO v_id;
    v_mesi := norma.frequenza_controllo_perdite(v_id);
    DELETE FROM impianti.apparecchiature_fgas WHERE apparecchiatura_fgas_id = v_id;
    RETURN v_mesi;
END $$;

SELECT is(pg_temp.frequenza(gas, kg, ermetica, residenziale, rilevamento), atteso,
          format('caso %s: %s kg di %s%s%s%s', n, kg, gas, CASE WHEN ermetica THEN ', ermetica' ELSE '' END,
                 CASE WHEN residenziale THEN ', residenziale' ELSE '' END, CASE WHEN rilevamento THEN ', con rilevamento' ELSE '' END))
FROM casi ORDER BY n;

-- ---------------------------------------------------------------------------
-- Apparecchiatura vera: scadenza, certificati, registro
-- ---------------------------------------------------------------------------

INSERT INTO impianti.apparecchiature_fgas (apparecchiatura_fgas_id, azienda_id, generatore_id, gas_id, carica_kg, codice_univoco)
SELECT '01990000-0000-7000-8000-0000000000a1', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-000000000071', gas_id, 3, 'COD-UNIVOCO-1'
FROM norma.gas_fluorurati WHERE codice = 'R410A';

SELECT is(
    (SELECT data_scadenza FROM lavoro.scadenze WHERE impianto_id = '01990000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_perdite' AND stato = 'aperta'),
    (SELECT d FROM oggi),
    'un''apparecchiatura soggetta mai controllata ha il controllo delle perdite da fare subito'
);

SELECT throws_ok(
    $$INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, tipo, stato, finestra, tecnico_utente_id)
      VALUES ('01990000-0000-7000-8000-0000000000b9', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000e1',
              'manutenzione', 'pianificato', tstzrange(now() - interval '2 hours', now() - interval '1 hour'), '01990000-0000-7000-8000-0000000000f2')$$,
    'GF001', NULL,
    'un tecnico con il certificato scaduto non si assegna a una manutenzione dell''impianto'
);
SELECT lives_ok(
    $$INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, tipo, stato, finestra, tecnico_utente_id)
      VALUES ('01990000-0000-7000-8000-0000000000b8', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000e1',
              'sopralluogo', 'pianificato', tstzrange(now() - interval '4 hours', now() - interval '3 hours'), '01990000-0000-7000-8000-0000000000f2')$$,
    'a un sopralluogo sì'
);

INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, tipo, stato, finestra, tecnico_utente_id)
VALUES ('01990000-0000-7000-8000-0000000000b1', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000e1',
        'manutenzione', 'pianificato', tstzrange(now() - interval '6 hours', now() - interval '5 hours'), '01990000-0000-7000-8000-0000000000f1');

SELECT throws_ok(
    $$INSERT INTO lavoro.interventi_fgas (azienda_id, apparecchiatura_fgas_id, intervento_id, tipo, eseguito_il, perdita_rilevata, tecnico_utente_id, certificato_tecnico, termine_comunicazione)
      VALUES ('01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000a1', '01990000-0000-7000-8000-0000000000b1',
              'controllo_perdite', (SELECT d FROM oggi), false, '01990000-0000-7000-8000-0000000000f2', 'x', (SELECT d FROM oggi))$$,
    'GF001', NULL,
    'nel registro non entra un''operazione di un tecnico senza certificato in corso'
);

INSERT INTO lavoro.interventi_fgas (intervento_fgas_id, azienda_id, apparecchiatura_fgas_id, intervento_id, tipo, eseguito_il, perdita_rilevata,
                                    tecnico_utente_id, certificato_tecnico, termine_comunicazione)
VALUES ('01990000-0000-7000-8000-0000000000a5', '01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000a1',
        '01990000-0000-7000-8000-0000000000b1', 'controllo_perdite', (SELECT d FROM oggi) - 10, false,
        '01990000-0000-7000-8000-0000000000f1', 'scritto a mano', (SELECT d FROM oggi));

SELECT results_eq(
    $$SELECT certificato_tecnico, certificato_impresa, termine_comunicazione FROM lavoro.interventi_fgas
      WHERE intervento_fgas_id = '01990000-0000-7000-8000-0000000000a5'$$,
    $$VALUES ('PERS-PROVA-1', 'IMPR-PROVA-1', (SELECT d FROM oggi) + 20)$$,
    'i certificati si copiano da quelli in corso e il termine è di 30 giorni'
);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.scadenze
     WHERE impianto_id = '01990000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_perdite'
       AND stato = 'chiusa' AND intervento_chiusura_id = '01990000-0000-7000-8000-0000000000b1'),
    1,
    'il controllo chiude la scadenza con il suo intervento'
);
SELECT is(
    (SELECT data_scadenza FROM lavoro.scadenze WHERE impianto_id = '01990000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_perdite' AND stato = 'aperta'),
    ((SELECT d FROM oggi) - 10 + interval '12 months')::date,
    'e la prossima è dopo la frequenza dell''apparecchiatura'
);

INSERT INTO lavoro.interventi_fgas (azienda_id, apparecchiatura_fgas_id, intervento_id, tipo, eseguito_il, gas_aggiunto_kg, tecnico_utente_id, certificato_tecnico, termine_comunicazione)
VALUES ('01990000-0000-7000-8000-00000000000a', '01990000-0000-7000-8000-0000000000a1', '01990000-0000-7000-8000-0000000000b1',
        'riparazione', (SELECT d FROM oggi) - 2, 0.4, '01990000-0000-7000-8000-0000000000f1', 'x', (SELECT d FROM oggi));
SELECT is(
    (SELECT data_scadenza FROM lavoro.scadenze WHERE impianto_id = '01990000-0000-7000-8000-0000000000e1' AND tipo = 'controllo_perdite' AND stato = 'aperta'),
    ((SELECT d FROM oggi) - 2 + interval '1 month')::date,
    'dopo una riparazione il controllo va fatto entro un mese (art. 4, par. 5)'
);

-- ---------------------------------------------------------------------------
-- Banca dati FGAS
-- ---------------------------------------------------------------------------

SELECT results_eq(
    $$SELECT tipo, operatore, codice_univoco, gas, carica_kg::numeric(10,3) FROM lavoro.comunicazioni_fgas(true) ORDER BY eseguito_il$$,
    $$VALUES ('controllo_perdite', 'Neri Giulia', 'COD-UNIVOCO-1', 'R410A', 3.000::numeric(10,3)), ('riparazione', 'Neri Giulia', 'COD-UNIVOCO-1', 'R410A', 3.000)$$,
    'le operazioni da comunicare portano i dati che il portale chiede'
);

UPDATE lavoro.interventi_fgas SET comunicato_il = (SELECT d FROM oggi) WHERE intervento_fgas_id = '01990000-0000-7000-8000-0000000000a5';
SELECT is((SELECT count(*)::integer FROM lavoro.comunicazioni_fgas(true)), 1, 'una comunicata non è più da comunicare');
SELECT throws_ok(
    $$UPDATE lavoro.interventi_fgas SET note = 'ripensamento' WHERE intervento_fgas_id = '01990000-0000-7000-8000-0000000000a5'$$,
    '55000', NULL,
    'e non si modifica più'
);

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01990000-0000-7000-8000-00000000000b', true);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.interventi_fgas) + (SELECT count(*)::integer FROM impianti.apparecchiature_fgas)
        + (SELECT count(*)::integer FROM sec.certificazioni),
    0,
    'un''altra azienda non vede registro, apparecchiature e certificati'
);
RESET ROLE;

SELECT * FROM finish();

ROLLBACK;

-- Dati aziendali modificabili dall'API: colonne ammesse, audit, permessi.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(9);

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01960000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01960000-0000-7000-8000-00000000000b', 'Clima Beta Srl (fittizia)', 'toscana');

-- ---------------------------------------------------------------------------
-- Colonne: l'API cambia anagrafica e codice ditta, non la regione
-- ---------------------------------------------------------------------------

SELECT ok(
    has_column_privilege('app_api', 'sec.aziende', 'codice_ditta_siert', 'UPDATE'),
    'app_api può aggiornare il codice ditta'
);
SELECT ok(
    NOT has_column_privilege('app_api', 'sec.aziende', 'regione', 'UPDATE'),
    'app_api non può cambiare la regione'
);

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01960000-0000-7000-8000-00000000000a', true);

SELECT lives_ok(
    $$UPDATE sec.aziende SET codice_ditta_siert = 4321, ragione_sociale = 'Termoidraulica Alfa Srl (fittizia, rinominata)'$$,
    'l''API aggiorna ragione sociale e codice ditta'
);
SELECT throws_ok(
    $$UPDATE sec.aziende SET regione = 'lombardia'$$,
    '42501',
    NULL,
    'la regione non si cambia dall''API'
);
SELECT is(
    (SELECT count(*)::integer FROM sec.aziende WHERE codice_ditta_siert = 4321),
    1,
    'la RLS limita l''aggiornamento all''azienda corrente'
);
SELECT throws_ok(
    $$UPDATE sec.aziende SET codice_ditta_siert = 0$$,
    '23514',
    NULL,
    'il codice ditta è positivo'
);

RESET ROLE;

-- ---------------------------------------------------------------------------
-- Audit e permessi
-- ---------------------------------------------------------------------------

SELECT is(
    (SELECT count(*)::integer FROM sec.audit_log
     WHERE entita = 'aziende' AND entita_id = '01960000-0000-7000-8000-00000000000a' AND azione = 'aziende.update'),
    1,
    'ogni modifica dei dati aziendali finisce nell''audit'
);
SELECT set_eq(
    $$SELECT r.codice FROM sec.ruoli_permessi rp
      JOIN sec.ruoli r ON r.ruolo_id = rp.ruolo_id
      JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
      WHERE p.codice = 'azienda.lettura'$$,
    ARRAY['titolare', 'amministrativo', 'responsabile_tecnico'],
    'leggono i dati aziendali Titolare, Amministrativo e Responsabile tecnico'
);
SELECT set_eq(
    $$SELECT r.codice FROM sec.ruoli_permessi rp
      JOIN sec.ruoli r ON r.ruolo_id = rp.ruolo_id
      JOIN sec.permessi p ON p.permesso_id = rp.permesso_id
      WHERE p.codice = 'azienda.scrittura'$$,
    ARRAY['titolare'],
    'li modifica solo il Titolare'
);

SELECT * FROM finish();

ROLLBACK;

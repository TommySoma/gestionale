-- Promemoria email: parametri di posta, esito del richiamo e dati per l'invio.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(12);

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES
    ('01980000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana'),
    ('01980000-0000-7000-8000-00000000000b', 'Clima Beta Srl (fittizia)', 'toscana');

-- Tre clienti: con email e consenso, con email ma consenso revocato, senza email.
INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01980000-0000-7000-8000-0000000000c1', '01980000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri'),
    ('01980000-0000-7000-8000-0000000000c2', '01980000-0000-7000-8000-00000000000a', 'privato', 'Paolo', 'Verdi'),
    ('01980000-0000-7000-8000-0000000000c3', '01980000-0000-7000-8000-00000000000a', 'privato', 'Anna', 'Bianchi');

INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale) VALUES
    ('01980000-0000-7000-8000-00000000000a', '01980000-0000-7000-8000-0000000000c1', 'email', 'giulia.neri@example.com', true),
    ('01980000-0000-7000-8000-00000000000a', '01980000-0000-7000-8000-0000000000c2', 'email', 'paolo.verdi@example.com', true);

INSERT INTO anagrafica.consensi_privacy (azienda_id, cliente_id, finalita_privacy_id, concesso, fonte, registrato_il)
SELECT '01980000-0000-7000-8000-00000000000a', c.cliente, f.finalita_privacy_id, c.concesso, 'modulo_firmato', c.quando
FROM anagrafica.finalita_privacy f,
     (VALUES ('01980000-0000-7000-8000-0000000000c1'::uuid, true, now() - interval '2 days'),
             ('01980000-0000-7000-8000-0000000000c2'::uuid, true, now() - interval '2 days'),
             ('01980000-0000-7000-8000-0000000000c2'::uuid, false, now() - interval '1 day')) AS c (cliente, concesso, quando)
WHERE f.codice = 'promemoria_scadenze';

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia)
SELECT ('01980000-0000-7000-8000-0000000000d' || n)::uuid, '01980000-0000-7000-8000-00000000000a',
       ('01980000-0000-7000-8000-0000000000c' || n)::uuid, 'Via dei Promemoria', n::text, '50100', 'Firenze', 'FI'
FROM generate_series(1, 3) AS n;

-- Impianti con la manutenzione ogni 12 mesi: la scadenza la crea il ricalcolo.
INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id, manutenzione_mesi)
SELECT ('01980000-0000-7000-8000-0000000000e' || n)::uuid, '01980000-0000-7000-8000-00000000000a',
       ('01980000-0000-7000-8000-0000000000d' || n)::uuid, t.tipo_impianto_id, 12
FROM generate_series(1, 3) AS n, impianti.tipi_impianto t
WHERE t.codice = 'prova_riscaldamento';

CREATE TEMPORARY TABLE scelte ON COMMIT DROP AS
SELECT array_agg(scadenza_id ORDER BY impianto_id) AS id
FROM lavoro.scadenze WHERE azienda_id = '01980000-0000-7000-8000-00000000000a' AND tipo = 'manutenzione';
GRANT SELECT ON scelte TO app_api;

-- ---------------------------------------------------------------------------
-- Dati per l'invio
-- ---------------------------------------------------------------------------

SELECT has_function('lavoro', 'dati_promemoria', ARRAY['uuid[]'], 'esiste lavoro.dati_promemoria');
SELECT results_eq(
    $$SELECT cliente, email, consenso FROM lavoro.dati_promemoria((SELECT id FROM scelte)) ORDER BY cliente$$,
    $$VALUES ('Anna Bianchi', NULL::text, false), ('Giulia Neri', 'giulia.neri@example.com', true), ('Paolo Verdi', 'paolo.verdi@example.com', false)$$,
    'per ogni scadenza: email principale e consenso in corso (l''ultimo, anche se revocato)'
);
SELECT is(
    (SELECT tipo_impianto || ' · ' || indirizzo FROM lavoro.dati_promemoria((SELECT id FROM scelte)) WHERE cliente = 'Giulia Neri'),
    'Riscaldamento (voce di prova) · Via dei Promemoria 1',
    'con il tipo di impianto e l''indirizzo'
);

SET LOCAL ROLE app_api;
SELECT set_config('app.azienda_id', '01980000-0000-7000-8000-00000000000b', true);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.dati_promemoria((SELECT id FROM scelte))),
    0,
    'un''altra azienda non vede le scadenze'
);
SELECT set_config('app.azienda_id', '01980000-0000-7000-8000-00000000000a', true);
SELECT is(
    (SELECT count(*)::integer FROM lavoro.dati_promemoria((SELECT id FROM scelte))),
    3,
    'l''azienda vede le sue'
);

-- ---------------------------------------------------------------------------
-- Il promemoria inviato è un richiamo per email
-- ---------------------------------------------------------------------------

SELECT lives_ok(
    $$INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito)
      VALUES ('01980000-0000-7000-8000-00000000000a', (SELECT id[1] FROM scelte), 'email', 'promemoria_inviato')$$,
    'un promemoria inviato si registra come richiamo'
);
SELECT is(
    (SELECT stato_richiamo FROM lavoro.scadenze WHERE scadenza_id = (SELECT id[1] FROM scelte)),
    'in_attesa',
    'e porta la scadenza in attesa di risposta'
);
SELECT throws_ok(
    $$INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito)
      VALUES ('01980000-0000-7000-8000-00000000000a', (SELECT id[1] FROM scelte), 'telefono', 'promemoria_inviato')$$,
    '23514', NULL,
    'un promemoria inviato passa solo per email'
);

-- ---------------------------------------------------------------------------
-- Parametri della posta
-- ---------------------------------------------------------------------------

SELECT lives_ok(
    $$INSERT INTO sec.parametri_posta (azienda_id, host, porta, sicurezza, utente, password_cifrata, mittente_email)
      VALUES ('01980000-0000-7000-8000-00000000000a', 'smtp.example.com', 587, 'starttls', 'utente', '\x0102', 'info@example.com')$$,
    'l''API salva i parametri della propria azienda'
);
SELECT throws_ok(
    $$INSERT INTO sec.parametri_posta (azienda_id, host, porta, sicurezza, mittente_email)
      VALUES ('01980000-0000-7000-8000-00000000000b', 'smtp.example.com', 587, 'starttls', 'info@example.com')$$,
    '42501', NULL,
    'non quelli di un''altra azienda'
);
SELECT throws_ok(
    $$UPDATE sec.parametri_posta SET sicurezza = 'tls'$$,
    '23514', NULL,
    'la sicurezza è nessuna, starttls o ssl'
);

RESET ROLE;

SELECT is(
    (SELECT count(*)::integer FROM sec.audit_log WHERE entita = 'parametri_posta' AND entita_id = '01980000-0000-7000-8000-00000000000a'),
    1,
    'il cambio dei parametri di posta finisce nell''audit'
);

SELECT * FROM finish();

ROLLBACK;

-- Bozza del rapporto di controllo creata alla chiusura dell'intervento.
-- Dati fittizi. Tutto avviene in una transazione annullata alla fine.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap;

SELECT plan(13);

INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione, codice_ditta_siert) VALUES
    ('01970000-0000-7000-8000-00000000000a', 'Termoidraulica Alfa Srl (fittizia)', 'toscana', 12345);

INSERT INTO sec.utenti (utente_id, azienda_id, email, nome, cognome, password_hash) VALUES
    ('01970000-0000-7000-8000-0000000000f9', '01970000-0000-7000-8000-00000000000a', 'tecnico@alfa.example', 'Marco', 'Rossi', '$argon2id$prova');

INSERT INTO anagrafica.clienti (cliente_id, azienda_id, tipo, nome, cognome) VALUES
    ('01970000-0000-7000-8000-0000000000c1', '01970000-0000-7000-8000-00000000000a', 'privato', 'Giulia', 'Neri');

INSERT INTO anagrafica.sedi (sede_id, azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia, codice_istat_comune) VALUES
    ('01970000-0000-7000-8000-0000000000d1', '01970000-0000-7000-8000-00000000000a', '01970000-0000-7000-8000-0000000000c1',
     'Via dei Platani', '12', '50100', 'Firenze', 'FI', '048017');

INSERT INTO impianti.impianti (impianto_id, azienda_id, sede_id, tipo_impianto_id, potenza_utile_kw)
SELECT '01970000-0000-7000-8000-0000000000e1', '01970000-0000-7000-8000-00000000000a', '01970000-0000-7000-8000-0000000000d1', tipo_impianto_id, 24.5
FROM impianti.tipi_impianto WHERE codice = 'prova_riscaldamento';

INSERT INTO impianti.generatori (generatore_id, azienda_id, impianto_id, tipo_generatore_id, potenza_kw, sigla_catasto)
SELECT g.id, '01970000-0000-7000-8000-00000000000a', '01970000-0000-7000-8000-0000000000e1', t.tipo_generatore_id, 24.5, g.sigla
FROM impianti.tipi_generatore t,
     (VALUES ('01970000-0000-7000-8000-0000000000f1'::uuid, 'GT01'), ('01970000-0000-7000-8000-0000000000f2'::uuid, 'GT02')) AS g (id, sigla)
WHERE t.codice = 'prova_caldaia';

-- Manutenzioni pianificate nei giorni scorsi, in finestre che non si sovrappongono.
INSERT INTO lavoro.interventi (intervento_id, azienda_id, impianto_id, generatore_id, tipo, stato, finestra, tecnico_utente_id)
SELECT i.id, '01970000-0000-7000-8000-00000000000a', '01970000-0000-7000-8000-0000000000e1', i.generatore, i.tipo, 'pianificato',
       tstzrange(now() - make_interval(days => i.giorni), now() - make_interval(days => i.giorni) + interval '1 hour'),
       '01970000-0000-7000-8000-0000000000f9'
FROM (VALUES
    ('01970000-0000-7000-8000-0000000000b1'::uuid, '01970000-0000-7000-8000-0000000000f1'::uuid, 'manutenzione', 3),
    ('01970000-0000-7000-8000-0000000000b2'::uuid, NULL, 'manutenzione', 4),
    ('01970000-0000-7000-8000-0000000000b3'::uuid, '01970000-0000-7000-8000-0000000000f1'::uuid, 'manutenzione', 5),
    ('01970000-0000-7000-8000-0000000000b4'::uuid, '01970000-0000-7000-8000-0000000000f2'::uuid, 'manutenzione', 6),
    ('01970000-0000-7000-8000-0000000000b5'::uuid, '01970000-0000-7000-8000-0000000000f2'::uuid, 'manutenzione', 7)
) AS i (id, generatore, tipo, giorni);

SELECT has_function('lavoro', 'chiudi_intervento', ARRAY['uuid', 'integer', 'text', 'text', 'jsonb', 'boolean', 'text', 'uuid'],
    'chiudi_intervento accetta modello e generatore del rapporto');

-- ---------------------------------------------------------------------------
-- Richieste non coerenti: nessuna chiusura
-- ---------------------------------------------------------------------------

SELECT throws_ok(
    $$SELECT lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b1', 1, 'risolto', NULL, '[]', false, 'tipo_1')$$,
    '22023', NULL,
    'senza controllo di efficienza non si crea la bozza'
);
SELECT throws_ok(
    $$SELECT lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b2', 1, 'risolto', NULL, '[]', true, 'tipo_1')$$,
    '22023', NULL,
    'senza un generatore la bozza non ha di chi parlare'
);
SELECT throws_ok(
    $$SELECT lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b1', 1, 'risolto', NULL, '[]', true, 'tipo_9')$$,
    '22023', NULL,
    'un modello sconosciuto è rifiutato'
);
SELECT is(
    (SELECT stato FROM lavoro.interventi WHERE intervento_id = '01970000-0000-7000-8000-0000000000b1'),
    'pianificato',
    'se la bozza non si crea, l''intervento resta aperto'
);

-- ---------------------------------------------------------------------------
-- Bozza sul generatore dell'intervento
-- ---------------------------------------------------------------------------

SELECT ok(
    lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b1', 1, 'risolto', NULL, '[]', true, 'tipo_1'),
    'la manutenzione con il controllo si chiude con il modello scelto'
);
SELECT results_eq(
    $$SELECT generatore_id, stato, tecnico_id, data_controllo, dati ->> 'gruppo', dati ->> 'comune'
      FROM norma.rapporti_controllo WHERE intervento_id = '01970000-0000-7000-8000-0000000000b1'$$,
    $$VALUES ('01970000-0000-7000-8000-0000000000f1'::uuid, 'bozza', '01970000-0000-7000-8000-0000000000f9'::uuid,
              ((now() - interval '3 days') AT TIME ZONE 'Europe/Rome')::date, 'GT01', '048017')$$,
    'la bozza è del generatore dell''intervento, con il tecnico, il giorno in calendario e i dati delle anagrafiche'
);

-- ---------------------------------------------------------------------------
-- Generatore scelto in chiusura, chiusura senza bozza, nessun doppione
-- ---------------------------------------------------------------------------

SELECT ok(
    lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b2', 1, 'risolto', NULL, '[]', true, 'tipo_1',
                             '01970000-0000-7000-8000-0000000000f2'),
    'il generatore si sceglie in chiusura quando l''intervento non lo indica'
);
SELECT is(
    (SELECT generatore_id FROM norma.rapporti_controllo WHERE intervento_id = '01970000-0000-7000-8000-0000000000b2'),
    '01970000-0000-7000-8000-0000000000f2'::uuid,
    'la bozza è del generatore scelto'
);

SELECT ok(
    lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b3', 1, 'risolto', NULL, '[]', true),
    'con il controllo ma senza modello l''intervento si chiude lo stesso'
);
SELECT is(
    (SELECT count(*)::integer FROM norma.rapporti_controllo WHERE intervento_id = '01970000-0000-7000-8000-0000000000b3'),
    0,
    'senza modello non nasce nessuna bozza'
);

SELECT norma.crea_rapporto('01970000-0000-7000-8000-0000000000e1', '01970000-0000-7000-8000-0000000000f2',
                           '01970000-0000-7000-8000-0000000000b4', 'tipo_1',
                           (now() AT TIME ZONE 'Europe/Rome')::date - 6, '01970000-0000-7000-8000-0000000000f9');
SELECT lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b4', 1, 'risolto', NULL, '[]', true, 'tipo_1');
SELECT is(
    (SELECT count(*)::integer FROM norma.rapporti_controllo WHERE intervento_id = '01970000-0000-7000-8000-0000000000b4'),
    1,
    'se l''intervento ha già il rapporto del generatore, la chiusura non ne crea un altro'
);

-- Le chiamate con sei argomenti, come prima di questa migrazione, non creano bozze.
SELECT lavoro.chiudi_intervento('01970000-0000-7000-8000-0000000000b5', 1, 'risolto', NULL, '[]', true);
SELECT is(
    (SELECT count(*)::integer FROM norma.rapporti_controllo WHERE intervento_id = '01970000-0000-7000-8000-0000000000b5'),
    0,
    'la chiamata di prima resta valida e non crea bozze'
);

SELECT * FROM finish();

ROLLBACK;

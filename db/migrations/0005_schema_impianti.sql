-- 0005: schema impianti (tipi, combustibili, impianti, riferimenti al catasto, generatori).
-- I libretti di impianto arrivano con M4: dipendono dal modello normativo, da verificare.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

CREATE SCHEMA impianti;

COMMENT ON SCHEMA impianti IS 'Impianti termici e di climatizzazione, generatori, riferimenti ai catasti regionali';

ALTER DEFAULT PRIVILEGES IN SCHEMA impianti REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Lookup normative: dati con validità, non codice.
-- I valori iniziali sono VOCI DI PROVA (da_verificare = true): quelli veri vanno
-- presi dai moduli ufficiali (SIERT, libretto di impianto) e caricati con una
-- nuova migrazione, che chiude le voci di prova con valido_al.
-- ---------------------------------------------------------------------------

CREATE TABLE impianti.tipi_impianto (
    tipo_impianto_id uuid        NOT NULL DEFAULT uuidv7(),
    codice           text        NOT NULL,
    nome             text        NOT NULL,
    descrizione      text,
    valido_dal       date        NOT NULL,
    valido_al        date,
    da_verificare    boolean     NOT NULL DEFAULT true,
    fonte            text,
    creato_il        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_tipi_impianto PRIMARY KEY (tipo_impianto_id),
    CONSTRAINT uq_tipi_impianto_codice UNIQUE (codice),
    CONSTRAINT ck_tipi_impianto_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_tipi_impianto_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    -- Una voce verificata dice da dove viene.
    CONSTRAINT ck_tipi_impianto_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

COMMENT ON TABLE impianti.tipi_impianto IS 'Tipi di impianto; collegati in M3 alle regole di cadenza';

CREATE TABLE impianti.tipi_generatore (
    tipo_generatore_id uuid        NOT NULL DEFAULT uuidv7(),
    codice             text        NOT NULL,
    nome               text        NOT NULL,
    valido_dal         date        NOT NULL,
    valido_al          date,
    da_verificare      boolean     NOT NULL DEFAULT true,
    fonte              text,
    creato_il          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_tipi_generatore PRIMARY KEY (tipo_generatore_id),
    CONSTRAINT uq_tipi_generatore_codice UNIQUE (codice),
    CONSTRAINT ck_tipi_generatore_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_tipi_generatore_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_tipi_generatore_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

CREATE TABLE impianti.combustibili (
    combustibile_id uuid        NOT NULL DEFAULT uuidv7(),
    codice          text        NOT NULL,
    nome            text        NOT NULL,
    valido_dal      date        NOT NULL,
    valido_al       date,
    da_verificare   boolean     NOT NULL DEFAULT true,
    fonte           text,
    creato_il       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_combustibili PRIMARY KEY (combustibile_id),
    CONSTRAINT uq_combustibili_codice UNIQUE (codice),
    CONSTRAINT ck_combustibili_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_combustibili_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_combustibili_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

-- Voci di prova: nomi volutamente segnati, per non scambiarle per l'elenco ufficiale.
INSERT INTO impianti.tipi_impianto (codice, nome, descrizione, valido_dal) VALUES
    ('prova_riscaldamento', 'Riscaldamento (voce di prova)', 'Da sostituire con l''elenco del modulo ufficiale', '2026-01-01'),
    ('prova_climatizzazione', 'Climatizzazione (voce di prova)', 'Da sostituire con l''elenco del modulo ufficiale', '2026-01-01'),
    ('prova_pompa_di_calore', 'Pompa di calore (voce di prova)', 'Da sostituire con l''elenco del modulo ufficiale', '2026-01-01');

INSERT INTO impianti.tipi_generatore (codice, nome, valido_dal) VALUES
    ('prova_caldaia', 'Caldaia (voce di prova)', '2026-01-01'),
    ('prova_pompa_di_calore', 'Pompa di calore (voce di prova)', '2026-01-01'),
    ('prova_climatizzatore', 'Climatizzatore (voce di prova)', '2026-01-01');

INSERT INTO impianti.combustibili (codice, nome, valido_dal) VALUES
    ('prova_gas_naturale', 'Gas naturale (voce di prova)', '2026-01-01'),
    ('prova_gpl', 'GPL (voce di prova)', '2026-01-01'),
    ('prova_energia_elettrica', 'Energia elettrica (voce di prova)', '2026-01-01');

-- ---------------------------------------------------------------------------
-- Impianti
-- ---------------------------------------------------------------------------

CREATE TABLE impianti.impianti (
    impianto_id             uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id              uuid          NOT NULL,
    sede_id                 uuid          NOT NULL,
    tipo_impianto_id        uuid          NOT NULL,
    stato                   text          NOT NULL DEFAULT 'attivo',
    anno_installazione      integer,
    potenza_utile_kw        numeric(9, 2),
    combustibile_id         uuid,
    pdr                     text,
    proprietario_cliente_id uuid,
    responsabile_cliente_id uuid,
    note                    text,
    dismesso_il             date,
    eliminato_il            timestamptz,
    eliminato_da            uuid,
    creato_il               timestamptz   NOT NULL DEFAULT now(),
    creato_da               uuid,
    modificato_il           timestamptz,
    modificato_da           uuid,
    versione                integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_impianti PRIMARY KEY (impianto_id),
    CONSTRAINT uq_impianti_azienda_impianto UNIQUE (azienda_id, impianto_id),
    -- Chiavi composte: sede e clienti appartengono per forza alla stessa azienda.
    CONSTRAINT fk_impianti_sedi FOREIGN KEY (azienda_id, sede_id) REFERENCES anagrafica.sedi (azienda_id, sede_id),
    CONSTRAINT fk_impianti_proprietario FOREIGN KEY (azienda_id, proprietario_cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_impianti_responsabile FOREIGN KEY (azienda_id, responsabile_cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_impianti_tipi FOREIGN KEY (tipo_impianto_id) REFERENCES impianti.tipi_impianto (tipo_impianto_id),
    CONSTRAINT fk_impianti_combustibili FOREIGN KEY (combustibile_id) REFERENCES impianti.combustibili (combustibile_id),
    CONSTRAINT fk_impianti_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_impianti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_impianti_eliminato_da FOREIGN KEY (eliminato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_impianti_stato CHECK (stato IN ('attivo', 'dismesso')),
    CONSTRAINT ck_impianti_dismissione CHECK ((stato = 'dismesso') = (dismesso_il IS NOT NULL)),
    CONSTRAINT ck_impianti_anno CHECK (anno_installazione BETWEEN 1900 AND 2100),
    CONSTRAINT ck_impianti_potenza CHECK (potenza_utile_kw > 0),
    -- Il formato del PDR non è verificato sul testo ufficiale: qui solo testo non vuoto e senza spazi.
    CONSTRAINT ck_impianti_pdr CHECK (pdr ~ '^\S+$'),
    CONSTRAINT ck_impianti_eliminazione CHECK (eliminato_da IS NULL OR eliminato_il IS NOT NULL),
    CONSTRAINT ck_impianti_versione CHECK (versione >= 1)
);

-- Un PDR appartiene a un solo impianto attivo per azienda.
CREATE UNIQUE INDEX uq_impianti_azienda_pdr_attivi
    ON impianti.impianti (azienda_id, pdr)
    WHERE pdr IS NOT NULL AND stato = 'attivo' AND eliminato_il IS NULL;

CREATE INDEX ix_impianti_sede ON impianti.impianti (sede_id);
CREATE INDEX ix_impianti_proprietario ON impianti.impianti (proprietario_cliente_id) WHERE proprietario_cliente_id IS NOT NULL;
CREATE INDEX ix_impianti_responsabile ON impianti.impianti (responsabile_cliente_id) WHERE responsabile_cliente_id IS NOT NULL;

COMMENT ON TABLE impianti.impianti IS 'Impianto presso una sede; si dismette con lo stato, si elimina solo logicamente';

-- ---------------------------------------------------------------------------
-- Riferimenti ai catasti regionali
-- ---------------------------------------------------------------------------

-- Unico per azienda, regione e codice: due imprese possono seguire lo stesso
-- impianto in tempi diversi, e un vincolo globale rivelerebbe dati di un'altra azienda.
CREATE TABLE impianti.riferimenti_catasto (
    riferimento_catasto_id uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id             uuid        NOT NULL,
    impianto_id            uuid        NOT NULL,
    regione                text        NOT NULL,
    sistema                text        NOT NULL,
    codice                 text        NOT NULL,
    registrato_il          date,
    creato_il              timestamptz NOT NULL DEFAULT now(),
    creato_da              uuid,
    modificato_il          timestamptz,
    modificato_da          uuid,
    versione               integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_riferimenti_catasto PRIMARY KEY (riferimento_catasto_id),
    CONSTRAINT uq_riferimenti_catasto_codice UNIQUE (azienda_id, regione, codice),
    CONSTRAINT uq_riferimenti_catasto_impianto_regione UNIQUE (impianto_id, regione),
    CONSTRAINT fk_riferimenti_catasto_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_riferimenti_catasto_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_riferimenti_catasto_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_riferimenti_catasto_regione CHECK (regione IN ('toscana', 'lombardia')),
    -- Un sistema per regione: SIERT in Toscana, CURIT in Lombardia.
    CONSTRAINT ck_riferimenti_catasto_sistema CHECK (
        (regione = 'toscana' AND sistema = 'siert') OR (regione = 'lombardia' AND sistema = 'curit')
    ),
    CONSTRAINT ck_riferimenti_catasto_codice CHECK (codice ~ '^\S+$'),
    CONSTRAINT ck_riferimenti_catasto_versione CHECK (versione >= 1)
);

COMMENT ON TABLE impianti.riferimenti_catasto IS 'Codice catasto (SIERT) o targa (CURIT) dell''impianto';

-- ---------------------------------------------------------------------------
-- Generatori
-- ---------------------------------------------------------------------------

CREATE TABLE impianti.generatori (
    generatore_id      uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id         uuid          NOT NULL,
    impianto_id        uuid          NOT NULL,
    tipo_generatore_id uuid          NOT NULL,
    marca              text,
    modello            text,
    matricola          text,
    potenza_kw         numeric(9, 2),
    combustibile_id    uuid,
    installato_il      date,
    dismesso_il        date,
    note               text,
    creato_il          timestamptz   NOT NULL DEFAULT now(),
    creato_da          uuid,
    modificato_il      timestamptz,
    modificato_da      uuid,
    versione           integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_generatori PRIMARY KEY (generatore_id),
    CONSTRAINT uq_generatori_azienda_generatore UNIQUE (azienda_id, generatore_id),
    CONSTRAINT fk_generatori_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_generatori_tipi FOREIGN KEY (tipo_generatore_id) REFERENCES impianti.tipi_generatore (tipo_generatore_id),
    CONSTRAINT fk_generatori_combustibili FOREIGN KEY (combustibile_id) REFERENCES impianti.combustibili (combustibile_id),
    CONSTRAINT fk_generatori_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_generatori_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_generatori_potenza CHECK (potenza_kw > 0),
    CONSTRAINT ck_generatori_date CHECK (dismesso_il IS NULL OR installato_il IS NULL OR dismesso_il >= installato_il),
    CONSTRAINT ck_generatori_versione CHECK (versione >= 1)
);

CREATE INDEX ix_generatori_impianto ON impianti.generatori (impianto_id);

-- Matricola duplicata: non è un errore (marche diverse possono coincidere), ma l'API
-- lo segnala. L'indice rende veloce il controllo.
CREATE INDEX ix_generatori_azienda_matricola
    ON impianti.generatori (azienda_id, lower(matricola))
    WHERE matricola IS NOT NULL;

COMMENT ON TABLE impianti.generatori IS 'Generatori di un impianto; si dismettono con la data, non si cancellano';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

CREATE TRIGGER tr_impianti_metadati
    BEFORE UPDATE ON impianti.impianti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_riferimenti_catasto_metadati
    BEFORE UPDATE ON impianti.riferimenti_catasto
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_generatori_metadati
    BEFORE UPDATE ON impianti.generatori
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE impianti.impianti            ENABLE ROW LEVEL SECURITY;
ALTER TABLE impianti.riferimenti_catasto ENABLE ROW LEVEL SECURITY;
ALTER TABLE impianti.generatori          ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_impianti ON impianti.impianti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_riferimenti_catasto ON impianti.riferimenti_catasto TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_generatori ON impianti.generatori TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON SCHEMA impianti FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA impianti FROM PUBLIC;

GRANT USAGE ON SCHEMA impianti TO app_api;

GRANT SELECT                         ON impianti.tipi_impianto       TO app_api;
GRANT SELECT                         ON impianti.tipi_generatore     TO app_api;
GRANT SELECT                         ON impianti.combustibili        TO app_api;
GRANT SELECT, INSERT, UPDATE         ON impianti.impianti            TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON impianti.riferimenti_catasto TO app_api;
GRANT SELECT, INSERT, UPDATE         ON impianti.generatori          TO app_api;

-- 0003: schema anagrafica (clienti, contatti, sedi, consensi privacy).
-- I controlli di formato stanno qui; le cifre di controllo di codice fiscale e
-- partita IVA le verifica l'API, dove gli errori diventano messaggi chiari.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- Ricerca per parti di nome (trigrammi). Estensione fidata: la crea il proprietario del database.
CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;

CREATE SCHEMA anagrafica;

COMMENT ON SCHEMA anagrafica IS 'Clienti, contatti, sedi e consensi privacy';

ALTER DEFAULT PRIVILEGES IN SCHEMA anagrafica REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Finalità del trattamento: lookup, modificabile senza toccare il codice
-- ---------------------------------------------------------------------------

CREATE TABLE anagrafica.finalita_privacy (
    finalita_privacy_id uuid        NOT NULL DEFAULT uuidv7(),
    codice              text        NOT NULL,
    nome                text        NOT NULL,
    descrizione         text        NOT NULL,
    attiva              boolean     NOT NULL DEFAULT true,
    creato_il           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_finalita_privacy PRIMARY KEY (finalita_privacy_id),
    CONSTRAINT uq_finalita_privacy_codice UNIQUE (codice),
    CONSTRAINT ck_finalita_privacy_codice CHECK (codice ~ '^[a-z][a-z_]*$')
);

COMMENT ON TABLE anagrafica.finalita_privacy IS 'Finalità per cui si chiede il consenso; scelte dall''utente, non normative';

INSERT INTO anagrafica.finalita_privacy (codice, nome, descrizione) VALUES
    ('promemoria_scadenze', 'Promemoria scadenze',
     'Invio di promemoria per manutenzioni e controlli in scadenza'),
    ('comunicazioni_commerciali', 'Comunicazioni commerciali',
     'Invio di offerte, promozioni e informazioni su nuovi servizi');

-- ---------------------------------------------------------------------------
-- Clienti
-- ---------------------------------------------------------------------------

CREATE TABLE anagrafica.clienti (
    cliente_id     uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id     uuid        NOT NULL,
    tipo           text        NOT NULL,
    denominazione  text,
    nome           text,
    cognome        text,
    codice_fiscale text,
    partita_iva    text,
    pec            text,
    note           text,
    -- Testo su cui si cerca: denominazione oppure cognome e nome, in minuscolo.
    nome_ricerca   text        GENERATED ALWAYS AS (
                       lower(btrim(coalesce(denominazione, '') || ' ' || coalesce(cognome, '') || ' ' || coalesce(nome, '')))
                   ) STORED,
    eliminato_il   timestamptz,
    eliminato_da   uuid,
    creato_il      timestamptz NOT NULL DEFAULT now(),
    creato_da      uuid,
    modificato_il  timestamptz,
    modificato_da  uuid,
    versione       integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_clienti PRIMARY KEY (cliente_id),
    CONSTRAINT uq_clienti_azienda_cliente UNIQUE (azienda_id, cliente_id),
    CONSTRAINT fk_clienti_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_clienti_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_clienti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_clienti_eliminato_da FOREIGN KEY (eliminato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_clienti_tipo CHECK (tipo IN ('privato', 'condominio', 'azienda')),
    -- Un privato ha nome e cognome; condomini e aziende una denominazione.
    CONSTRAINT ck_clienti_nominativo CHECK (
        CASE tipo
            WHEN 'privato' THEN nullif(btrim(nome), '') IS NOT NULL AND nullif(btrim(cognome), '') IS NOT NULL
            ELSE nullif(btrim(denominazione), '') IS NOT NULL
        END
    ),
    CONSTRAINT ck_clienti_codice_fiscale CHECK (codice_fiscale ~ '^([0-9]{11}|[A-Z0-9]{16})$'),
    CONSTRAINT ck_clienti_partita_iva CHECK (partita_iva ~ '^[0-9]{11}$'),
    CONSTRAINT ck_clienti_pec CHECK (pec ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT ck_clienti_eliminazione CHECK (eliminato_da IS NULL OR eliminato_il IS NOT NULL),
    CONSTRAINT ck_clienti_versione CHECK (versione >= 1)
);

-- Una partita IVA appartiene a un solo cliente attivo per azienda.
CREATE UNIQUE INDEX uq_clienti_azienda_partita_iva
    ON anagrafica.clienti (azienda_id, partita_iva)
    WHERE partita_iva IS NOT NULL AND eliminato_il IS NULL;

CREATE INDEX ix_clienti_azienda_codice_fiscale
    ON anagrafica.clienti (azienda_id, codice_fiscale)
    WHERE codice_fiscale IS NOT NULL;

CREATE INDEX ix_clienti_nome_ricerca_trgm
    ON anagrafica.clienti USING gin (nome_ricerca public.gin_trgm_ops);

COMMENT ON TABLE anagrafica.clienti IS 'Clienti: privati, condomini, aziende. Cancellazione logica con eliminato_il';

-- ---------------------------------------------------------------------------
-- Contatti
-- ---------------------------------------------------------------------------

CREATE TABLE anagrafica.contatti (
    contatto_id   uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id    uuid        NOT NULL,
    cliente_id    uuid        NOT NULL,
    tipo          text        NOT NULL,
    valore        text        NOT NULL,
    etichetta     text,
    principale    boolean     NOT NULL DEFAULT false,
    creato_il     timestamptz NOT NULL DEFAULT now(),
    creato_da     uuid,
    modificato_il timestamptz,
    modificato_da uuid,
    versione      integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_contatti PRIMARY KEY (contatto_id),
    CONSTRAINT fk_contatti_clienti FOREIGN KEY (azienda_id, cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_contatti_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_contatti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_contatti_tipo CHECK (tipo IN ('telefono', 'cellulare', 'email')),
    CONSTRAINT ck_contatti_valore CHECK (
        CASE tipo
            WHEN 'email' THEN valore ~ '^[^@\s]+@[^@\s]+$'
            ELSE valore ~ '^\+?[0-9][0-9 ./-]{3,19}$'
        END
    ),
    CONSTRAINT ck_contatti_versione CHECK (versione >= 1)
);

-- Un solo contatto principale per tipo e per cliente.
CREATE UNIQUE INDEX uq_contatti_principale
    ON anagrafica.contatti (cliente_id, tipo)
    WHERE principale;

CREATE INDEX ix_contatti_cliente ON anagrafica.contatti (cliente_id);

-- ---------------------------------------------------------------------------
-- Sedi
-- ---------------------------------------------------------------------------

CREATE TABLE anagrafica.sedi (
    sede_id                uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id             uuid          NOT NULL,
    cliente_id             uuid          NOT NULL,
    descrizione            text,
    indirizzo              text          NOT NULL,
    civico                 text,
    cap                    text          NOT NULL,
    comune                 text          NOT NULL,
    provincia              text          NOT NULL,
    note_accesso           text,
    latitudine             numeric(9, 6),
    longitudine            numeric(9, 6),
    -- Indirizzo senza maiuscole, punteggiatura e spazi doppi: serve a trovare i doppioni.
    indirizzo_normalizzato text          GENERATED ALWAYS AS (
                               regexp_replace(
                                   lower(indirizzo || ' ' || coalesce(civico, '') || ' ' || cap || ' ' || comune),
                                   '[^a-z0-9àèéìòù]+', ' ', 'g')
                           ) STORED,
    eliminata_il           timestamptz,
    eliminata_da           uuid,
    creato_il              timestamptz   NOT NULL DEFAULT now(),
    creato_da              uuid,
    modificato_il          timestamptz,
    modificato_da          uuid,
    versione               integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_sedi PRIMARY KEY (sede_id),
    CONSTRAINT uq_sedi_azienda_sede UNIQUE (azienda_id, sede_id),
    CONSTRAINT fk_sedi_clienti FOREIGN KEY (azienda_id, cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_sedi_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_sedi_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_sedi_eliminata_da FOREIGN KEY (eliminata_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_sedi_indirizzo CHECK (btrim(indirizzo) <> ''),
    CONSTRAINT ck_sedi_comune CHECK (btrim(comune) <> ''),
    CONSTRAINT ck_sedi_cap CHECK (cap ~ '^[0-9]{5}$'),
    CONSTRAINT ck_sedi_provincia CHECK (provincia ~ '^[A-Z]{2}$'),
    CONSTRAINT ck_sedi_coordinate CHECK (
        (latitudine IS NULL) = (longitudine IS NULL)
        AND (latitudine IS NULL OR latitudine BETWEEN -90 AND 90)
        AND (longitudine IS NULL OR longitudine BETWEEN -180 AND 180)
    ),
    CONSTRAINT ck_sedi_eliminazione CHECK (eliminata_da IS NULL OR eliminata_il IS NOT NULL),
    CONSTRAINT ck_sedi_versione CHECK (versione >= 1)
);

CREATE INDEX ix_sedi_cliente ON anagrafica.sedi (cliente_id);
CREATE INDEX ix_sedi_azienda_indirizzo ON anagrafica.sedi (azienda_id, indirizzo_normalizzato);

COMMENT ON TABLE anagrafica.sedi IS 'Indirizzi dei clienti; gli impianti si collegano alla sede';

-- ---------------------------------------------------------------------------
-- Consensi privacy: storico che non si modifica
-- ---------------------------------------------------------------------------

-- Ogni riga è una dichiarazione del cliente; il consenso valido è l'ultima per finalità.
CREATE TABLE anagrafica.consensi_privacy (
    consenso_id         uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id          uuid        NOT NULL,
    cliente_id          uuid        NOT NULL,
    finalita_privacy_id uuid        NOT NULL,
    concesso            boolean     NOT NULL,
    fonte               text        NOT NULL,
    note                text,
    registrato_il       timestamptz NOT NULL DEFAULT now(),
    registrato_da       uuid,
    CONSTRAINT pk_consensi_privacy PRIMARY KEY (consenso_id),
    CONSTRAINT fk_consensi_privacy_clienti FOREIGN KEY (azienda_id, cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_consensi_privacy_finalita FOREIGN KEY (finalita_privacy_id) REFERENCES anagrafica.finalita_privacy (finalita_privacy_id),
    CONSTRAINT fk_consensi_privacy_registrato_da FOREIGN KEY (registrato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_consensi_privacy_fonte CHECK (fonte IN ('modulo_firmato', 'email', 'telefono', 'di_persona', 'altro'))
);

CREATE INDEX ix_consensi_privacy_cliente_finalita
    ON anagrafica.consensi_privacy (cliente_id, finalita_privacy_id, registrato_il DESC, consenso_id DESC);

COMMENT ON TABLE anagrafica.consensi_privacy IS 'Storico dei consensi: solo inserimento';

-- Consenso valido per cliente e finalità. security_invoker: la vista rispetta la RLS di chi la legge.
CREATE VIEW anagrafica.v_consensi_correnti
WITH (security_invoker = true) AS
SELECT DISTINCT ON (c.cliente_id, c.finalita_privacy_id)
       c.azienda_id, c.cliente_id, c.finalita_privacy_id, f.codice AS finalita,
       c.concesso, c.fonte, c.registrato_il, c.registrato_da
FROM anagrafica.consensi_privacy c
JOIN anagrafica.finalita_privacy f ON f.finalita_privacy_id = c.finalita_privacy_id
ORDER BY c.cliente_id, c.finalita_privacy_id, c.registrato_il DESC, c.consenso_id DESC;

-- ---------------------------------------------------------------------------
-- Funzioni e trigger
-- ---------------------------------------------------------------------------

-- Blocca UPDATE e DELETE sulle tabelle che sono uno storico.
CREATE FUNCTION anagrafica.blocca_modifica_storico()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '%.% è uno storico: % non consentito', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
        USING ERRCODE = 'insufficient_privilege';
END
$$;

-- Cancellazione logica di un cliente e delle sue sedi, con controllo di versione.
-- Restituisce false se il cliente non esiste, è già eliminato o la versione non coincide.
CREATE FUNCTION anagrafica.elimina_cliente(p_cliente_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE anagrafica.clienti
    SET eliminato_il = now(), eliminato_da = sec.utente_corrente()
    WHERE cliente_id = p_cliente_id
      AND versione = p_versione
      AND eliminato_il IS NULL;

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    UPDATE anagrafica.sedi
    SET eliminata_il = now(), eliminata_da = sec.utente_corrente()
    WHERE cliente_id = p_cliente_id
      AND eliminata_il IS NULL;

    RETURN true;
END
$$;

COMMENT ON FUNCTION anagrafica.elimina_cliente(uuid, integer) IS 'Cancellazione logica di cliente e sedi con concorrenza ottimistica';

CREATE TRIGGER tr_clienti_metadati
    BEFORE UPDATE ON anagrafica.clienti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_contatti_metadati
    BEFORE UPDATE ON anagrafica.contatti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_sedi_metadati
    BEFORE UPDATE ON anagrafica.sedi
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_consensi_privacy_storico
    BEFORE UPDATE OR DELETE ON anagrafica.consensi_privacy
    FOR EACH ROW EXECUTE FUNCTION anagrafica.blocca_modifica_storico();

CREATE TRIGGER tr_consensi_privacy_no_truncate
    BEFORE TRUNCATE ON anagrafica.consensi_privacy
    FOR EACH STATEMENT EXECUTE FUNCTION anagrafica.blocca_modifica_storico();

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE anagrafica.clienti          ENABLE ROW LEVEL SECURITY;
ALTER TABLE anagrafica.contatti         ENABLE ROW LEVEL SECURITY;
ALTER TABLE anagrafica.sedi             ENABLE ROW LEVEL SECURITY;
ALTER TABLE anagrafica.consensi_privacy ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_clienti ON anagrafica.clienti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_contatti ON anagrafica.contatti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_sedi ON anagrafica.sedi TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_consensi_privacy ON anagrafica.consensi_privacy TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON SCHEMA anagrafica FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA anagrafica FROM PUBLIC;

GRANT USAGE ON SCHEMA anagrafica TO app_api;

GRANT SELECT                         ON anagrafica.finalita_privacy    TO app_api;
GRANT SELECT, INSERT, UPDATE         ON anagrafica.clienti             TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON anagrafica.contatti            TO app_api;
GRANT SELECT, INSERT, UPDATE         ON anagrafica.sedi                TO app_api;
-- Consensi: solo inserimento e lettura.
GRANT SELECT, INSERT                 ON anagrafica.consensi_privacy    TO app_api;
GRANT SELECT                         ON anagrafica.v_consensi_correnti TO app_api;

REVOKE ALL ON FUNCTION
    anagrafica.blocca_modifica_storico(),
    anagrafica.elimina_cliente(uuid, integer)
FROM PUBLIC;

GRANT EXECUTE ON FUNCTION anagrafica.elimina_cliente(uuid, integer) TO app_api;

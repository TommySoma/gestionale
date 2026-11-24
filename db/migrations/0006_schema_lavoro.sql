-- 0006: numerazione dei documenti (contatori) e schema lavoro (interventi e righe).
-- Scadenze e richiami arrivano con M3, il collegamento ai preventivi anche.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- Serve al vincolo di esclusione: confronta uuid con = dentro un indice GiST.
CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA public;

-- ---------------------------------------------------------------------------
-- Numerazione progressiva senza buchi (schema commerciale, come da piano)
-- ---------------------------------------------------------------------------

CREATE SCHEMA commerciale;

COMMENT ON SCHEMA commerciale IS 'Listino, preventivi e numerazione dei documenti';

CREATE TABLE commerciale.contatori (
    azienda_id     uuid        NOT NULL,
    tipo_documento text        NOT NULL,
    anno           integer     NOT NULL,
    ultimo_numero  integer     NOT NULL,
    creato_il      timestamptz NOT NULL DEFAULT now(),
    modificato_il  timestamptz,
    CONSTRAINT pk_contatori PRIMARY KEY (azienda_id, tipo_documento, anno),
    CONSTRAINT fk_contatori_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT ck_contatori_tipo CHECK (tipo_documento IN ('intervento', 'preventivo', 'dichiarazione_conformita')),
    CONSTRAINT ck_contatori_anno CHECK (anno BETWEEN 2000 AND 2100),
    CONSTRAINT ck_contatori_ultimo_numero CHECK (ultimo_numero >= 1)
);

COMMENT ON TABLE commerciale.contatori IS 'Ultimo numero assegnato per azienda, tipo di documento e anno';

-- Il numero si prende nella stessa transazione del documento: la riga del
-- contatore resta bloccata fino al COMMIT, quindi due transazioni concorrenti
-- si mettono in fila, e se il documento non viene salvato il numero torna
-- disponibile. È questo che evita i buchi.
CREATE FUNCTION commerciale.prossimo_numero(p_azienda_id uuid, p_tipo_documento text, p_anno integer)
RETURNS integer
LANGUAGE sql
AS $$
    INSERT INTO commerciale.contatori AS c (azienda_id, tipo_documento, anno, ultimo_numero)
    VALUES (p_azienda_id, p_tipo_documento, p_anno, 1)
    ON CONFLICT (azienda_id, tipo_documento, anno)
    DO UPDATE SET ultimo_numero = c.ultimo_numero + 1, modificato_il = now()
    RETURNING ultimo_numero
$$;

COMMENT ON FUNCTION commerciale.prossimo_numero(uuid, text, integer) IS 'Numero successivo, atomico e senza buchi; da chiamare nella transazione del documento';

-- ---------------------------------------------------------------------------
-- Schema lavoro
-- ---------------------------------------------------------------------------

CREATE SCHEMA lavoro;

COMMENT ON SCHEMA lavoro IS 'Interventi, calendario e, da M3, scadenze e richiami';

-- Tipi, stati ed esiti sono interni all'impresa, non normativi: vincoli CHECK.
-- Stati: da_pianificare (senza data) -> pianificato -> in_corso -> completato;
-- annullato da qualsiasi stato aperto. Completato e annullato sono finali.
CREATE TABLE lavoro.interventi (
    intervento_id        uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id           uuid        NOT NULL,
    anno                 integer     NOT NULL,
    numero               integer     NOT NULL,
    impianto_id          uuid        NOT NULL,
    generatore_id        uuid,
    tipo                 text        NOT NULL,
    stato                text        NOT NULL DEFAULT 'da_pianificare',
    finestra             tstzrange,
    tecnico_utente_id    uuid,
    descrizione          text,
    esito                text,
    note_chiusura        text,
    chiuso_il            timestamptz,
    chiuso_da            uuid,
    annullato_il         timestamptz,
    annullato_da         uuid,
    motivo_annullamento  text,
    creato_il            timestamptz NOT NULL DEFAULT now(),
    creato_da            uuid,
    modificato_il        timestamptz,
    modificato_da        uuid,
    versione             integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_interventi PRIMARY KEY (intervento_id),
    CONSTRAINT uq_interventi_azienda_intervento UNIQUE (azienda_id, intervento_id),
    CONSTRAINT uq_interventi_numero UNIQUE (azienda_id, anno, numero),
    -- Chiavi composte: impianto, generatore e tecnico sono per forza della stessa azienda.
    CONSTRAINT fk_interventi_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_interventi_generatori FOREIGN KEY (azienda_id, generatore_id) REFERENCES impianti.generatori (azienda_id, generatore_id),
    CONSTRAINT fk_interventi_tecnico FOREIGN KEY (azienda_id, tecnico_utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT fk_interventi_chiuso_da FOREIGN KEY (chiuso_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_interventi_annullato_da FOREIGN KEY (annullato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_interventi_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_interventi_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_interventi_tipo CHECK (tipo IN ('manutenzione', 'riparazione', 'installazione', 'sopralluogo', 'altro')),
    CONSTRAINT ck_interventi_stato CHECK (stato IN ('da_pianificare', 'pianificato', 'in_corso', 'completato', 'annullato')),
    CONSTRAINT ck_interventi_esito CHECK (esito IN ('risolto', 'da_completare', 'non_risolto')),
    CONSTRAINT ck_interventi_numero CHECK (numero >= 1),
    CONSTRAINT ck_interventi_anno CHECK (anno BETWEEN 2000 AND 2100),
    -- Finestra chiusa a sinistra e aperta a destra, con inizio e fine: [10:00, 12:00).
    -- Così un intervento che finisce alle 12 non si sovrappone a uno che inizia alle 12.
    CONSTRAINT ck_interventi_finestra CHECK (
        finestra IS NULL OR (
            NOT isempty(finestra) AND NOT lower_inf(finestra) AND NOT upper_inf(finestra)
            AND lower_inc(finestra) AND NOT upper_inc(finestra)
        )
    ),
    CONSTRAINT ck_interventi_da_pianificare CHECK (stato <> 'da_pianificare' OR finestra IS NULL),
    -- Ciò che è in calendario ha data e tecnico.
    CONSTRAINT ck_interventi_pianificato CHECK (
        stato NOT IN ('pianificato', 'in_corso', 'completato')
        OR (finestra IS NOT NULL AND tecnico_utente_id IS NOT NULL)
    ),
    CONSTRAINT ck_interventi_chiusura CHECK (
        (stato = 'completato') = (chiuso_il IS NOT NULL AND esito IS NOT NULL)
    ),
    CONSTRAINT ck_interventi_annullamento CHECK (
        (stato = 'annullato') = (annullato_il IS NOT NULL AND nullif(btrim(motivo_annullamento), '') IS NOT NULL)
    ),
    CONSTRAINT ck_interventi_versione CHECK (versione >= 1),
    -- Un tecnico non ha due interventi sovrapposti. Gli annullati e quelli
    -- ancora da pianificare non occupano il calendario.
    CONSTRAINT ex_interventi_tecnico_sovrapposti EXCLUDE USING gist (
        tecnico_utente_id WITH =,
        finestra WITH &&
    ) WHERE (stato IN ('pianificato', 'in_corso', 'completato'))
);

-- Calendario: interventi dell'azienda in un intervallo di date.
CREATE INDEX ix_interventi_azienda_finestra ON lavoro.interventi USING gist (azienda_id, finestra)
    WHERE finestra IS NOT NULL;
-- Timeline della scheda impianto.
CREATE INDEX ix_interventi_impianto ON lavoro.interventi (impianto_id, creato_il DESC);
-- Elenco "da pianificare".
CREATE INDEX ix_interventi_da_pianificare ON lavoro.interventi (azienda_id, creato_il)
    WHERE stato = 'da_pianificare';

COMMENT ON TABLE lavoro.interventi IS 'Interventi su un impianto; numerati per azienda e anno, si annullano e non si cancellano';
COMMENT ON COLUMN lavoro.interventi.finestra IS 'Intervallo pianificato [inizio, fine)';

-- Righe: materiali e manodopera. Codice, descrizione e prezzo sono copiati al
-- momento (dal listino, da M3): una modifica del listino non cambia lo storico.
CREATE TABLE lavoro.interventi_righe (
    intervento_riga_id uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id         uuid          NOT NULL,
    intervento_id      uuid          NOT NULL,
    posizione          integer       NOT NULL,
    tipo               text          NOT NULL,
    codice             text,
    descrizione        text          NOT NULL,
    unita              text          NOT NULL,
    quantita           numeric(10, 3) NOT NULL,
    prezzo_unitario    numeric(12, 2) NOT NULL DEFAULT 0,
    sconto_percentuale numeric(5, 2)  NOT NULL DEFAULT 0,
    aliquota_iva       numeric(5, 2),
    importo            numeric(12, 2) GENERATED ALWAYS AS (
        round(quantita * prezzo_unitario * (1 - sconto_percentuale / 100), 2)
    ) STORED,
    creato_il          timestamptz   NOT NULL DEFAULT now(),
    creato_da          uuid,
    modificato_il      timestamptz,
    modificato_da      uuid,
    versione           integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_interventi_righe PRIMARY KEY (intervento_riga_id),
    CONSTRAINT uq_interventi_righe_posizione UNIQUE (intervento_id, posizione),
    CONSTRAINT fk_interventi_righe_interventi FOREIGN KEY (azienda_id, intervento_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_interventi_righe_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_interventi_righe_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_interventi_righe_tipo CHECK (tipo IN ('materiale', 'manodopera')),
    -- La manodopera si conta in ore.
    CONSTRAINT ck_interventi_righe_manodopera CHECK (tipo <> 'manodopera' OR unita = 'h'),
    CONSTRAINT ck_interventi_righe_descrizione CHECK (btrim(descrizione) <> ''),
    CONSTRAINT ck_interventi_righe_unita CHECK (btrim(unita) <> ''),
    CONSTRAINT ck_interventi_righe_posizione CHECK (posizione >= 1),
    CONSTRAINT ck_interventi_righe_quantita CHECK (quantita > 0),
    CONSTRAINT ck_interventi_righe_prezzo CHECK (prezzo_unitario >= 0),
    CONSTRAINT ck_interventi_righe_sconto CHECK (sconto_percentuale BETWEEN 0 AND 100),
    CONSTRAINT ck_interventi_righe_iva CHECK (aliquota_iva BETWEEN 0 AND 100),
    CONSTRAINT ck_interventi_righe_versione CHECK (versione >= 1)
);

COMMENT ON TABLE lavoro.interventi_righe IS 'Materiali e ore di un intervento, con prezzi copiati al momento';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

-- Anno e numero li assegna il database, una volta sola.
CREATE FUNCTION lavoro.numera_intervento()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.numero IS NOT NULL THEN
        RAISE EXCEPTION 'il numero dell''intervento lo assegna il database'
            USING ERRCODE = 'check_violation';
    END IF;

    NEW.anno   := coalesce(NEW.anno, extract(year FROM now() AT TIME ZONE 'Europe/Rome')::integer);
    NEW.numero := commerciale.prossimo_numero(NEW.azienda_id, 'intervento', NEW.anno);
    RETURN NEW;
END
$$;

-- Un intervento completato o annullato non cambia più; anno e numero mai.
CREATE FUNCTION lavoro.controlla_modifica_intervento()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.stato IN ('completato', 'annullato') THEN
        RAISE EXCEPTION 'l''intervento %/% è % e non si modifica', OLD.numero, OLD.anno, OLD.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF NEW.anno <> OLD.anno OR NEW.numero <> OLD.numero OR NEW.azienda_id <> OLD.azienda_id THEN
        RAISE EXCEPTION 'anno, numero e azienda di un intervento non si modificano'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

-- Le righe si toccano solo finché l'intervento è aperto.
CREATE FUNCTION lavoro.controlla_modifica_righe()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_intervento_id uuid := CASE TG_OP WHEN 'DELETE' THEN OLD.intervento_id ELSE NEW.intervento_id END;
    v_stato         text;
BEGIN
    SELECT stato INTO v_stato FROM lavoro.interventi WHERE intervento_id = v_intervento_id;

    IF v_stato IN ('completato', 'annullato') THEN
        RAISE EXCEPTION 'le righe di un intervento % non si modificano', v_stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF TG_OP = 'UPDATE' AND NEW.intervento_id <> OLD.intervento_id THEN
        RAISE EXCEPTION 'una riga non si sposta su un altro intervento'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN CASE TG_OP WHEN 'DELETE' THEN OLD ELSE NEW END;
END
$$;

CREATE TRIGGER tr_interventi_numero
    BEFORE INSERT ON lavoro.interventi
    FOR EACH ROW EXECUTE FUNCTION lavoro.numera_intervento();

CREATE TRIGGER tr_interventi_controlli
    BEFORE UPDATE ON lavoro.interventi
    FOR EACH ROW EXECUTE FUNCTION lavoro.controlla_modifica_intervento();

CREATE TRIGGER tr_interventi_metadati
    BEFORE UPDATE ON lavoro.interventi
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_interventi_righe_controlli
    BEFORE INSERT OR UPDATE OR DELETE ON lavoro.interventi_righe
    FOR EACH ROW EXECUTE FUNCTION lavoro.controlla_modifica_righe();

CREATE TRIGGER tr_interventi_righe_metadati
    BEFORE UPDATE ON lavoro.interventi_righe
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

-- ---------------------------------------------------------------------------
-- Chiusura dell'intervento
-- ---------------------------------------------------------------------------

-- In una transazione: sostituisce le righe, registra esito e note, chiude.
-- p_righe è un array JSON di oggetti con tipo, codice, descrizione, unita,
-- quantita, prezzo_unitario, sconto_percentuale, aliquota_iva; la posizione
-- segue l'ordine dell'array. Restituisce false se l'intervento non esiste o la
-- versione non coincide. Da M3 chiuderà anche la scadenza e calcolerà la prossima.
CREATE FUNCTION lavoro.chiudi_intervento(
    p_intervento_id uuid,
    p_versione      integer,
    p_esito         text,
    p_note_chiusura text,
    p_righe         jsonb
)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_intervento lavoro.interventi%ROWTYPE;
BEGIN
    SELECT * INTO v_intervento
    FROM lavoro.interventi
    WHERE intervento_id = p_intervento_id
    FOR UPDATE;

    IF NOT FOUND OR v_intervento.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_intervento.stato NOT IN ('pianificato', 'in_corso') THEN
        RAISE EXCEPTION 'si chiude solo un intervento pianificato o in corso, non %', v_intervento.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF p_righe IS NULL OR jsonb_typeof(p_righe) <> 'array' THEN
        RAISE EXCEPTION 'le righe devono essere un array JSON'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    DELETE FROM lavoro.interventi_righe WHERE intervento_id = p_intervento_id;

    INSERT INTO lavoro.interventi_righe (
        azienda_id, intervento_id, posizione, tipo, codice, descrizione, unita,
        quantita, prezzo_unitario, sconto_percentuale, aliquota_iva, creato_da
    )
    SELECT v_intervento.azienda_id, p_intervento_id, r.posizione::integer,
           r.riga ->> 'tipo',
           r.riga ->> 'codice',
           r.riga ->> 'descrizione',
           r.riga ->> 'unita',
           (r.riga ->> 'quantita')::numeric,
           coalesce((r.riga ->> 'prezzo_unitario')::numeric, 0),
           coalesce((r.riga ->> 'sconto_percentuale')::numeric, 0),
           (r.riga ->> 'aliquota_iva')::numeric,
           sec.utente_corrente()
    FROM jsonb_array_elements(p_righe) WITH ORDINALITY AS r (riga, posizione);

    UPDATE lavoro.interventi
    SET stato         = 'completato',
        esito         = p_esito,
        note_chiusura = p_note_chiusura,
        chiuso_il     = now(),
        chiuso_da     = sec.utente_corrente(),
        modificato_da = sec.utente_corrente()
    WHERE intervento_id = p_intervento_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb) IS 'Chiude un intervento con esito e righe, con concorrenza ottimistica';

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE commerciale.contatori    ENABLE ROW LEVEL SECURITY;
ALTER TABLE lavoro.interventi        ENABLE ROW LEVEL SECURITY;
ALTER TABLE lavoro.interventi_righe  ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_contatori ON commerciale.contatori TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_interventi ON lavoro.interventi TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_interventi_righe ON lavoro.interventi_righe TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON SCHEMA commerciale FROM PUBLIC;
REVOKE ALL ON SCHEMA lavoro FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA commerciale FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA lavoro FROM PUBLIC;
-- Le impostazioni predefinite per schema non tolgono l'EXECUTE a PUBLIC
-- (si sommano a quelle globali): lo si toglie qui, funzione per funzione.
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA commerciale FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA lavoro FROM PUBLIC;

GRANT USAGE ON SCHEMA commerciale TO app_api;
GRANT USAGE ON SCHEMA lavoro TO app_api;

-- Niente DELETE sugli interventi: si annullano. Le righe si cancellano finché
-- l'intervento è aperto, e lo controlla il trigger.
GRANT SELECT, INSERT, UPDATE         ON commerciale.contatori   TO app_api;
GRANT SELECT, INSERT, UPDATE         ON lavoro.interventi       TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON lavoro.interventi_righe TO app_api;

GRANT EXECUTE ON FUNCTION commerciale.prossimo_numero(uuid, text, integer) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb) TO app_api;

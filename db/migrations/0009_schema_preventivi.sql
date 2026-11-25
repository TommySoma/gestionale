-- 0009: listino prezzi con storico e preventivi (schema commerciale).
-- Decisioni del 25/11/2026: il numero del preventivo si assegna all'emissione
-- (la bozza non ha numero e si può eliminare; dopo si annulla); stati bozza,
-- emesso, accettato, rifiutato, annullato, con la scadenza della validità
-- calcolata dalla data e non salvata come stato; un preventivo accettato
-- genera un intervento da pianificare con le righe copiate; i prezzi del
-- listino hanno uno storico con data di validità.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- ---------------------------------------------------------------------------
-- Listino
-- ---------------------------------------------------------------------------

CREATE TABLE commerciale.listino_voci (
    voce_id       uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id    uuid          NOT NULL,
    codice        text          NOT NULL,
    descrizione   text          NOT NULL,
    tipo          text          NOT NULL,
    unita         text          NOT NULL,
    aliquota_iva  numeric(5, 2),
    attiva        boolean       NOT NULL DEFAULT true,
    note          text,
    creato_il     timestamptz   NOT NULL DEFAULT now(),
    creato_da     uuid,
    modificato_il timestamptz,
    modificato_da uuid,
    versione      integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_listino_voci PRIMARY KEY (voce_id),
    CONSTRAINT uq_listino_voci_azienda_voce UNIQUE (azienda_id, voce_id),
    CONSTRAINT fk_listino_voci_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_listino_voci_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_listino_voci_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_listino_voci_codice CHECK (codice ~ '^\S+$'),
    CONSTRAINT ck_listino_voci_descrizione CHECK (btrim(descrizione) <> ''),
    CONSTRAINT ck_listino_voci_tipo CHECK (tipo IN ('materiale', 'manodopera')),
    -- La manodopera si conta in ore, come nelle righe degli interventi.
    CONSTRAINT ck_listino_voci_manodopera CHECK (tipo <> 'manodopera' OR unita = 'h'),
    CONSTRAINT ck_listino_voci_unita CHECK (btrim(unita) <> ''),
    CONSTRAINT ck_listino_voci_iva CHECK (aliquota_iva BETWEEN 0 AND 100),
    CONSTRAINT ck_listino_voci_versione CHECK (versione >= 1)
);

-- Codice unico per azienda, senza distinguere maiuscole e minuscole.
CREATE UNIQUE INDEX uq_listino_voci_codice ON commerciale.listino_voci (azienda_id, lower(codice));

COMMENT ON TABLE commerciale.listino_voci IS 'Voci del listino: materiali e manodopera; si disattivano, non si cancellano';

-- Ogni cambio di prezzo è una riga nuova: il prezzo vale da valido_dal fino al
-- successivo. Preventivi e interventi copiano comunque il prezzo nelle righe.
CREATE TABLE commerciale.listino_prezzi (
    prezzo_id     uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id    uuid          NOT NULL,
    voce_id       uuid          NOT NULL,
    prezzo        numeric(12, 2) NOT NULL,
    valido_dal    date          NOT NULL,
    creato_il     timestamptz   NOT NULL DEFAULT now(),
    creato_da     uuid,
    CONSTRAINT pk_listino_prezzi PRIMARY KEY (prezzo_id),
    CONSTRAINT uq_listino_prezzi_voce_data UNIQUE (voce_id, valido_dal),
    CONSTRAINT fk_listino_prezzi_voci FOREIGN KEY (azienda_id, voce_id) REFERENCES commerciale.listino_voci (azienda_id, voce_id),
    CONSTRAINT fk_listino_prezzi_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_listino_prezzi_prezzo CHECK (prezzo >= 0)
);

COMMENT ON TABLE commerciale.listino_prezzi IS 'Storico dei prezzi del listino: solo aggiunta';

-- Prezzo di una voce in una data: quello con la validità più recente non successiva.
CREATE FUNCTION commerciale.prezzo_voce(p_voce_id uuid, p_data date)
RETURNS numeric
LANGUAGE sql
STABLE
AS $$
    SELECT prezzo
    FROM commerciale.listino_prezzi
    WHERE voce_id = p_voce_id AND valido_dal <= p_data
    ORDER BY valido_dal DESC
    LIMIT 1
$$;

COMMENT ON FUNCTION commerciale.prezzo_voce(uuid, date) IS 'Prezzo valido alla data; NULL se la voce non aveva ancora un prezzo';

-- ---------------------------------------------------------------------------
-- Preventivi
-- ---------------------------------------------------------------------------

-- Stati: bozza (senza numero) -> emesso (numerato) -> accettato o rifiutato;
-- annullato da emesso, o da accettato finché non ha generato l'intervento.
-- Rifiutato e annullato sono finali. "Scaduto" non è uno stato: è un
-- preventivo emesso oltre scade_il.
CREATE TABLE commerciale.preventivi (
    preventivo_id       uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id          uuid          NOT NULL,
    anno                integer,
    numero              integer,
    cliente_id          uuid          NOT NULL,
    sede_id             uuid,
    impianto_id         uuid,
    stato               text          NOT NULL DEFAULT 'bozza',
    oggetto             text          NOT NULL,
    note                text,
    validita_giorni     integer       NOT NULL DEFAULT 30,
    emesso_il           date,
    scade_il            date          GENERATED ALWAYS AS (emesso_il + validita_giorni) STORED,
    imponibile          numeric(12, 2) NOT NULL DEFAULT 0,
    iva                 numeric(12, 2) NOT NULL DEFAULT 0,
    totale              numeric(12, 2) NOT NULL DEFAULT 0,
    accettato_il        timestamptz,
    rifiutato_il        timestamptz,
    annullato_il        timestamptz,
    motivo_annullamento text,
    intervento_id       uuid,
    creato_il           timestamptz   NOT NULL DEFAULT now(),
    creato_da           uuid,
    modificato_il       timestamptz,
    modificato_da       uuid,
    versione            integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_preventivi PRIMARY KEY (preventivo_id),
    CONSTRAINT uq_preventivi_azienda_preventivo UNIQUE (azienda_id, preventivo_id),
    CONSTRAINT uq_preventivi_numero UNIQUE (azienda_id, anno, numero),
    -- Un preventivo genera al massimo un intervento, e un intervento viene da un solo preventivo.
    CONSTRAINT uq_preventivi_intervento UNIQUE (intervento_id),
    CONSTRAINT fk_preventivi_clienti FOREIGN KEY (azienda_id, cliente_id) REFERENCES anagrafica.clienti (azienda_id, cliente_id),
    CONSTRAINT fk_preventivi_sedi FOREIGN KEY (azienda_id, sede_id) REFERENCES anagrafica.sedi (azienda_id, sede_id),
    CONSTRAINT fk_preventivi_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_preventivi_interventi FOREIGN KEY (azienda_id, intervento_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_preventivi_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_preventivi_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_preventivi_stato CHECK (stato IN ('bozza', 'emesso', 'accettato', 'rifiutato', 'annullato')),
    CONSTRAINT ck_preventivi_oggetto CHECK (btrim(oggetto) <> ''),
    CONSTRAINT ck_preventivi_validita CHECK (validita_giorni BETWEEN 1 AND 365),
    -- La bozza non ha numero né data di emissione; tutti gli altri sì.
    CONSTRAINT ck_preventivi_numerazione CHECK (
        (stato = 'bozza') = (anno IS NULL AND numero IS NULL AND emesso_il IS NULL)
        AND (anno IS NULL) = (numero IS NULL) AND (numero IS NULL) = (emesso_il IS NULL)
    ),
    CONSTRAINT ck_preventivi_numero CHECK (numero >= 1),
    CONSTRAINT ck_preventivi_anno CHECK (anno BETWEEN 2000 AND 2100),
    -- Un accettato poi annullato conserva la data di accettazione.
    CONSTRAINT ck_preventivi_accettazione CHECK (
        (stato <> 'accettato' OR accettato_il IS NOT NULL) AND (accettato_il IS NULL OR stato IN ('accettato', 'annullato'))
    ),
    CONSTRAINT ck_preventivi_rifiuto CHECK ((stato = 'rifiutato') = (rifiutato_il IS NOT NULL)),
    CONSTRAINT ck_preventivi_annullamento CHECK (
        (stato = 'annullato') = (annullato_il IS NOT NULL AND nullif(btrim(motivo_annullamento), '') IS NOT NULL)
    ),
    -- Solo un preventivo accettato genera l'intervento, e ne serve l'impianto.
    CONSTRAINT ck_preventivi_intervento CHECK (intervento_id IS NULL OR (stato = 'accettato' AND impianto_id IS NOT NULL)),
    CONSTRAINT ck_preventivi_totali CHECK (imponibile >= 0 AND iva >= 0 AND totale = imponibile + iva),
    CONSTRAINT ck_preventivi_versione CHECK (versione >= 1)
);

CREATE INDEX ix_preventivi_azienda_creato ON commerciale.preventivi (azienda_id, creato_il DESC);
CREATE INDEX ix_preventivi_cliente ON commerciale.preventivi (cliente_id);
CREATE INDEX ix_preventivi_impianto ON commerciale.preventivi (impianto_id) WHERE impianto_id IS NOT NULL;

COMMENT ON TABLE commerciale.preventivi IS 'Preventivi: numerati all''emissione, modificabili solo in bozza, si annullano e non si cancellano dopo l''emissione';
COMMENT ON COLUMN commerciale.preventivi.scade_il IS 'Fine della validità; un preventivo emesso oltre questa data è scaduto';

CREATE TABLE commerciale.preventivi_righe (
    preventivo_riga_id uuid           NOT NULL DEFAULT uuidv7(),
    azienda_id         uuid           NOT NULL,
    preventivo_id      uuid           NOT NULL,
    posizione          integer        NOT NULL,
    voce_id            uuid,
    tipo               text           NOT NULL,
    codice             text,
    descrizione        text           NOT NULL,
    unita              text           NOT NULL,
    quantita           numeric(10, 3) NOT NULL,
    prezzo_unitario    numeric(12, 2) NOT NULL DEFAULT 0,
    sconto_percentuale numeric(5, 2)  NOT NULL DEFAULT 0,
    aliquota_iva       numeric(5, 2),
    importo            numeric(12, 2) GENERATED ALWAYS AS (
        round(quantita * prezzo_unitario * (1 - sconto_percentuale / 100), 2)
    ) STORED,
    creato_il          timestamptz    NOT NULL DEFAULT now(),
    creato_da          uuid,
    modificato_il      timestamptz,
    modificato_da      uuid,
    versione           integer        NOT NULL DEFAULT 1,
    CONSTRAINT pk_preventivi_righe PRIMARY KEY (preventivo_riga_id),
    CONSTRAINT uq_preventivi_righe_posizione UNIQUE (preventivo_id, posizione),
    -- Eliminando una bozza se ne vanno anche le righe.
    CONSTRAINT fk_preventivi_righe_preventivi FOREIGN KEY (azienda_id, preventivo_id)
        REFERENCES commerciale.preventivi (azienda_id, preventivo_id) ON DELETE CASCADE,
    CONSTRAINT fk_preventivi_righe_voci FOREIGN KEY (azienda_id, voce_id) REFERENCES commerciale.listino_voci (azienda_id, voce_id),
    CONSTRAINT fk_preventivi_righe_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_preventivi_righe_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_preventivi_righe_tipo CHECK (tipo IN ('materiale', 'manodopera')),
    CONSTRAINT ck_preventivi_righe_manodopera CHECK (tipo <> 'manodopera' OR unita = 'h'),
    CONSTRAINT ck_preventivi_righe_descrizione CHECK (btrim(descrizione) <> ''),
    CONSTRAINT ck_preventivi_righe_unita CHECK (btrim(unita) <> ''),
    CONSTRAINT ck_preventivi_righe_posizione CHECK (posizione >= 1),
    CONSTRAINT ck_preventivi_righe_quantita CHECK (quantita > 0),
    CONSTRAINT ck_preventivi_righe_prezzo CHECK (prezzo_unitario >= 0),
    CONSTRAINT ck_preventivi_righe_sconto CHECK (sconto_percentuale BETWEEN 0 AND 100),
    CONSTRAINT ck_preventivi_righe_iva CHECK (aliquota_iva BETWEEN 0 AND 100),
    CONSTRAINT ck_preventivi_righe_versione CHECK (versione >= 1)
);

COMMENT ON TABLE commerciale.preventivi_righe IS 'Righe di un preventivo, con prezzi copiati dal listino; modificabili solo in bozza';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

-- Sede e impianto devono essere del cliente del preventivo (l'impianto anche della sede, se indicata).
-- Dopo la bozza cambiano solo lo stato e i suoi dati; numero e anno mai.
CREATE FUNCTION commerciale.controlla_preventivo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.sede_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM anagrafica.sedi WHERE sede_id = NEW.sede_id AND cliente_id = NEW.cliente_id
    ) THEN
        RAISE EXCEPTION 'la sede non è del cliente del preventivo'
            USING ERRCODE = 'check_violation';
    END IF;

    IF NEW.impianto_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM impianti.impianti i
        JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
        WHERE i.impianto_id = NEW.impianto_id
          AND s.cliente_id = NEW.cliente_id
          AND (NEW.sede_id IS NULL OR i.sede_id = NEW.sede_id)
    ) THEN
        RAISE EXCEPTION 'l''impianto non è del cliente o della sede del preventivo'
            USING ERRCODE = 'check_violation';
    END IF;

    IF TG_OP = 'UPDATE' THEN
        IF OLD.stato IN ('rifiutato', 'annullato') THEN
            RAISE EXCEPTION 'il preventivo %/% è % e non si modifica', OLD.numero, OLD.anno, OLD.stato
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;

        IF NEW.azienda_id <> OLD.azienda_id
           OR (OLD.numero IS NOT NULL AND (NEW.numero IS DISTINCT FROM OLD.numero OR NEW.anno IS DISTINCT FROM OLD.anno OR NEW.emesso_il IS DISTINCT FROM OLD.emesso_il)) THEN
            RAISE EXCEPTION 'azienda, numero, anno e data di emissione di un preventivo non si modificano'
                USING ERRCODE = 'check_violation';
        END IF;

        -- Il contenuto si tocca solo in bozza; i totali li aggiornano le righe.
        IF OLD.stato <> 'bozza' AND (
            NEW.cliente_id, NEW.sede_id, NEW.impianto_id, NEW.oggetto, NEW.note, NEW.validita_giorni,
            NEW.imponibile, NEW.iva, NEW.totale
        ) IS DISTINCT FROM (
            OLD.cliente_id, OLD.sede_id, OLD.impianto_id, OLD.oggetto, OLD.note, OLD.validita_giorni,
            OLD.imponibile, OLD.iva, OLD.totale
        ) THEN
            RAISE EXCEPTION 'un preventivo emesso non si modifica: annullalo e creane un altro'
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;

        IF NEW.stato <> OLD.stato AND NOT (
            (OLD.stato = 'bozza' AND NEW.stato = 'emesso')
            OR (OLD.stato = 'emesso' AND NEW.stato IN ('accettato', 'rifiutato', 'annullato'))
            OR (OLD.stato = 'accettato' AND NEW.stato = 'annullato' AND OLD.intervento_id IS NULL)
        ) THEN
            RAISE EXCEPTION 'un preventivo non passa da % a %', OLD.stato, NEW.stato
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;

        IF OLD.intervento_id IS NOT NULL AND NEW.intervento_id IS DISTINCT FROM OLD.intervento_id THEN
            RAISE EXCEPTION 'l''intervento generato da un preventivo non cambia'
                USING ERRCODE = 'check_violation';
        END IF;
    ELSIF NEW.stato <> 'bozza' THEN
        RAISE EXCEPTION 'un preventivo nasce in bozza'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

-- Dopo l'emissione il preventivo resta: si annulla.
CREATE FUNCTION commerciale.controlla_eliminazione_preventivo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.stato <> 'bozza' THEN
        RAISE EXCEPTION 'si elimina solo una bozza: il preventivo %/% si annulla', OLD.numero, OLD.anno
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    RETURN OLD;
END
$$;

-- Le righe si toccano solo in bozza. Con l'eliminazione a cascata di una bozza
-- il preventivo non si trova più: va bene così.
CREATE FUNCTION commerciale.controlla_righe_preventivo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_preventivo_id uuid := CASE TG_OP WHEN 'DELETE' THEN OLD.preventivo_id ELSE NEW.preventivo_id END;
    v_stato         text;
BEGIN
    SELECT stato INTO v_stato FROM commerciale.preventivi WHERE preventivo_id = v_preventivo_id;

    IF v_stato IS NOT NULL AND v_stato <> 'bozza' THEN
        RAISE EXCEPTION 'le righe di un preventivo % non si modificano', v_stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF TG_OP = 'UPDATE' AND NEW.preventivo_id <> OLD.preventivo_id THEN
        RAISE EXCEPTION 'una riga non si sposta su un altro preventivo'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN CASE TG_OP WHEN 'DELETE' THEN OLD ELSE NEW END;
END
$$;

-- Totali ricalcolati dalle righe. L'IVA si calcola per aliquota sulla somma
-- degli importi e si arrotonda al centesimo, come nel rapportino; le righe
-- senza aliquota non portano IVA.
CREATE FUNCTION commerciale.ricalcola_totali_preventivo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_preventivo_id uuid := CASE TG_OP WHEN 'DELETE' THEN OLD.preventivo_id ELSE NEW.preventivo_id END;
BEGIN
    UPDATE commerciale.preventivi p
    SET imponibile = t.imponibile,
        iva        = t.iva,
        totale     = t.imponibile + t.iva
    FROM (
        SELECT coalesce(sum(imponibile), 0) AS imponibile,
               coalesce(sum(round(imponibile * coalesce(aliquota_iva, 0) / 100, 2)), 0) AS iva
        FROM (
            SELECT aliquota_iva, sum(importo) AS imponibile
            FROM commerciale.preventivi_righe
            WHERE preventivo_id = v_preventivo_id
            GROUP BY aliquota_iva
        ) per_aliquota
    ) t
    WHERE p.preventivo_id = v_preventivo_id
      AND (p.imponibile, p.iva) IS DISTINCT FROM (t.imponibile, t.iva);

    RETURN NULL;
END
$$;

CREATE TRIGGER tr_listino_voci_metadati
    BEFORE UPDATE ON commerciale.listino_voci
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_preventivi_controlli
    BEFORE INSERT OR UPDATE ON commerciale.preventivi
    FOR EACH ROW EXECUTE FUNCTION commerciale.controlla_preventivo();

CREATE TRIGGER tr_preventivi_eliminazione
    BEFORE DELETE ON commerciale.preventivi
    FOR EACH ROW EXECUTE FUNCTION commerciale.controlla_eliminazione_preventivo();

CREATE TRIGGER tr_preventivi_metadati
    BEFORE UPDATE ON commerciale.preventivi
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_preventivi_righe_controlli
    BEFORE INSERT OR UPDATE OR DELETE ON commerciale.preventivi_righe
    FOR EACH ROW EXECUTE FUNCTION commerciale.controlla_righe_preventivo();

CREATE TRIGGER tr_preventivi_righe_metadati
    BEFORE UPDATE ON commerciale.preventivi_righe
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_preventivi_righe_totali
    AFTER INSERT OR UPDATE OR DELETE ON commerciale.preventivi_righe
    FOR EACH ROW EXECUTE FUNCTION commerciale.ricalcola_totali_preventivo();

-- ---------------------------------------------------------------------------
-- Funzioni
-- ---------------------------------------------------------------------------

-- Sostituisce le righe di una bozza. p_righe è un array JSON di oggetti con
-- voce_id, tipo, codice, descrizione, unita, quantita, prezzo_unitario,
-- sconto_percentuale, aliquota_iva; la posizione segue l'ordine dell'array.
-- Restituisce false se il preventivo non esiste o la versione non coincide.
CREATE FUNCTION commerciale.salva_righe_preventivo(p_preventivo_id uuid, p_versione integer, p_righe jsonb)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_preventivo commerciale.preventivi%ROWTYPE;
BEGIN
    SELECT * INTO v_preventivo FROM commerciale.preventivi WHERE preventivo_id = p_preventivo_id FOR UPDATE;

    IF NOT FOUND OR v_preventivo.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_preventivo.stato <> 'bozza' THEN
        RAISE EXCEPTION 'le righe si modificano solo in bozza, non con un preventivo %', v_preventivo.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF p_righe IS NULL OR jsonb_typeof(p_righe) <> 'array' THEN
        RAISE EXCEPTION 'le righe devono essere un array JSON'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    DELETE FROM commerciale.preventivi_righe WHERE preventivo_id = p_preventivo_id;

    INSERT INTO commerciale.preventivi_righe (
        azienda_id, preventivo_id, posizione, voce_id, tipo, codice, descrizione, unita,
        quantita, prezzo_unitario, sconto_percentuale, aliquota_iva, creato_da
    )
    SELECT v_preventivo.azienda_id, p_preventivo_id, r.posizione::integer,
           (r.riga ->> 'voce_id')::uuid,
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

    -- La versione cresce anche se cambiano solo le righe: chi ha letto prima lo scopre.
    UPDATE commerciale.preventivi SET modificato_da = sec.utente_corrente() WHERE preventivo_id = p_preventivo_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION commerciale.salva_righe_preventivo(uuid, integer, jsonb) IS 'Sostituisce le righe di una bozza, con concorrenza ottimistica';

-- Emette una bozza: anno e numero senza buchi nella stessa transazione.
CREATE FUNCTION commerciale.emetti_preventivo(p_preventivo_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_preventivo commerciale.preventivi%ROWTYPE;
    v_oggi       date := (now() AT TIME ZONE 'Europe/Rome')::date;
    v_anno       integer := extract(year FROM v_oggi)::integer;
BEGIN
    SELECT * INTO v_preventivo FROM commerciale.preventivi WHERE preventivo_id = p_preventivo_id FOR UPDATE;

    IF NOT FOUND OR v_preventivo.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_preventivo.stato <> 'bozza' THEN
        RAISE EXCEPTION 'si emette solo una bozza, non un preventivo %', v_preventivo.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM commerciale.preventivi_righe WHERE preventivo_id = p_preventivo_id) THEN
        RAISE EXCEPTION 'un preventivo senza righe non si emette'
            USING ERRCODE = 'check_violation';
    END IF;

    UPDATE commerciale.preventivi
    SET stato         = 'emesso',
        anno          = v_anno,
        numero        = commerciale.prossimo_numero(azienda_id, 'preventivo', v_anno),
        emesso_il     = v_oggi,
        modificato_da = sec.utente_corrente()
    WHERE preventivo_id = p_preventivo_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION commerciale.emetti_preventivo(uuid, integer) IS 'Emette una bozza assegnando anno e numero senza buchi';

-- Da un preventivo accettato nasce un intervento da pianificare sul suo
-- impianto, con le righe e i prezzi copiati. Restituisce l'intervento, oppure
-- NULL se il preventivo non esiste o la versione non coincide.
CREATE FUNCTION commerciale.converti_preventivo(p_preventivo_id uuid, p_versione integer, p_tipo text)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_preventivo commerciale.preventivi%ROWTYPE;
    v_intervento uuid;
BEGIN
    SELECT * INTO v_preventivo FROM commerciale.preventivi WHERE preventivo_id = p_preventivo_id FOR UPDATE;

    IF NOT FOUND OR v_preventivo.versione <> p_versione THEN
        RETURN NULL;
    END IF;

    IF v_preventivo.stato <> 'accettato' THEN
        RAISE EXCEPTION 'l''intervento nasce solo da un preventivo accettato, non %', v_preventivo.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF v_preventivo.intervento_id IS NOT NULL THEN
        RAISE EXCEPTION 'il preventivo %/% ha già generato un intervento', v_preventivo.numero, v_preventivo.anno
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF v_preventivo.impianto_id IS NULL THEN
        RAISE EXCEPTION 'per generare l''intervento il preventivo deve indicare l''impianto'
            USING ERRCODE = 'check_violation';
    END IF;

    INSERT INTO lavoro.interventi (azienda_id, impianto_id, tipo, descrizione, creato_da)
    VALUES (
        v_preventivo.azienda_id, v_preventivo.impianto_id, p_tipo,
        format('Dal preventivo %s/%s: %s', v_preventivo.numero, v_preventivo.anno, v_preventivo.oggetto),
        sec.utente_corrente()
    )
    RETURNING intervento_id INTO v_intervento;

    INSERT INTO lavoro.interventi_righe (
        azienda_id, intervento_id, posizione, tipo, codice, descrizione, unita,
        quantita, prezzo_unitario, sconto_percentuale, aliquota_iva, creato_da
    )
    SELECT azienda_id, v_intervento, posizione, tipo, codice, descrizione, unita,
           quantita, prezzo_unitario, sconto_percentuale, aliquota_iva, sec.utente_corrente()
    FROM commerciale.preventivi_righe
    WHERE preventivo_id = p_preventivo_id
    ORDER BY posizione;

    UPDATE commerciale.preventivi
    SET intervento_id = v_intervento,
        modificato_da = sec.utente_corrente()
    WHERE preventivo_id = p_preventivo_id;

    RETURN v_intervento;
END
$$;

COMMENT ON FUNCTION commerciale.converti_preventivo(uuid, integer, text) IS 'Crea l''intervento da pianificare di un preventivo accettato, con le righe copiate';

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE commerciale.listino_voci     ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerciale.listino_prezzi   ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerciale.preventivi       ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerciale.preventivi_righe ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_listino_voci ON commerciale.listino_voci TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_listino_prezzi ON commerciale.listino_prezzi TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_preventivi ON commerciale.preventivi TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_preventivi_righe ON commerciale.preventivi_righe TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON commerciale.listino_voci, commerciale.listino_prezzi, commerciale.preventivi, commerciale.preventivi_righe FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA commerciale FROM PUBLIC;

-- Le voci si disattivano, i prezzi si aggiungono; i preventivi si eliminano solo
-- in bozza (lo controlla il trigger), le righe si sostituiscono finché è bozza.
GRANT SELECT, INSERT, UPDATE         ON commerciale.listino_voci     TO app_api;
GRANT SELECT, INSERT                 ON commerciale.listino_prezzi   TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerciale.preventivi       TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerciale.preventivi_righe TO app_api;

GRANT EXECUTE ON FUNCTION commerciale.prezzo_voce(uuid, date) TO app_api;
GRANT EXECUTE ON FUNCTION commerciale.salva_righe_preventivo(uuid, integer, jsonb) TO app_api;
GRANT EXECUTE ON FUNCTION commerciale.emetti_preventivo(uuid, integer) TO app_api;
GRANT EXECUTE ON FUNCTION commerciale.converti_preventivo(uuid, integer, text) TO app_api;

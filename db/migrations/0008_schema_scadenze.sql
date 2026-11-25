-- 0008: regole di cadenza (schema norma), scadenze e richiami (schema lavoro).
-- Decisioni del 25/11/2026: due tipi di scadenza, la manutenzione con la
-- periodicità indicata sull'impianto (istruzioni del costruttore o
-- dell'installatore) e il controllo di efficienza energetica dalle regole di
-- cadenza; una manutenzione chiusa con esito risolto chiude la scadenza di
-- manutenzione e, se in chiusura si spunta il controllo, anche quella di
-- controllo; il Tecnico legge le scadenze degli impianti dei propri interventi.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

CREATE SCHEMA norma;

COMMENT ON SCHEMA norma IS 'Regole normative come dati con validità: cadenze, da M4 bollini, rapporti, F-Gas';

ALTER DEFAULT PRIVILEGES IN SCHEMA norma REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Regole di cadenza del controllo di efficienza energetica.
-- I valori iniziali sono VOCI DI PROVA (da_verificare = true), con periodicità
-- fittizie: quelle vere vanno prese dal DPR 74/2013 e dalle norme regionali
-- vigenti e caricate con una nuova migrazione, che chiude queste con valido_al.
-- Il primo controllo dopo l'installazione non ha una colonna propria finché la
-- regola non è verificata: per ora vale la stessa periodicità.
-- ---------------------------------------------------------------------------

CREATE TABLE norma.regole_cadenza (
    regola_cadenza_id     uuid          NOT NULL DEFAULT uuidv7(),
    codice                text          NOT NULL,
    nome                  text          NOT NULL,
    regione               text          NOT NULL,
    tipo_impianto_id      uuid          NOT NULL,
    -- NULL vale per qualsiasi combustibile; una regola con il combustibile vince.
    combustibile_id       uuid,
    -- Potenza utile nell'intervallo [da, a); NULL da una parte = senza limite.
    potenza_da_kw         numeric(9, 2),
    potenza_a_kw          numeric(9, 2),
    periodicita_mesi      integer       NOT NULL,
    riferimento_normativo text,
    valido_dal            date          NOT NULL,
    valido_al             date,
    da_verificare         boolean       NOT NULL DEFAULT true,
    fonte                 text,
    creato_il             timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT pk_regole_cadenza PRIMARY KEY (regola_cadenza_id),
    CONSTRAINT uq_regole_cadenza_codice UNIQUE (codice),
    CONSTRAINT fk_regole_cadenza_tipi_impianto FOREIGN KEY (tipo_impianto_id) REFERENCES impianti.tipi_impianto (tipo_impianto_id),
    CONSTRAINT fk_regole_cadenza_combustibili FOREIGN KEY (combustibile_id) REFERENCES impianti.combustibili (combustibile_id),
    CONSTRAINT ck_regole_cadenza_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_regole_cadenza_regione CHECK (regione IN ('toscana', 'lombardia')),
    CONSTRAINT ck_regole_cadenza_potenza CHECK (
        (potenza_da_kw IS NULL OR potenza_da_kw >= 0)
        AND (potenza_da_kw IS NULL OR potenza_a_kw IS NULL OR potenza_a_kw > potenza_da_kw)
    ),
    CONSTRAINT ck_regole_cadenza_periodicita CHECK (periodicita_mesi BETWEEN 1 AND 120),
    CONSTRAINT ck_regole_cadenza_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_regole_cadenza_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL),
    -- Nello stesso periodo, per regione, tipo e combustibile, le fasce di
    -- potenza non si sovrappongono: per un impianto vale una regola sola.
    CONSTRAINT ex_regole_cadenza_sovrapposte EXCLUDE USING gist (
        regione WITH =,
        tipo_impianto_id WITH =,
        coalesce(combustibile_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =,
        numrange(potenza_da_kw, potenza_a_kw, '[)') WITH &&,
        daterange(valido_dal, valido_al, '[]') WITH &&
    )
);

COMMENT ON TABLE norma.regole_cadenza IS 'Periodicità del controllo di efficienza per regione, tipo di impianto, combustibile e potenza';

-- Voci di prova: nomi e periodicità volutamente segnati come fittizi.
INSERT INTO norma.regole_cadenza (codice, nome, regione, tipo_impianto_id, potenza_da_kw, potenza_a_kw, periodicita_mesi, valido_dal)
SELECT v.codice, v.nome, 'toscana', t.tipo_impianto_id, v.da, v.a, v.mesi, '2026-01-01'
FROM (VALUES
    ('prova_riscaldamento_piccolo', 'Riscaldamento fino a 100 kW (voce di prova)', 'prova_riscaldamento', NULL::numeric, 100::numeric, 48),
    ('prova_riscaldamento_grande', 'Riscaldamento da 100 kW (voce di prova)', 'prova_riscaldamento', 100, NULL, 24),
    ('prova_climatizzazione', 'Climatizzazione (voce di prova)', 'prova_climatizzazione', NULL, NULL, 48),
    ('prova_pompa_di_calore', 'Pompa di calore (voce di prova)', 'prova_pompa_di_calore', NULL, NULL, 48)
) AS v (codice, nome, tipo, da, a, mesi)
JOIN impianti.tipi_impianto t ON t.codice = v.tipo;

-- ---------------------------------------------------------------------------
-- Nuove colonne su impianti e interventi
-- ---------------------------------------------------------------------------

ALTER TABLE impianti.impianti
    ADD COLUMN manutenzione_mesi integer,
    ADD CONSTRAINT ck_impianti_manutenzione_mesi CHECK (manutenzione_mesi BETWEEN 1 AND 120);

COMMENT ON COLUMN impianti.impianti.manutenzione_mesi IS 'Periodicità della manutenzione indicata dal costruttore o dall''installatore; NULL = nessuna scadenza';

-- Spuntato alla chiusura di una manutenzione: è stato eseguito anche il
-- controllo di efficienza. Da M4 genererà la bozza del rapporto di controllo.
ALTER TABLE lavoro.interventi
    ADD COLUMN controllo_efficienza boolean NOT NULL DEFAULT false,
    ADD CONSTRAINT ck_interventi_controllo_efficienza CHECK (NOT controllo_efficienza OR tipo = 'manutenzione');

-- ---------------------------------------------------------------------------
-- Scadenze
-- ---------------------------------------------------------------------------

-- Stati: aperta -> chiusa (da un intervento) oppure annullata (impianto
-- dismesso, periodicità tolta, nessuna regola). Chiusa e annullata sono finali.
-- Stato del richiamo: non_contattato -> in_attesa (nessuna risposta o da
-- risentire) -> fissato (appuntamento) o rifiutato; lo aggiornano i richiami.
CREATE TABLE lavoro.scadenze (
    scadenza_id            uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id             uuid        NOT NULL,
    impianto_id            uuid        NOT NULL,
    tipo                   text        NOT NULL,
    data_scadenza          date        NOT NULL,
    stato                  text        NOT NULL DEFAULT 'aperta',
    -- Periodicità e regola usate per il calcolo, copiate per lo storico.
    periodicita_mesi       integer     NOT NULL,
    regola_cadenza_id      uuid,
    -- Data corretta a mano dall'ufficio: il ricalcolo non la sposta più.
    data_manuale           boolean     NOT NULL DEFAULT false,
    intervento_origine_id  uuid,
    intervento_chiusura_id uuid,
    chiusa_il              timestamptz,
    annullata_il           timestamptz,
    motivo_annullamento    text,
    stato_richiamo         text        NOT NULL DEFAULT 'non_contattato',
    creato_il              timestamptz NOT NULL DEFAULT now(),
    creato_da              uuid,
    modificato_il          timestamptz,
    modificato_da          uuid,
    versione               integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_scadenze PRIMARY KEY (scadenza_id),
    CONSTRAINT uq_scadenze_azienda_scadenza UNIQUE (azienda_id, scadenza_id),
    CONSTRAINT fk_scadenze_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_scadenze_regole FOREIGN KEY (regola_cadenza_id) REFERENCES norma.regole_cadenza (regola_cadenza_id),
    CONSTRAINT fk_scadenze_intervento_origine FOREIGN KEY (azienda_id, intervento_origine_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_scadenze_intervento_chiusura FOREIGN KEY (azienda_id, intervento_chiusura_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_scadenze_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_scadenze_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_scadenze_tipo CHECK (tipo IN ('manutenzione', 'controllo_efficienza')),
    CONSTRAINT ck_scadenze_stato CHECK (stato IN ('aperta', 'chiusa', 'annullata')),
    CONSTRAINT ck_scadenze_stato_richiamo CHECK (stato_richiamo IN ('non_contattato', 'in_attesa', 'fissato', 'rifiutato')),
    CONSTRAINT ck_scadenze_periodicita CHECK (periodicita_mesi BETWEEN 1 AND 120),
    -- Il controllo viene sempre da una regola, la manutenzione mai.
    CONSTRAINT ck_scadenze_regola CHECK ((tipo = 'controllo_efficienza') = (regola_cadenza_id IS NOT NULL)),
    CONSTRAINT ck_scadenze_chiusura CHECK (
        (stato = 'chiusa') = (chiusa_il IS NOT NULL AND intervento_chiusura_id IS NOT NULL)
    ),
    CONSTRAINT ck_scadenze_annullamento CHECK (
        (stato = 'annullata') = (annullata_il IS NOT NULL AND nullif(btrim(motivo_annullamento), '') IS NOT NULL)
    ),
    CONSTRAINT ck_scadenze_versione CHECK (versione >= 1)
);

-- Una sola scadenza aperta per impianto e tipo.
CREATE UNIQUE INDEX uq_scadenze_aperte_impianto_tipo
    ON lavoro.scadenze (impianto_id, tipo)
    WHERE stato = 'aperta';
-- Lista "da richiamare": scadenze aperte per azienda e data.
CREATE INDEX ix_scadenze_aperte_azienda_data
    ON lavoro.scadenze (azienda_id, data_scadenza)
    WHERE stato = 'aperta';
-- Storico nella scheda impianto.
CREATE INDEX ix_scadenze_impianto ON lavoro.scadenze (impianto_id, data_scadenza DESC);

COMMENT ON TABLE lavoro.scadenze IS 'Prossima manutenzione o controllo di un impianto; calcolata dal database, si chiude o si annulla';

-- ---------------------------------------------------------------------------
-- Richiami: tentativi di contatto per una scadenza. Solo aggiunta.
-- ---------------------------------------------------------------------------

CREATE TABLE lavoro.richiami (
    richiamo_id   uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id    uuid        NOT NULL,
    scadenza_id   uuid        NOT NULL,
    canale        text        NOT NULL,
    effettuato_il timestamptz NOT NULL DEFAULT now(),
    esito         text        NOT NULL,
    richiamare_il date,
    intervento_id uuid,
    note          text,
    creato_il     timestamptz NOT NULL DEFAULT now(),
    creato_da     uuid,
    modificato_il timestamptz,
    modificato_da uuid,
    versione      integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_richiami PRIMARY KEY (richiamo_id),
    CONSTRAINT fk_richiami_scadenze FOREIGN KEY (azienda_id, scadenza_id) REFERENCES lavoro.scadenze (azienda_id, scadenza_id),
    CONSTRAINT fk_richiami_interventi FOREIGN KEY (azienda_id, intervento_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_richiami_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_richiami_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_richiami_canale CHECK (canale IN ('telefono', 'email', 'sms', 'whatsapp', 'di_persona', 'altro')),
    CONSTRAINT ck_richiami_esito CHECK (esito IN ('nessuna_risposta', 'da_richiamare', 'appuntamento', 'rifiutato')),
    CONSTRAINT ck_richiami_richiamare_il CHECK (richiamare_il IS NULL OR esito IN ('nessuna_risposta', 'da_richiamare')),
    CONSTRAINT ck_richiami_intervento CHECK (intervento_id IS NULL OR esito = 'appuntamento'),
    CONSTRAINT ck_richiami_versione CHECK (versione >= 1)
);

CREATE INDEX ix_richiami_scadenza ON lavoro.richiami (scadenza_id, effettuato_il DESC);

COMMENT ON TABLE lavoro.richiami IS 'Tentativi di contatto per una scadenza: canale, esito, eventuale appuntamento';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

-- Una scadenza chiusa o annullata non cambia più; impianto e tipo mai.
CREATE FUNCTION lavoro.controlla_modifica_scadenza()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.stato IN ('chiusa', 'annullata') THEN
        RAISE EXCEPTION 'la scadenza è % e non si modifica', OLD.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF NEW.azienda_id <> OLD.azienda_id OR NEW.impianto_id <> OLD.impianto_id OR NEW.tipo <> OLD.tipo THEN
        RAISE EXCEPTION 'azienda, impianto e tipo di una scadenza non si modificano'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

-- Un richiamo si registra solo su una scadenza aperta e ne aggiorna lo stato.
CREATE FUNCTION lavoro.registra_richiamo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE lavoro.scadenze
    SET stato_richiamo = CASE NEW.esito
                             WHEN 'appuntamento' THEN 'fissato'
                             WHEN 'rifiutato'    THEN 'rifiutato'
                             ELSE 'in_attesa'
                         END,
        modificato_da  = NEW.creato_da
    WHERE scadenza_id = NEW.scadenza_id
      AND stato = 'aperta';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'un richiamo si registra solo su una scadenza aperta'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    RETURN NEW;
END
$$;

CREATE TRIGGER tr_scadenze_controlli
    BEFORE UPDATE ON lavoro.scadenze
    FOR EACH ROW EXECUTE FUNCTION lavoro.controlla_modifica_scadenza();

CREATE TRIGGER tr_scadenze_metadati
    BEFORE UPDATE ON lavoro.scadenze
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_richiami_scadenza
    AFTER INSERT ON lavoro.richiami
    FOR EACH ROW EXECUTE FUNCTION lavoro.registra_richiamo();

-- ---------------------------------------------------------------------------
-- Calcolo delle scadenze
-- ---------------------------------------------------------------------------

-- Regola di cadenza valida oggi per un impianto: regione dell'azienda, tipo,
-- combustibile (una regola specifica vince su una generica) e fascia di
-- potenza. Una regola con fasce non vale per un impianto senza potenza.
CREATE FUNCTION norma.regola_cadenza_impianto(p_impianto_id uuid)
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
    SELECT r.regola_cadenza_id
    FROM impianti.impianti i
    JOIN sec.aziende a ON a.azienda_id = i.azienda_id
    JOIN norma.regole_cadenza r
      ON r.regione = a.regione
     AND r.tipo_impianto_id = i.tipo_impianto_id
     AND (r.combustibile_id IS NULL OR r.combustibile_id = i.combustibile_id)
     AND (r.potenza_da_kw IS NULL OR i.potenza_utile_kw >= r.potenza_da_kw)
     AND (r.potenza_a_kw IS NULL OR i.potenza_utile_kw < r.potenza_a_kw)
     AND (now() AT TIME ZONE 'Europe/Rome')::date BETWEEN r.valido_dal AND coalesce(r.valido_al, 'infinity'::date)
    WHERE i.impianto_id = p_impianto_id
    ORDER BY r.combustibile_id IS NULL, r.valido_dal DESC
    LIMIT 1
$$;

COMMENT ON FUNCTION norma.regola_cadenza_impianto(uuid) IS 'Regola di cadenza del controllo valida oggi per l''impianto; NULL se nessuna';

-- Porta la scadenza aperta di un tipo alla data dovuta, creandola se manca.
-- p_base è la data dell'ultimo intervento che conta (NULL se non ce n'è: la
-- scadenza è da fare subito e l'ufficio può correggerne la data).
CREATE FUNCTION lavoro.allinea_scadenza(
    p_azienda_id    uuid,
    p_impianto_id   uuid,
    p_tipo          text,
    p_mesi          integer,
    p_regola_id     uuid,
    p_base          date,
    p_intervento_id uuid
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_oggi date := (now() AT TIME ZONE 'Europe/Rome')::date;
    v_data date := CASE WHEN p_base IS NULL THEN v_oggi
                        ELSE (p_base + make_interval(months => p_mesi))::date END;
BEGIN
    UPDATE lavoro.scadenze
    SET data_scadenza         = CASE WHEN data_manuale THEN data_scadenza ELSE v_data END,
        periodicita_mesi      = p_mesi,
        regola_cadenza_id     = p_regola_id,
        intervento_origine_id = p_intervento_id,
        modificato_da         = sec.utente_corrente()
    WHERE impianto_id = p_impianto_id
      AND tipo = p_tipo
      AND stato = 'aperta'
      AND (data_scadenza, periodicita_mesi, regola_cadenza_id, intervento_origine_id)
          IS DISTINCT FROM
          (CASE WHEN data_manuale THEN data_scadenza ELSE v_data END, p_mesi, p_regola_id, p_intervento_id);

    IF NOT FOUND AND NOT EXISTS (
        SELECT 1 FROM lavoro.scadenze
        WHERE impianto_id = p_impianto_id AND tipo = p_tipo AND stato = 'aperta'
    ) THEN
        INSERT INTO lavoro.scadenze (
            azienda_id, impianto_id, tipo, data_scadenza, periodicita_mesi,
            regola_cadenza_id, intervento_origine_id, creato_da
        )
        VALUES (
            p_azienda_id, p_impianto_id, p_tipo, v_data, p_mesi,
            p_regola_id, p_intervento_id, sec.utente_corrente()
        );
    END IF;
END
$$;

-- Annulla la scadenza aperta di un tipo (o di tutti i tipi, con p_tipo NULL).
CREATE FUNCTION lavoro.annulla_scadenze(p_impianto_id uuid, p_tipo text, p_motivo text)
RETURNS void
LANGUAGE sql
AS $$
    UPDATE lavoro.scadenze
    SET stato               = 'annullata',
        annullata_il        = now(),
        motivo_annullamento = p_motivo,
        modificato_da       = sec.utente_corrente()
    WHERE impianto_id = p_impianto_id
      AND (p_tipo IS NULL OR tipo = p_tipo)
      AND stato = 'aperta'
$$;

-- Ricalcola le scadenze aperte di un impianto. La base è l'ultimo intervento
-- completato con esito risolto: manutenzione o installazione per la
-- manutenzione; manutenzione con il controllo spuntato, o installazione, per
-- il controllo di efficienza. Un impianto dismesso o eliminato non ha scadenze.
CREATE FUNCTION lavoro.ricalcola_scadenze(p_impianto_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_impianto impianti.impianti%ROWTYPE;
    v_regola   norma.regole_cadenza%ROWTYPE;
    v_base     date;
    v_origine  uuid;
BEGIN
    -- Il blocco sull'impianto mette in fila due ricalcoli concorrenti.
    SELECT * INTO v_impianto
    FROM impianti.impianti
    WHERE impianto_id = p_impianto_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF v_impianto.eliminato_il IS NOT NULL THEN
        PERFORM lavoro.annulla_scadenze(p_impianto_id, NULL, 'Impianto eliminato');
        RETURN;
    END IF;

    IF v_impianto.stato <> 'attivo' THEN
        PERFORM lavoro.annulla_scadenze(p_impianto_id, NULL, 'Impianto dismesso');
        RETURN;
    END IF;

    -- Manutenzione
    IF v_impianto.manutenzione_mesi IS NULL THEN
        PERFORM lavoro.annulla_scadenze(p_impianto_id, 'manutenzione', 'Periodicità di manutenzione non indicata');
    ELSE
        SELECT (chiuso_il AT TIME ZONE 'Europe/Rome')::date, intervento_id
        INTO v_base, v_origine
        FROM lavoro.interventi
        WHERE impianto_id = p_impianto_id
          AND stato = 'completato'
          AND esito = 'risolto'
          AND tipo IN ('manutenzione', 'installazione')
        ORDER BY chiuso_il DESC
        LIMIT 1;

        PERFORM lavoro.allinea_scadenza(
            v_impianto.azienda_id, p_impianto_id, 'manutenzione',
            v_impianto.manutenzione_mesi, NULL, v_base, v_origine);
    END IF;

    -- Controllo di efficienza energetica
    SELECT * INTO v_regola
    FROM norma.regole_cadenza
    WHERE regola_cadenza_id = norma.regola_cadenza_impianto(p_impianto_id);

    IF NOT FOUND THEN
        PERFORM lavoro.annulla_scadenze(p_impianto_id, 'controllo_efficienza', 'Nessuna regola di cadenza applicabile');
    ELSE
        v_base := NULL;
        v_origine := NULL;

        SELECT (chiuso_il AT TIME ZONE 'Europe/Rome')::date, intervento_id
        INTO v_base, v_origine
        FROM lavoro.interventi
        WHERE impianto_id = p_impianto_id
          AND stato = 'completato'
          AND esito = 'risolto'
          AND (controllo_efficienza OR tipo = 'installazione')
        ORDER BY chiuso_il DESC
        LIMIT 1;

        PERFORM lavoro.allinea_scadenza(
            v_impianto.azienda_id, p_impianto_id, 'controllo_efficienza',
            v_regola.periodicita_mesi, v_regola.regola_cadenza_id, v_base, v_origine);
    END IF;
END
$$;

COMMENT ON FUNCTION lavoro.ricalcola_scadenze(uuid) IS 'Crea, sposta o annulla le scadenze aperte di un impianto secondo periodicità e regole di cadenza';

-- Ogni modifica che conta per le scadenze le ricalcola.
CREATE FUNCTION lavoro.ricalcola_scadenze_impianto()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM lavoro.ricalcola_scadenze(NEW.impianto_id);
    RETURN NULL;
END
$$;

CREATE TRIGGER tr_impianti_scadenze
    AFTER INSERT OR UPDATE OF stato, tipo_impianto_id, combustibile_id, potenza_utile_kw, manutenzione_mesi, eliminato_il
    ON impianti.impianti
    FOR EACH ROW EXECUTE FUNCTION lavoro.ricalcola_scadenze_impianto();

-- ---------------------------------------------------------------------------
-- Chiusura dell'intervento, ora con le scadenze
-- ---------------------------------------------------------------------------

-- Il nuovo parametro ha un valore predefinito: le chiamate con cinque
-- argomenti continuano a funzionare.
DROP FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb);

-- In una transazione: sostituisce le righe, registra esito e note, chiude.
-- Una manutenzione con esito risolto chiude la scadenza di manutenzione e, con
-- p_controllo_efficienza, quella di controllo; poi le scadenze si ricalcolano
-- a partire da questo intervento. Restituisce false se l'intervento non esiste
-- o la versione non coincide.
CREATE FUNCTION lavoro.chiudi_intervento(
    p_intervento_id        uuid,
    p_versione             integer,
    p_esito                text,
    p_note_chiusura        text,
    p_righe                jsonb,
    p_controllo_efficienza boolean DEFAULT false
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
    SET stato                = 'completato',
        esito                = p_esito,
        note_chiusura        = p_note_chiusura,
        controllo_efficienza = coalesce(p_controllo_efficienza, false),
        chiuso_il            = now(),
        chiuso_da            = sec.utente_corrente(),
        modificato_da        = sec.utente_corrente()
    WHERE intervento_id = p_intervento_id;

    IF v_intervento.tipo = 'manutenzione' AND p_esito = 'risolto' THEN
        UPDATE lavoro.scadenze
        SET stato                  = 'chiusa',
            chiusa_il              = now(),
            intervento_chiusura_id = p_intervento_id,
            modificato_da          = sec.utente_corrente()
        WHERE impianto_id = v_intervento.impianto_id
          AND stato = 'aperta'
          AND (tipo = 'manutenzione' OR (tipo = 'controllo_efficienza' AND coalesce(p_controllo_efficienza, false)));
    END IF;

    PERFORM lavoro.ricalcola_scadenze(v_intervento.impianto_id);

    RETURN true;
END
$$;

COMMENT ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean) IS 'Chiude un intervento con esito e righe, chiude le scadenze soddisfatte e calcola le successive';

-- ---------------------------------------------------------------------------
-- Scadenze in arrivo: la lista "da richiamare", usata anche dal chatbot.
-- Comprende le scadute; il filtro sui propri impianti lo applica l'API.
-- ---------------------------------------------------------------------------

CREATE FUNCTION lavoro.scadenze_in_arrivo(p_giorni integer)
RETURNS TABLE (
    scadenza_id          uuid,
    impianto_id          uuid,
    tipo                 text,
    data_scadenza        date,
    giorni_mancanti      integer,
    stato_richiamo       text,
    versione             integer,
    cliente_id           uuid,
    cliente              text,
    indirizzo            text,
    comune               text,
    provincia            text,
    telefono             text,
    ultimo_richiamo_il   timestamptz,
    ultimo_esito         text,
    richiamare_il        date
)
LANGUAGE sql
STABLE
AS $$
    SELECT s.scadenza_id,
           s.impianto_id,
           s.tipo,
           s.data_scadenza,
           s.data_scadenza - (now() AT TIME ZONE 'Europe/Rome')::date,
           s.stato_richiamo,
           s.versione,
           c.cliente_id,
           CASE WHEN c.tipo = 'privato' THEN btrim(c.cognome || ' ' || c.nome) ELSE c.denominazione END,
           btrim(se.indirizzo || ' ' || coalesce(se.civico, '')),
           se.comune,
           se.provincia,
           t.valore,
           r.effettuato_il,
           r.esito,
           r.richiamare_il
    FROM lavoro.scadenze s
    JOIN impianti.impianti i ON i.impianto_id = s.impianto_id
    JOIN anagrafica.sedi se ON se.sede_id = i.sede_id
    JOIN anagrafica.clienti c ON c.cliente_id = coalesce(i.responsabile_cliente_id, i.proprietario_cliente_id, se.cliente_id)
    LEFT JOIN LATERAL (
        SELECT ct.valore
        FROM anagrafica.contatti ct
        WHERE ct.cliente_id = c.cliente_id AND ct.tipo IN ('telefono', 'cellulare')
        ORDER BY ct.principale DESC, ct.creato_il
        LIMIT 1
    ) t ON true
    LEFT JOIN LATERAL (
        SELECT ri.effettuato_il, ri.esito, ri.richiamare_il
        FROM lavoro.richiami ri
        WHERE ri.scadenza_id = s.scadenza_id
        ORDER BY ri.effettuato_il DESC
        LIMIT 1
    ) r ON true
    WHERE s.stato = 'aperta'
      AND i.eliminato_il IS NULL
      AND se.eliminata_il IS NULL
      AND c.eliminato_il IS NULL
      AND c.anonimizzato_il IS NULL
      AND s.data_scadenza <= (now() AT TIME ZONE 'Europe/Rome')::date + p_giorni
    ORDER BY s.data_scadenza, s.scadenza_id
$$;

COMMENT ON FUNCTION lavoro.scadenze_in_arrivo(integer) IS 'Scadenze aperte entro p_giorni (comprese le scadute), con cliente, telefono e ultimo richiamo';

-- ---------------------------------------------------------------------------
-- Calcolo iniziale per gli impianti già presenti
-- ---------------------------------------------------------------------------

-- Senza periodicità di manutenzione nasce solo il controllo, dove c'è una regola.
DO $$
DECLARE
    v_impianto_id uuid;
BEGIN
    FOR v_impianto_id IN SELECT impianto_id FROM impianti.impianti LOOP
        PERFORM lavoro.ricalcola_scadenze(v_impianto_id);
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE lavoro.scadenze ENABLE ROW LEVEL SECURITY;
ALTER TABLE lavoro.richiami ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_scadenze ON lavoro.scadenze TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_richiami ON lavoro.richiami TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON SCHEMA norma FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA norma FROM PUBLIC;
REVOKE ALL ON lavoro.scadenze, lavoro.richiami FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA norma FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA lavoro FROM PUBLIC;

GRANT USAGE ON SCHEMA norma TO app_api;

GRANT SELECT ON norma.regole_cadenza TO app_api;
-- Niente DELETE: le scadenze si chiudono o si annullano. I richiami sono solo aggiunta.
GRANT SELECT, INSERT, UPDATE ON lavoro.scadenze TO app_api;
GRANT SELECT, INSERT         ON lavoro.richiami TO app_api;

-- Le funzioni sono SECURITY INVOKER: anche quelle interne, chiamate dal
-- ricalcolo e dai trigger, vanno concesse ad app_api.
GRANT EXECUTE ON FUNCTION norma.regola_cadenza_impianto(uuid) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.allinea_scadenza(uuid, uuid, text, integer, uuid, date, uuid) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.annulla_scadenze(uuid, text, text) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.ricalcola_scadenze(uuid) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean) TO app_api;
GRANT EXECUTE ON FUNCTION lavoro.scadenze_in_arrivo(integer) TO app_api;

-- ---------------------------------------------------------------------------
-- Permessi
-- ---------------------------------------------------------------------------

INSERT INTO sec.permessi (codice, descrizione) VALUES
    ('scadenze.lettura',   'Consultare scadenze e richiami'),
    ('scadenze.scrittura', 'Gestire scadenze e registrare i richiami');

-- Decisione del 25/11/2026: il Tecnico legge le scadenze degli impianti dei
-- propri interventi, senza richiami (lo filtra l'API).
INSERT INTO sec.ruoli_permessi (ruolo_id, permesso_id, ambito)
SELECT r.ruolo_id, p.permesso_id, m.ambito
FROM (VALUES
    ('titolare',             'scadenze.lettura',   'tutti'),
    ('titolare',             'scadenze.scrittura', 'tutti'),
    ('amministrativo',       'scadenze.lettura',   'tutti'),
    ('amministrativo',       'scadenze.scrittura', 'tutti'),
    ('responsabile_tecnico', 'scadenze.lettura',   'tutti'),
    ('responsabile_tecnico', 'scadenze.scrittura', 'tutti'),
    ('tecnico',              'scadenze.lettura',   'propri'),
    ('sola_lettura',         'scadenze.lettura',   'tutti')
) AS m (ruolo, permesso, ambito)
JOIN sec.ruoli r ON r.codice = m.ruolo AND r.azienda_id IS NULL
JOIN sec.permessi p ON p.codice = m.permesso;

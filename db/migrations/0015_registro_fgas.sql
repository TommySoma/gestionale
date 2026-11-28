-- 0015: registro F-Gas, controlli delle perdite, certificati e Banca dati FGAS (M4b, ADR 0008).
--
-- Fonti lette il 28/11/2026: Reg. (UE) 2024/573 artt. 3-7 e allegati I, II e VI;
-- DPR 146/2018 art. 16; chiarimento del MASE del 27/02/2025. Soglie, frequenze,
-- esenzioni e GWP sono dati con validità e fonte, come cadenze e bollini.

-- ---------------------------------------------------------------------------
-- Gas fluorurati e loro GWP
-- ---------------------------------------------------------------------------

CREATE TABLE norma.gas_fluorurati (
    gas_id        uuid          NOT NULL DEFAULT uuidv7(),
    codice        text          NOT NULL,
    designazione  text          NOT NULL,
    -- Allegato del regolamento: I (HFC e simili, soglie in CO2 equivalente) o II (insaturi, soglie in kg).
    allegato      text          NOT NULL,
    miscela       boolean       NOT NULL DEFAULT false,
    gwp           numeric(12,3) NOT NULL,
    valido_dal    date          NOT NULL,
    valido_al     date,
    da_verificare boolean       NOT NULL DEFAULT true,
    fonte         text,
    creato_il     timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT pk_gas_fluorurati PRIMARY KEY (gas_id),
    CONSTRAINT uq_gas_fluorurati_codice UNIQUE (codice),
    CONSTRAINT ck_gas_fluorurati_allegato CHECK (allegato IN ('I', 'II')),
    CONSTRAINT ck_gas_fluorurati_gwp CHECK (gwp >= 0),
    CONSTRAINT ck_gas_fluorurati_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_gas_fluorurati_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

COMMENT ON TABLE norma.gas_fluorurati IS 'Gas fluorurati con il GWP a 100 anni del Reg. (UE) 2024/573; le miscele lo ricavano dai componenti';

-- Composizione delle miscele in percentuale di peso (allegato VI).
CREATE TABLE norma.gas_componenti (
    miscela_id    uuid         NOT NULL,
    componente_id uuid         NOT NULL,
    percentuale   numeric(5,2) NOT NULL,
    CONSTRAINT pk_gas_componenti PRIMARY KEY (miscela_id, componente_id),
    CONSTRAINT fk_gas_componenti_miscela FOREIGN KEY (miscela_id) REFERENCES norma.gas_fluorurati (gas_id),
    CONSTRAINT fk_gas_componenti_componente FOREIGN KEY (componente_id) REFERENCES norma.gas_fluorurati (gas_id),
    CONSTRAINT ck_gas_componenti_percentuale CHECK (percentuale > 0 AND percentuale <= 100)
);

INSERT INTO norma.gas_fluorurati (codice, designazione, allegato, gwp, valido_dal, da_verificare, fonte) VALUES
    ('R32',    'HFC-32',    'I',  675,   '2024-03-11', false, 'Reg. (UE) 2024/573, allegato I, sezione 1, letto il 28/11/2026'),
    ('R125',   'HFC-125',   'I',  3500,  '2024-03-11', false, 'Reg. (UE) 2024/573, allegato I, sezione 1, letto il 28/11/2026'),
    ('R134a',  'HFC-134a',  'I',  1430,  '2024-03-11', false, 'Reg. (UE) 2024/573, allegato I, sezione 1, letto il 28/11/2026'),
    ('R143a',  'HFC-143a',  'I',  4470,  '2024-03-11', false, 'Reg. (UE) 2024/573, allegato I, sezione 1, letto il 28/11/2026'),
    ('R1234yf', 'HFC-1234yf', 'II', 0.501, '2024-03-11', false, 'Reg. (UE) 2024/573, allegato II, sezione 1, letto il 28/11/2026'),
    ('R1234ze', 'HFC-1234ze', 'II', 1.37,  '2024-03-11', false, 'Reg. (UE) 2024/573, allegato II, sezione 1, letto il 28/11/2026');

-- Miscele comuni: composizione della norma ISO 817 non letta, quindi da verificare.
INSERT INTO norma.gas_fluorurati (codice, designazione, allegato, miscela, gwp, valido_dal, da_verificare) VALUES
    ('R410A', 'R-410A (HFC-32/HFC-125)', 'I', true, 0, '2024-03-11', true),
    ('R407C', 'R-407C (HFC-32/HFC-125/HFC-134a)', 'I', true, 0, '2024-03-11', true),
    ('R404A', 'R-404A (HFC-125/HFC-143a/HFC-134a)', 'I', true, 0, '2024-03-11', true);

INSERT INTO norma.gas_componenti (miscela_id, componente_id, percentuale)
SELECT m.gas_id, c.gas_id, v.percentuale
FROM (VALUES
    ('R410A', 'R32', 50), ('R410A', 'R125', 50),
    ('R407C', 'R32', 23), ('R407C', 'R125', 25), ('R407C', 'R134a', 52),
    ('R404A', 'R125', 44), ('R404A', 'R143a', 52), ('R404A', 'R134a', 4)
) AS v (miscela, componente, percentuale)
JOIN norma.gas_fluorurati m ON m.codice = v.miscela
JOIN norma.gas_fluorurati c ON c.codice = v.componente;

-- Allegato VI: media pesata dei GWP dei componenti.
UPDATE norma.gas_fluorurati m
SET gwp = (SELECT sum(c.percentuale * g.gwp) / 100
           FROM norma.gas_componenti c JOIN norma.gas_fluorurati g ON g.gas_id = c.componente_id
           WHERE c.miscela_id = m.gas_id)
WHERE m.miscela;

-- ---------------------------------------------------------------------------
-- Regole dei controlli delle perdite (art. 5)
-- ---------------------------------------------------------------------------

CREATE TABLE norma.regole_controllo_perdite (
    regola_id            uuid          NOT NULL DEFAULT uuidv7(),
    allegato             text          NOT NULL,
    -- Fascia [da, a): tonnellate di CO2 equivalente per l'allegato I, chilogrammi per l'allegato II.
    soglia_da            numeric(12,3) NOT NULL,
    soglia_a             numeric(12,3),
    mesi                 integer       NOT NULL,
    mesi_con_rilevamento integer       NOT NULL,
    valido_dal           date          NOT NULL,
    valido_al            date,
    da_verificare        boolean       NOT NULL DEFAULT true,
    fonte                text,
    CONSTRAINT pk_regole_controllo_perdite PRIMARY KEY (regola_id),
    CONSTRAINT ck_regole_controllo_perdite_allegato CHECK (allegato IN ('I', 'II')),
    CONSTRAINT ck_regole_controllo_perdite_soglie CHECK (soglia_a IS NULL OR soglia_a > soglia_da),
    CONSTRAINT ck_regole_controllo_perdite_mesi CHECK (mesi BETWEEN 1 AND 120 AND mesi_con_rilevamento BETWEEN mesi AND 120),
    CONSTRAINT ck_regole_controllo_perdite_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

INSERT INTO norma.regole_controllo_perdite (allegato, soglia_da, soglia_a, mesi, mesi_con_rilevamento, valido_dal, da_verificare, fonte)
SELECT r.allegato, r.da, r.a, r.mesi, r.rilevamento, '2024-03-11', false, 'Reg. (UE) 2024/573, art. 5, par. 1 e 6, letto il 28/11/2026'
FROM (VALUES
    ('I', 5, 50, 12, 24), ('I', 50, 500, 6, 12), ('I', 500, NULL, 3, 6),
    ('II', 1, 10, 12, 24), ('II', 10, 100, 6, 12), ('II', 100, NULL, 3, 6)
) AS r (allegato, da, a, mesi, rilevamento);

-- Apparecchiature ermeticamente sigillate ed etichettate: esenti sotto la soglia (art. 5, par. 1).
CREATE TABLE norma.esenzioni_ermetiche (
    esenzione_id         uuid          NOT NULL DEFAULT uuidv7(),
    -- NULL: vale per ogni gas (la deroga degli edifici residenziali, in kg).
    allegato             text,
    soglia               numeric(12,3) NOT NULL,
    unita                text          NOT NULL,
    solo_residenziale    boolean       NOT NULL DEFAULT false,
    valido_dal           date          NOT NULL,
    valido_al            date,
    da_verificare        boolean       NOT NULL DEFAULT true,
    fonte                text,
    CONSTRAINT pk_esenzioni_ermetiche PRIMARY KEY (esenzione_id),
    CONSTRAINT ck_esenzioni_ermetiche_allegato CHECK (allegato IS NULL OR allegato IN ('I', 'II')),
    CONSTRAINT ck_esenzioni_ermetiche_unita CHECK (unita IN ('t_co2e', 'kg')),
    CONSTRAINT ck_esenzioni_ermetiche_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

INSERT INTO norma.esenzioni_ermetiche (allegato, soglia, unita, solo_residenziale, valido_dal, da_verificare, fonte) VALUES
    ('I',  10, 't_co2e', false, '2024-03-11', false, 'Reg. (UE) 2024/573, art. 5, par. 1, secondo comma, lettera a), letto il 28/11/2026'),
    ('II', 2,  'kg',     false, '2024-03-11', false, 'Reg. (UE) 2024/573, art. 5, par. 1, secondo comma, lettera b), letto il 28/11/2026'),
    (NULL, 3,  'kg',     true,  '2024-03-11', false, 'Reg. (UE) 2024/573, art. 5, par. 1, terzo comma, letto il 28/11/2026');

-- ---------------------------------------------------------------------------
-- Apparecchiature con F-Gas: una per generatore
-- ---------------------------------------------------------------------------

CREATE TABLE impianti.apparecchiature_fgas (
    apparecchiatura_fgas_id   uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id                uuid          NOT NULL,
    generatore_id             uuid          NOT NULL,
    gas_id                    uuid          NOT NULL,
    carica_kg                 numeric(10,3) NOT NULL,
    carica_installazione_kg   numeric(10,3),
    ermetica_etichettata      boolean       NOT NULL DEFAULT false,
    edificio_residenziale     boolean       NOT NULL DEFAULT false,
    sistema_rilevamento       boolean       NOT NULL DEFAULT false,
    -- Dati per la Banca dati FGAS (DPR 146/2018, art. 16, comma 4).
    codice_univoco            text,
    fattura_numero            text,
    fattura_data              date,
    creato_il                 timestamptz   NOT NULL DEFAULT now(),
    creato_da                 uuid,
    modificato_il             timestamptz,
    modificato_da             uuid,
    versione                  integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_apparecchiature_fgas PRIMARY KEY (apparecchiatura_fgas_id),
    CONSTRAINT uq_apparecchiature_fgas_azienda UNIQUE (azienda_id, apparecchiatura_fgas_id),
    CONSTRAINT uq_apparecchiature_fgas_generatore UNIQUE (generatore_id),
    CONSTRAINT fk_apparecchiature_fgas_generatori FOREIGN KEY (azienda_id, generatore_id) REFERENCES impianti.generatori (azienda_id, generatore_id),
    CONSTRAINT fk_apparecchiature_fgas_gas FOREIGN KEY (gas_id) REFERENCES norma.gas_fluorurati (gas_id),
    CONSTRAINT fk_apparecchiature_fgas_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_apparecchiature_fgas_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_apparecchiature_fgas_carica CHECK (carica_kg > 0 AND (carica_installazione_kg IS NULL OR carica_installazione_kg >= 0)),
    CONSTRAINT ck_apparecchiature_fgas_versione CHECK (versione >= 1)
);

COMMENT ON TABLE impianti.apparecchiature_fgas IS 'Gas fluorurato e carica di un generatore, con i dati per controlli delle perdite e Banca dati FGAS';

-- Obbligo e frequenza del controllo delle perdite oggi, dai dati del regolamento:
-- NULL se l'apparecchiatura non è soggetta.
CREATE FUNCTION norma.frequenza_controllo_perdite(p_apparecchiatura_id uuid)
RETURNS integer
LANGUAGE sql
STABLE
AS $$
    WITH a AS (
        SELECT g.allegato,
               ap.ermetica_etichettata,
               ap.edificio_residenziale,
               ap.sistema_rilevamento,
               ap.carica_kg,
               CASE WHEN g.allegato = 'I' THEN ap.carica_kg * g.gwp / 1000 ELSE ap.carica_kg END AS quantita
        FROM impianti.apparecchiature_fgas ap
        JOIN norma.gas_fluorurati g ON g.gas_id = ap.gas_id
        WHERE ap.apparecchiatura_fgas_id = p_apparecchiatura_id
    )
    SELECT CASE WHEN a.sistema_rilevamento THEN r.mesi_con_rilevamento ELSE r.mesi END
    FROM a
    JOIN norma.regole_controllo_perdite r
      ON r.allegato = a.allegato
     AND a.quantita >= r.soglia_da AND (r.soglia_a IS NULL OR a.quantita < r.soglia_a)
     AND r.valido_dal <= current_date AND (r.valido_al IS NULL OR r.valido_al >= current_date)
    WHERE NOT (a.ermetica_etichettata AND EXISTS (
        SELECT 1 FROM norma.esenzioni_ermetiche e
        WHERE e.valido_dal <= current_date AND (e.valido_al IS NULL OR e.valido_al >= current_date)
          AND (e.allegato IS NULL OR e.allegato = a.allegato)
          AND (NOT e.solo_residenziale OR a.edificio_residenziale)
          AND CASE WHEN e.unita = 'kg' THEN a.carica_kg ELSE a.quantita END < e.soglia))
    LIMIT 1
$$;

COMMENT ON FUNCTION norma.frequenza_controllo_perdite(uuid) IS 'Mesi tra due controlli delle perdite (Reg. UE 2024/573, art. 5); NULL se non soggetta';

-- ---------------------------------------------------------------------------
-- Certificati F-Gas dell'impresa e dei tecnici
-- ---------------------------------------------------------------------------

CREATE TABLE sec.certificazioni (
    certificazione_id uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id        uuid        NOT NULL,
    -- NULL: il certificato dell'impresa.
    utente_id         uuid,
    tipo              text        NOT NULL,
    numero            text        NOT NULL,
    ente              text,
    rilasciata_il     date,
    scade_il          date,
    creato_il         timestamptz NOT NULL DEFAULT now(),
    creato_da         uuid,
    modificato_il     timestamptz,
    modificato_da     uuid,
    versione          integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_certificazioni PRIMARY KEY (certificazione_id),
    CONSTRAINT fk_certificazioni_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_certificazioni_utenti FOREIGN KEY (azienda_id, utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT fk_certificazioni_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_certificazioni_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_certificazioni_tipo CHECK (
        (tipo = 'fgas_persona' AND utente_id IS NOT NULL) OR (tipo = 'fgas_impresa' AND utente_id IS NULL)),
    CONSTRAINT ck_certificazioni_numero CHECK (btrim(numero) <> ''),
    CONSTRAINT ck_certificazioni_date CHECK (scade_il IS NULL OR rilasciata_il IS NULL OR scade_il >= rilasciata_il),
    CONSTRAINT ck_certificazioni_versione CHECK (versione >= 1)
);

COMMENT ON TABLE sec.certificazioni IS 'Certificati F-Gas (Reg. UE 2015/2067) dell''impresa e dei tecnici';

-- Numero del certificato in corso a una data; NULL se non ce n'è. Con p_utente NULL, quello dell'impresa.
CREATE FUNCTION sec.certificato_fgas(p_utente_id uuid, p_data date)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT numero
    FROM sec.certificazioni
    WHERE tipo = CASE WHEN p_utente_id IS NULL THEN 'fgas_impresa' ELSE 'fgas_persona' END
      AND utente_id IS NOT DISTINCT FROM p_utente_id
      AND (rilasciata_il IS NULL OR rilasciata_il <= p_data)
      AND (scade_il IS NULL OR scade_il >= p_data)
    ORDER BY scade_il DESC NULLS FIRST, creato_il DESC
    LIMIT 1
$$;

-- Un tecnico senza certificato in corso non si assegna a manutenzioni, riparazioni o
-- installazioni di un impianto con apparecchiature F-Gas (ADR 0008).
CREATE FUNCTION lavoro.controlla_certificato_tecnico()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_data date := coalesce((lower(NEW.finestra) AT TIME ZONE 'Europe/Rome')::date, (now() AT TIME ZONE 'Europe/Rome')::date);
BEGIN
    IF NEW.tecnico_utente_id IS NULL OR NEW.tipo NOT IN ('manutenzione', 'riparazione', 'installazione')
       OR NEW.stato IN ('completato', 'annullato') THEN
        RETURN NEW;
    END IF;

    IF EXISTS (
        SELECT 1 FROM impianti.apparecchiature_fgas ap
        JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
        WHERE g.impianto_id = NEW.impianto_id
          AND g.dismesso_il IS NULL
          AND (NEW.generatore_id IS NULL OR g.generatore_id = NEW.generatore_id)
    ) AND sec.certificato_fgas(NEW.tecnico_utente_id, v_data) IS NULL THEN
        RAISE EXCEPTION 'il tecnico non ha un certificato F-Gas in corso il %', v_data
            USING ERRCODE = 'GF001';
    END IF;

    RETURN NEW;
END
$$;

CREATE TRIGGER tr_interventi_certificato_fgas
    BEFORE INSERT OR UPDATE OF tecnico_utente_id, finestra, tipo, impianto_id, generatore_id ON lavoro.interventi
    FOR EACH ROW EXECUTE FUNCTION lavoro.controlla_certificato_tecnico();

-- ---------------------------------------------------------------------------
-- Registro degli interventi F-Gas (art. 7 del regolamento, art. 16 del DPR)
-- ---------------------------------------------------------------------------

CREATE TABLE lavoro.interventi_fgas (
    intervento_fgas_id       uuid          NOT NULL DEFAULT uuidv7(),
    azienda_id               uuid          NOT NULL,
    apparecchiatura_fgas_id  uuid          NOT NULL,
    intervento_id            uuid          NOT NULL,
    tipo                     text          NOT NULL,
    eseguito_il              date          NOT NULL,
    perdita_rilevata         boolean,
    gas_aggiunto_kg          numeric(10,3) NOT NULL DEFAULT 0,
    gas_recuperato_kg        numeric(10,3) NOT NULL DEFAULT 0,
    gas_riciclato            boolean       NOT NULL DEFAULT false,
    impianto_riciclaggio     text,
    certificato_riciclaggio  text,
    misure_smaltimento       text,
    tecnico_utente_id        uuid          NOT NULL,
    -- Copiati alla registrazione: un rinnovo successivo non cambia lo storico.
    certificato_tecnico      text          NOT NULL,
    certificato_impresa      text,
    note                     text,
    termine_comunicazione    date          NOT NULL,
    comunicato_il            date,
    annullato_il             timestamptz,
    motivo_annullamento      text,
    creato_il                timestamptz   NOT NULL DEFAULT now(),
    creato_da                uuid,
    modificato_il            timestamptz,
    modificato_da            uuid,
    versione                 integer       NOT NULL DEFAULT 1,
    CONSTRAINT pk_interventi_fgas PRIMARY KEY (intervento_fgas_id),
    CONSTRAINT fk_interventi_fgas_apparecchiature FOREIGN KEY (azienda_id, apparecchiatura_fgas_id)
        REFERENCES impianti.apparecchiature_fgas (azienda_id, apparecchiatura_fgas_id),
    CONSTRAINT fk_interventi_fgas_interventi FOREIGN KEY (azienda_id, intervento_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_interventi_fgas_tecnico FOREIGN KEY (azienda_id, tecnico_utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT fk_interventi_fgas_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_interventi_fgas_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_interventi_fgas_tipo CHECK (tipo IN ('installazione', 'controllo_perdite', 'manutenzione', 'riparazione', 'recupero', 'smantellamento')),
    -- L'esito del controllo c'è solo per un controllo delle perdite, e lì è obbligatorio.
    CONSTRAINT ck_interventi_fgas_perdita CHECK ((tipo = 'controllo_perdite') = (perdita_rilevata IS NOT NULL)),
    CONSTRAINT ck_interventi_fgas_quantita CHECK (gas_aggiunto_kg >= 0 AND gas_recuperato_kg >= 0),
    CONSTRAINT ck_interventi_fgas_riciclo CHECK (NOT gas_riciclato OR (gas_aggiunto_kg > 0 AND nullif(btrim(impianto_riciclaggio), '') IS NOT NULL)),
    CONSTRAINT ck_interventi_fgas_smantellamento CHECK (tipo <> 'smantellamento' OR nullif(btrim(misure_smaltimento), '') IS NOT NULL),
    CONSTRAINT ck_interventi_fgas_comunicazione CHECK (comunicato_il IS NULL OR comunicato_il >= eseguito_il),
    CONSTRAINT ck_interventi_fgas_annullamento CHECK ((annullato_il IS NULL) = (motivo_annullamento IS NULL)),
    CONSTRAINT ck_interventi_fgas_versione CHECK (versione >= 1)
);

CREATE INDEX ix_interventi_fgas_apparecchiatura ON lavoro.interventi_fgas (apparecchiatura_fgas_id, eseguito_il DESC);
CREATE INDEX ix_interventi_fgas_da_comunicare ON lavoro.interventi_fgas (azienda_id, termine_comunicazione)
    WHERE comunicato_il IS NULL AND annullato_il IS NULL;

COMMENT ON TABLE lavoro.interventi_fgas IS 'Registro delle operazioni su apparecchiature F-Gas; si annulla, non si cancella';

-- Alla registrazione: certificati in corso, termine dei 30 giorni, intervento dello stesso impianto.
-- Dopo la comunicazione alla Banca dati l'operazione non cambia più.
CREATE FUNCTION lavoro.controlla_intervento_fgas()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD.comunicato_il IS NOT NULL THEN
            RAISE EXCEPTION 'un''operazione F-Gas già comunicata alla Banca dati non si modifica'
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;
        IF OLD.annullato_il IS NOT NULL THEN
            RAISE EXCEPTION 'un''operazione F-Gas annullata non si modifica'
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;
        IF NEW.annullato_il IS NOT NULL AND NEW.comunicato_il IS NOT NULL THEN
            RAISE EXCEPTION 'non si annulla un''operazione nello stesso momento in cui la si comunica'
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM impianti.apparecchiature_fgas ap
        JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
        JOIN lavoro.interventi i ON i.impianto_id = g.impianto_id
        WHERE ap.apparecchiatura_fgas_id = NEW.apparecchiatura_fgas_id AND i.intervento_id = NEW.intervento_id
    ) THEN
        RAISE EXCEPTION 'l''intervento non è dell''impianto dell''apparecchiatura'
            USING ERRCODE = 'foreign_key_violation';
    END IF;

    IF TG_OP = 'INSERT' OR NEW.tecnico_utente_id IS DISTINCT FROM OLD.tecnico_utente_id OR NEW.eseguito_il IS DISTINCT FROM OLD.eseguito_il THEN
        NEW.certificato_tecnico := sec.certificato_fgas(NEW.tecnico_utente_id, NEW.eseguito_il);
        IF NEW.certificato_tecnico IS NULL THEN
            RAISE EXCEPTION 'il tecnico non ha un certificato F-Gas in corso il %', NEW.eseguito_il
                USING ERRCODE = 'GF001';
        END IF;
        NEW.certificato_impresa := sec.certificato_fgas(NULL, NEW.eseguito_il);
    END IF;

    NEW.termine_comunicazione := NEW.eseguito_il + 30;
    RETURN NEW;
END
$$;

CREATE TRIGGER tr_interventi_fgas_controlli
    BEFORE INSERT OR UPDATE ON lavoro.interventi_fgas
    FOR EACH ROW EXECUTE FUNCTION lavoro.controlla_intervento_fgas();

CREATE TRIGGER tr_interventi_fgas_metadati
    BEFORE UPDATE ON lavoro.interventi_fgas
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_interventi_fgas_audit
    AFTER INSERT OR UPDATE ON lavoro.interventi_fgas
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('intervento_fgas_id');

CREATE TRIGGER tr_apparecchiature_fgas_metadati
    BEFORE UPDATE ON impianti.apparecchiature_fgas
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_certificazioni_metadati
    BEFORE UPDATE ON sec.certificazioni
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_certificazioni_audit
    AFTER INSERT OR UPDATE ON sec.certificazioni
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('certificazione_id');

-- ---------------------------------------------------------------------------
-- Scadenza del controllo delle perdite, per impianto
-- ---------------------------------------------------------------------------

ALTER TABLE lavoro.scadenze
    DROP CONSTRAINT ck_scadenze_tipo,
    ADD CONSTRAINT ck_scadenze_tipo CHECK (tipo IN ('manutenzione', 'controllo_efficienza', 'controllo_perdite'));

-- La data è la più vicina tra le apparecchiature soggette dell'impianto: ultimo
-- controllo più la frequenza, oggi se non è mai stato controllato, al più tardi un
-- mese dopo una riparazione seguita da nessun controllo (art. 4, par. 5).
CREATE FUNCTION lavoro.ricalcola_controllo_perdite(p_impianto_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_azienda  uuid;
    v_data     date;
    v_mesi     integer;
    v_origine  uuid;
    v_oggi     date := (now() AT TIME ZONE 'Europe/Rome')::date;
BEGIN
    WITH soggette AS (
        SELECT ap.apparecchiatura_fgas_id, ap.azienda_id, norma.frequenza_controllo_perdite(ap.apparecchiatura_fgas_id) AS mesi
        FROM impianti.apparecchiature_fgas ap
        JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
        JOIN impianti.impianti i ON i.impianto_id = g.impianto_id
        WHERE g.impianto_id = p_impianto_id AND g.dismesso_il IS NULL
          AND i.stato = 'attivo' AND i.eliminato_il IS NULL
          AND NOT EXISTS (SELECT 1 FROM lavoro.interventi_fgas x
                          WHERE x.apparecchiatura_fgas_id = ap.apparecchiatura_fgas_id
                            AND x.tipo = 'smantellamento' AND x.annullato_il IS NULL)
    ),
    dovute AS (
        SELECT s.azienda_id, s.mesi, c.intervento_id,
               least(
                   CASE WHEN c.eseguito_il IS NULL THEN v_oggi ELSE (c.eseguito_il + make_interval(months => s.mesi))::date END,
                   CASE WHEN r.eseguito_il IS NOT NULL AND (c.eseguito_il IS NULL OR r.eseguito_il > c.eseguito_il)
                        THEN (r.eseguito_il + interval '1 month')::date END
               ) AS data_dovuta
        FROM soggette s
        LEFT JOIN LATERAL (
            SELECT x.eseguito_il, x.intervento_id FROM lavoro.interventi_fgas x
            WHERE x.apparecchiatura_fgas_id = s.apparecchiatura_fgas_id AND x.annullato_il IS NULL
              AND (x.tipo = 'controllo_perdite' OR x.tipo = 'installazione')
            ORDER BY x.eseguito_il DESC, x.creato_il DESC LIMIT 1
        ) c ON true
        LEFT JOIN LATERAL (
            SELECT x.eseguito_il FROM lavoro.interventi_fgas x
            WHERE x.apparecchiatura_fgas_id = s.apparecchiatura_fgas_id AND x.annullato_il IS NULL AND x.tipo = 'riparazione'
            ORDER BY x.eseguito_il DESC LIMIT 1
        ) r ON true
        WHERE s.mesi IS NOT NULL
    )
    SELECT azienda_id, data_dovuta, mesi, intervento_id
    INTO v_azienda, v_data, v_mesi, v_origine
    FROM dovute
    ORDER BY data_dovuta, mesi
    LIMIT 1;

    IF v_data IS NULL THEN
        PERFORM lavoro.annulla_scadenze(p_impianto_id, 'controllo_perdite', 'Nessuna apparecchiatura soggetta al controllo delle perdite');
        RETURN;
    END IF;

    UPDATE lavoro.scadenze
    SET data_scadenza         = CASE WHEN data_manuale THEN data_scadenza ELSE v_data END,
        periodicita_mesi      = v_mesi,
        intervento_origine_id = v_origine,
        modificato_da         = sec.utente_corrente()
    WHERE impianto_id = p_impianto_id AND tipo = 'controllo_perdite' AND stato = 'aperta'
      AND (data_scadenza, periodicita_mesi, intervento_origine_id)
          IS DISTINCT FROM (CASE WHEN data_manuale THEN data_scadenza ELSE v_data END, v_mesi, v_origine);

    IF NOT FOUND AND NOT EXISTS (
        SELECT 1 FROM lavoro.scadenze WHERE impianto_id = p_impianto_id AND tipo = 'controllo_perdite' AND stato = 'aperta'
    ) THEN
        INSERT INTO lavoro.scadenze (azienda_id, impianto_id, tipo, data_scadenza, periodicita_mesi, intervento_origine_id, creato_da)
        VALUES (v_azienda, p_impianto_id, 'controllo_perdite', v_data, v_mesi, v_origine, sec.utente_corrente());
    END IF;
END
$$;

COMMENT ON FUNCTION lavoro.ricalcola_controllo_perdite(uuid) IS 'Scadenza del controllo delle perdite dell''impianto dalle sue apparecchiature F-Gas (Reg. UE 2024/573, artt. 4 e 5)';

-- Il ricalcolo dell'impianto ora comprende il controllo delle perdite.
CREATE OR REPLACE FUNCTION lavoro.ricalcola_scadenze(p_impianto_id uuid)
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

    -- Controllo delle perdite delle apparecchiature F-Gas
    PERFORM lavoro.ricalcola_controllo_perdite(p_impianto_id);
END
$$;

-- Un controllo delle perdite chiude la scadenza aperta con il suo intervento; poi si ricalcola.
CREATE FUNCTION lavoro.aggiorna_scadenze_fgas()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_impianto uuid;
BEGIN
    SELECT g.impianto_id INTO v_impianto
    FROM impianti.apparecchiature_fgas ap JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
    WHERE ap.apparecchiatura_fgas_id = NEW.apparecchiatura_fgas_id;

    IF TG_OP = 'INSERT' AND NEW.tipo = 'controllo_perdite' THEN
        UPDATE lavoro.scadenze
        SET stato = 'chiusa', chiusa_il = now(), intervento_chiusura_id = NEW.intervento_id, modificato_da = sec.utente_corrente()
        WHERE impianto_id = v_impianto AND tipo = 'controllo_perdite' AND stato = 'aperta';
    END IF;

    PERFORM lavoro.ricalcola_controllo_perdite(v_impianto);
    RETURN NULL;
END
$$;

CREATE TRIGGER tr_interventi_fgas_scadenze
    AFTER INSERT OR UPDATE ON lavoro.interventi_fgas
    FOR EACH ROW EXECUTE FUNCTION lavoro.aggiorna_scadenze_fgas();

CREATE FUNCTION lavoro.ricalcola_scadenze_apparecchiatura()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM lavoro.ricalcola_controllo_perdite(g.impianto_id)
    FROM impianti.generatori g
    WHERE g.generatore_id = coalesce(NEW.generatore_id, OLD.generatore_id);
    RETURN NULL;
END
$$;

CREATE TRIGGER tr_apparecchiature_fgas_scadenze
    AFTER INSERT OR UPDATE OR DELETE ON impianti.apparecchiature_fgas
    FOR EACH ROW EXECUTE FUNCTION lavoro.ricalcola_scadenze_apparecchiatura();

CREATE TRIGGER tr_generatori_scadenze_fgas
    AFTER UPDATE OF dismesso_il ON impianti.generatori
    FOR EACH ROW EXECUTE FUNCTION lavoro.ricalcola_scadenze_apparecchiatura();

-- ---------------------------------------------------------------------------
-- Dati per la Banca dati FGAS (DPR 146/2018, art. 16)
-- ---------------------------------------------------------------------------

-- Una riga per operazione con quello che il portale chiede: operatore, luogo,
-- apparecchiatura, gas, quantità, riciclaggio, certificati e termine dei 30 giorni.
CREATE FUNCTION lavoro.comunicazioni_fgas(p_solo_da_comunicare boolean)
RETURNS TABLE (
    intervento_fgas_id      uuid,
    versione                integer,
    tipo                    text,
    eseguito_il             date,
    termine_comunicazione   date,
    comunicato_il           date,
    impianto_id             uuid,
    operatore               text,
    codice_fiscale          text,
    partita_iva             text,
    luogo                   text,
    tipologia               text,
    codice_univoco          text,
    matricola               text,
    installato_il           date,
    fattura_numero          text,
    fattura_data            date,
    gas                     text,
    carica_kg               numeric,
    gas_aggiunto_kg         numeric,
    gas_recuperato_kg       numeric,
    impianto_riciclaggio    text,
    certificato_riciclaggio text,
    misure_smaltimento      text,
    certificato_tecnico     text,
    certificato_impresa     text,
    tecnico                 text,
    note                    text
)
LANGUAGE sql
STABLE
AS $$
    SELECT f.intervento_fgas_id, f.versione, f.tipo, f.eseguito_il, f.termine_comunicazione, f.comunicato_il,
           g.impianto_id,
           CASE WHEN c.tipo = 'privato' THEN btrim(c.cognome || ' ' || c.nome) ELSE c.denominazione END,
           c.codice_fiscale, c.partita_iva,
           btrim(se.indirizzo || ' ' || coalesce(se.civico, '')) || ', ' || se.cap || ' ' || se.comune || ' (' || se.provincia || ')',
           tg.nome, ap.codice_univoco, g.matricola, g.installato_il, ap.fattura_numero, ap.fattura_data,
           gf.codice, ap.carica_kg, f.gas_aggiunto_kg, f.gas_recuperato_kg,
           f.impianto_riciclaggio, f.certificato_riciclaggio, f.misure_smaltimento,
           f.certificato_tecnico, f.certificato_impresa,
           btrim(u.nome || ' ' || u.cognome), f.note
    FROM lavoro.interventi_fgas f
    JOIN impianti.apparecchiature_fgas ap ON ap.apparecchiatura_fgas_id = f.apparecchiatura_fgas_id
    JOIN norma.gas_fluorurati gf ON gf.gas_id = ap.gas_id
    JOIN impianti.generatori g ON g.generatore_id = ap.generatore_id
    JOIN impianti.tipi_generatore tg ON tg.tipo_generatore_id = g.tipo_generatore_id
    JOIN impianti.impianti i ON i.impianto_id = g.impianto_id
    JOIN anagrafica.sedi se ON se.sede_id = i.sede_id
    JOIN anagrafica.clienti c ON c.cliente_id = coalesce(i.responsabile_cliente_id, i.proprietario_cliente_id, se.cliente_id)
    JOIN sec.utenti u ON u.utente_id = f.tecnico_utente_id
    WHERE f.annullato_il IS NULL
      AND (NOT p_solo_da_comunicare OR f.comunicato_il IS NULL)
    ORDER BY f.termine_comunicazione, f.eseguito_il, f.intervento_fgas_id
$$;

COMMENT ON FUNCTION lavoro.comunicazioni_fgas(boolean) IS 'Dati delle operazioni F-Gas da comunicare alla Banca dati FGAS (DPR 146/2018, art. 16)';

-- ---------------------------------------------------------------------------
-- Sicurezza
-- ---------------------------------------------------------------------------

ALTER TABLE impianti.apparecchiature_fgas ENABLE ROW LEVEL SECURITY;
ALTER TABLE lavoro.interventi_fgas        ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.certificazioni            ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_apparecchiature_fgas ON impianti.apparecchiature_fgas TO app_api
    USING (azienda_id = sec.azienda_corrente()) WITH CHECK (azienda_id = sec.azienda_corrente());
CREATE POLICY rls_interventi_fgas ON lavoro.interventi_fgas TO app_api
    USING (azienda_id = sec.azienda_corrente()) WITH CHECK (azienda_id = sec.azienda_corrente());
CREATE POLICY rls_certificazioni ON sec.certificazioni TO app_api
    USING (azienda_id = sec.azienda_corrente()) WITH CHECK (azienda_id = sec.azienda_corrente());

GRANT SELECT ON norma.gas_fluorurati, norma.gas_componenti, norma.regole_controllo_perdite, norma.esenzioni_ermetiche TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON impianti.apparecchiature_fgas TO app_api;
GRANT SELECT, INSERT, UPDATE ON lavoro.interventi_fgas TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON sec.certificazioni TO app_api;

REVOKE EXECUTE ON FUNCTION
    norma.frequenza_controllo_perdite(uuid),
    sec.certificato_fgas(uuid, date),
    lavoro.ricalcola_controllo_perdite(uuid),
    lavoro.comunicazioni_fgas(boolean)
FROM PUBLIC;
GRANT EXECUTE ON FUNCTION
    norma.frequenza_controllo_perdite(uuid),
    sec.certificato_fgas(uuid, date),
    lavoro.ricalcola_controllo_perdite(uuid),
    lavoro.comunicazioni_fgas(boolean)
TO app_api;

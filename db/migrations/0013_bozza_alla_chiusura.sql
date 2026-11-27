-- 0013: bozza del rapporto di controllo alla chiusura dell'intervento (M4).
--
-- Chi chiude una manutenzione con il controllo di efficienza sceglie il
-- modello di RCEE e il generatore: la chiusura crea la bozza precompilata
-- nella stessa transazione. Il modello non si ricava dal tipo di generatore,
-- che per ora ha solo voci di prova: lo sceglie chi ha fatto il controllo.

-- I due parametri nuovi hanno un valore predefinito: le chiamate con sei
-- argomenti continuano a funzionare e non creano bozze.
DROP FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean);

-- In una transazione: sostituisce le righe, registra esito e note, chiude.
-- Una manutenzione con esito risolto chiude la scadenza di manutenzione e, con
-- p_controllo_efficienza, quella di controllo; poi le scadenze si ricalcolano
-- a partire da questo intervento. Con p_modello_rapporto crea anche la bozza
-- del rapporto, sul generatore indicato o su quello dell'intervento, salvo che
-- l'intervento ne abbia già uno vivo per lo stesso generatore. Restituisce
-- false se l'intervento non esiste o la versione non coincide.
CREATE FUNCTION lavoro.chiudi_intervento(
    p_intervento_id         uuid,
    p_versione              integer,
    p_esito                 text,
    p_note_chiusura         text,
    p_righe                 jsonb,
    p_controllo_efficienza  boolean DEFAULT false,
    p_modello_rapporto      text DEFAULT NULL,
    p_generatore_rapporto   uuid DEFAULT NULL
)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_intervento lavoro.interventi%ROWTYPE;
    v_generatore uuid;
    v_oggi       date := (now() AT TIME ZONE 'Europe/Rome')::date;
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

    IF p_modello_rapporto IS NOT NULL THEN
        IF NOT coalesce(p_controllo_efficienza, false) THEN
            RAISE EXCEPTION 'la bozza del rapporto nasce solo con il controllo di efficienza'
                USING ERRCODE = 'invalid_parameter_value';
        END IF;

        v_generatore := coalesce(p_generatore_rapporto, v_intervento.generatore_id);
        IF v_generatore IS NULL THEN
            RAISE EXCEPTION 'per la bozza del rapporto serve il generatore controllato'
                USING ERRCODE = 'invalid_parameter_value';
        END IF;
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

    -- La data del controllo è il giorno dell'intervento in calendario, mai
    -- futura; il compilatore è il tecnico assegnato, altrimenti chi chiude.
    IF v_generatore IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM norma.rapporti_controllo
        WHERE intervento_id = p_intervento_id AND generatore_id = v_generatore AND stato <> 'annullato'
    ) THEN
        PERFORM norma.crea_rapporto(
            v_intervento.impianto_id, v_generatore, p_intervento_id, p_modello_rapporto,
            least(coalesce((lower(v_intervento.finestra) AT TIME ZONE 'Europe/Rome')::date, v_oggi), v_oggi),
            coalesce(v_intervento.tecnico_utente_id, sec.utente_corrente()));
    END IF;

    RETURN true;
END
$$;

COMMENT ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean, text, uuid)
    IS 'Chiude un intervento con esito e righe, aggiorna le scadenze e, con il controllo di efficienza, crea la bozza del rapporto';

REVOKE EXECUTE ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean, text, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION lavoro.chiudi_intervento(uuid, integer, text, text, jsonb, boolean, text, uuid) TO app_api;

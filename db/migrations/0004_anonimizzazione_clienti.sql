-- 0004: anonimizzazione di un cliente su richiesta dell'interessato (GDPR, diritto
-- alla cancellazione). Scelte e limiti nell'ADR 0004.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- ---------------------------------------------------------------------------
-- Clienti: stato di anonimizzazione
-- ---------------------------------------------------------------------------

ALTER TABLE anagrafica.clienti
    ADD COLUMN anonimizzato_il timestamptz,
    ADD COLUMN anonimizzato_da uuid,
    ADD CONSTRAINT fk_clienti_anonimizzato_da FOREIGN KEY (anonimizzato_da) REFERENCES sec.utenti (utente_id);

-- Un cliente anonimizzato non ha più un nominativo: il vincolo vale solo per gli altri.
ALTER TABLE anagrafica.clienti DROP CONSTRAINT ck_clienti_nominativo;

ALTER TABLE anagrafica.clienti
    ADD CONSTRAINT ck_clienti_nominativo CHECK (
        anonimizzato_il IS NOT NULL
        OR CASE tipo
               WHEN 'privato' THEN nullif(btrim(nome), '') IS NOT NULL AND nullif(btrim(cognome), '') IS NOT NULL
               ELSE nullif(btrim(denominazione), '') IS NOT NULL
           END
    ),
    -- Anonimizzato vuol dire: nessun dato che identifichi la persona, ed eliminato dagli elenchi.
    ADD CONSTRAINT ck_clienti_anonimizzato CHECK (
        anonimizzato_il IS NULL
        OR (denominazione IS NULL AND nome IS NULL AND cognome IS NULL AND codice_fiscale IS NULL
            AND partita_iva IS NULL AND pec IS NULL AND note IS NULL AND eliminato_il IS NOT NULL)
    ),
    ADD CONSTRAINT ck_clienti_anonimizzato_da CHECK (anonimizzato_da IS NULL OR anonimizzato_il IS NOT NULL);

COMMENT ON COLUMN anagrafica.clienti.anonimizzato_il IS 'Quando i dati personali sono stati cancellati; da allora la riga non si modifica più';

-- L'anonimizzazione non si annulla: la riga resta solo come riferimento per i documenti.
CREATE FUNCTION anagrafica.blocca_modifica_anonimizzato()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.anonimizzato_il IS NOT NULL THEN
        RAISE EXCEPTION 'il cliente % è anonimizzato e non si modifica più', OLD.cliente_id
            USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN NEW;
END
$$;

-- Il nome del trigger lo fa scattare prima di tr_clienti_metadati (ordine alfabetico).
CREATE TRIGGER tr_clienti_anonimizzato
    BEFORE UPDATE ON anagrafica.clienti
    FOR EACH ROW EXECUTE FUNCTION anagrafica.blocca_modifica_anonimizzato();

-- ---------------------------------------------------------------------------
-- Consensi: lo storico resta immutabile, tranne la cancellazione delle note
-- ---------------------------------------------------------------------------

-- Le note sono testo libero e possono contenere dati personali. Finalità, esito,
-- fonte e data restano: dimostrano come è stato trattato il consenso.
CREATE FUNCTION anagrafica.consenti_solo_cancellazione_note()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_atteso anagrafica.consensi_privacy := NEW;
BEGIN
    v_atteso.note := OLD.note;
    IF NEW.note IS NULL AND v_atteso IS NOT DISTINCT FROM OLD THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION '%.% è uno storico: si possono solo cancellare le note', TG_TABLE_SCHEMA, TG_TABLE_NAME
        USING ERRCODE = 'insufficient_privilege';
END
$$;

DROP TRIGGER tr_consensi_privacy_storico ON anagrafica.consensi_privacy;

CREATE TRIGGER tr_consensi_privacy_storico
    BEFORE DELETE ON anagrafica.consensi_privacy
    FOR EACH ROW EXECUTE FUNCTION anagrafica.blocca_modifica_storico();

CREATE TRIGGER tr_consensi_privacy_solo_note
    BEFORE UPDATE ON anagrafica.consensi_privacy
    FOR EACH ROW EXECUTE FUNCTION anagrafica.consenti_solo_cancellazione_note();

-- ---------------------------------------------------------------------------
-- Funzione di anonimizzazione
-- ---------------------------------------------------------------------------

-- Cancella i dati personali del cliente e lo elimina dagli elenchi, in una sola
-- transazione e con controllo di versione. Restituisce false se il cliente non
-- esiste, è già anonimizzato o la versione non coincide.
--   - cliente: nominativo, codici fiscali, PEC e note a NULL;
--   - contatti: cancellati;
--   - sedi: restano indirizzo e coordinate, che descrivono dove si trova un impianto
--     e servono ai documenti normativi; descrizione e note di accesso a NULL;
--   - consensi: resta lo storico, senza note.
-- Gira con i privilegi di chi la chiama (app_api), quindi sotto Row-Level Security.
-- Quando arriveranno impianti e documenti (M2-M4) va estesa: i documenti con valore
-- legale conservano la propria copia dei dati, come richiesto dalla normativa.
CREATE FUNCTION anagrafica.anonimizza_cliente(p_cliente_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_azienda_id uuid;
    v_contatti   integer;
    v_sedi       integer;
    v_note       integer;
BEGIN
    UPDATE anagrafica.clienti
    SET denominazione   = NULL,
        nome            = NULL,
        cognome         = NULL,
        codice_fiscale  = NULL,
        partita_iva     = NULL,
        pec             = NULL,
        note            = NULL,
        eliminato_il    = coalesce(eliminato_il, now()),
        eliminato_da    = coalesce(eliminato_da, sec.utente_corrente()),
        anonimizzato_il = now(),
        anonimizzato_da = sec.utente_corrente(),
        modificato_da   = sec.utente_corrente()
    WHERE cliente_id = p_cliente_id
      AND versione = p_versione
      AND anonimizzato_il IS NULL
    RETURNING azienda_id INTO v_azienda_id;

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    DELETE FROM anagrafica.contatti WHERE cliente_id = p_cliente_id;
    GET DIAGNOSTICS v_contatti = ROW_COUNT;

    UPDATE anagrafica.sedi
    SET descrizione   = NULL,
        note_accesso  = NULL,
        eliminata_il  = coalesce(eliminata_il, now()),
        eliminata_da  = coalesce(eliminata_da, sec.utente_corrente()),
        modificato_da = sec.utente_corrente()
    WHERE cliente_id = p_cliente_id;
    GET DIAGNOSTICS v_sedi = ROW_COUNT;

    UPDATE anagrafica.consensi_privacy
    SET note = NULL
    WHERE cliente_id = p_cliente_id AND note IS NOT NULL;
    GET DIAGNOSTICS v_note = ROW_COUNT;

    -- Nell'audit solo numeri: nessun dato personale del cliente.
    INSERT INTO sec.audit_log (azienda_id, utente_id, azione, entita, entita_id, dettaglio)
    VALUES (v_azienda_id, sec.utente_corrente(), 'cliente.anonimizzato', 'clienti', p_cliente_id,
            jsonb_build_object('contatti_cancellati', v_contatti, 'sedi_ripulite', v_sedi, 'note_consensi_cancellate', v_note));

    RETURN true;
END
$$;

COMMENT ON FUNCTION anagrafica.anonimizza_cliente(uuid, integer) IS 'Diritto alla cancellazione: anonimizza il cliente e lascia i riferimenti per i documenti';

-- ---------------------------------------------------------------------------
-- Privilegi e permesso
-- ---------------------------------------------------------------------------

REVOKE ALL ON FUNCTION
    anagrafica.blocca_modifica_anonimizzato(),
    anagrafica.consenti_solo_cancellazione_note(),
    anagrafica.anonimizza_cliente(uuid, integer)
FROM PUBLIC;

GRANT EXECUTE ON FUNCTION anagrafica.anonimizza_cliente(uuid, integer) TO app_api;

-- Solo la colonna note: il trigger accetta un solo cambiamento, la cancellazione.
GRANT UPDATE (note) ON anagrafica.consensi_privacy TO app_api;

-- Azione irreversibile: per ora solo il Titolare, che rappresenta il titolare del trattamento.
INSERT INTO sec.permessi (codice, descrizione) VALUES
    ('clienti.anonimizzazione', 'Anonimizzare un cliente su richiesta dell''interessato (GDPR)');

INSERT INTO sec.ruoli_permessi (ruolo_id, permesso_id, ambito)
SELECT r.ruolo_id, p.permesso_id, 'tutti'
FROM sec.ruoli r
JOIN sec.permessi p ON p.codice = 'clienti.anonimizzazione'
WHERE r.codice = 'titolare' AND r.azienda_id IS NULL;

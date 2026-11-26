-- 0011: bozza del rapporto di controllo precompilata dalle anagrafiche ed esiti
-- del report SIERT riportati sui rapporti.
--
-- La bozza copia in `dati` ciò che le anagrafiche già sanno (codice catasto,
-- comune, indirizzo, responsabile, generatore, tecnico): i valori restano
-- modificabili finché il rapporto è in bozza e da lì in poi sono quelli della
-- copia firmata. Nessun valore viene trasformato (maiuscole, abbreviazioni):
-- la guida SIERT non lo chiede e il CIT confronta i dati con i propri.
--
-- Esiti del report: importato e importato in bozza (da confermare sul CIT)
-- rendono il rapporto trasmesso; scartato lo rende scartato, e potrà entrare
-- in un invio nuovo. Un invio preparato e mai caricato si annulla e libera i
-- suoi rapporti.
--
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- Un generatore ha un solo rapporto vivo per intervento.
CREATE UNIQUE INDEX uq_rapporti_controllo_intervento_generatore
    ON norma.rapporti_controllo (intervento_id, generatore_id)
    WHERE intervento_id IS NOT NULL AND stato <> 'annullato';

-- Crea la bozza e restituisce il rapporto. Il responsabile è quello indicato
-- sull'impianto, altrimenti il proprietario, altrimenti il cliente della sede.
CREATE FUNCTION norma.crea_rapporto(
    p_impianto_id   uuid,
    p_generatore_id uuid,
    p_intervento_id uuid,
    p_modello       text,
    p_data          date,
    p_tecnico_id    uuid
)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_modello  norma.modelli_rapporto%ROWTYPE;
    v_dati     jsonb;
    v_azienda  uuid;
    v_rapporto uuid;
BEGIN
    SELECT * INTO v_modello FROM norma.modelli_rapporto WHERE codice = p_modello;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'modello di rapporto % sconosciuto', p_modello
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    SELECT i.azienda_id,
           jsonb_strip_nulls(jsonb_build_object(
               'codice_catasto',        rc.codice,
               'potenza_totale',        i.potenza_utile_kw,
               'comune',                s.codice_istat_comune,
               'indirizzo_impianto',    concat_ws(', ', s.indirizzo, nullif(btrim(s.civico), '')),
               'responsabile_impianto', CASE WHEN c.tipo = 'privato' THEN c.cognome || ' ' || c.nome END,
               'codice_fiscale',        c.codice_fiscale,
               'ragione_sociale',       CASE WHEN c.tipo <> 'privato' THEN c.denominazione END,
               'partita_iva',           CASE WHEN c.tipo <> 'privato' THEN c.partita_iva END,
               'data_installazione',    to_char(g.installato_il, 'YYYY-MM-DD'),
               'fabbricante',           g.marca,
               'modello',               g.modello,
               'matricola',             g.matricola,
               'gruppo',                g.sigla_catasto,
               'potenza_utile',         CASE WHEN v_modello.codice = 'tipo_1' THEN g.potenza_kw END,
               'manutentore',           (SELECT u.cognome || ' ' || u.nome FROM sec.utenti u WHERE u.utente_id = p_tecnico_id)
           ))
    INTO v_azienda, v_dati
    FROM impianti.impianti i
    JOIN anagrafica.sedi s ON s.sede_id = i.sede_id
    JOIN impianti.generatori g ON g.generatore_id = p_generatore_id AND g.impianto_id = i.impianto_id
    LEFT JOIN anagrafica.clienti c ON c.cliente_id = coalesce(i.responsabile_cliente_id, i.proprietario_cliente_id, s.cliente_id)
    LEFT JOIN impianti.riferimenti_catasto rc ON rc.impianto_id = i.impianto_id AND rc.sistema = 'siert'
    WHERE i.impianto_id = p_impianto_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'impianto o generatore non trovati'
            USING ERRCODE = 'no_data_found';
    END IF;

    INSERT INTO norma.rapporti_controllo (
        azienda_id, impianto_id, generatore_id, intervento_id, modello_rapporto_id,
        tecnico_id, data_controllo, dati, creato_da
    )
    VALUES (
        v_azienda, p_impianto_id, p_generatore_id, p_intervento_id, v_modello.modello_rapporto_id,
        p_tecnico_id, p_data, v_dati, sec.utente_corrente()
    )
    RETURNING rapporto_controllo_id INTO v_rapporto;

    RETURN v_rapporto;
END
$$;

COMMENT ON FUNCTION norma.crea_rapporto(uuid, uuid, uuid, text, date, uuid) IS 'Crea la bozza di un RCEE precompilata dalle anagrafiche';

-- Registra il report del SIERT: un esito per ogni riga dell'invio, poi l'invio
-- è elaborato e i rapporti cambiano stato. p_esiti è un array JSON di oggetti
-- con rapporto_controllo_id, esito e causa. Restituisce false se l'invio non
-- esiste o la versione non coincide.
CREATE FUNCTION norma.registra_esiti_invio(p_invio_id uuid, p_versione integer, p_esiti jsonb)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_invio   norma.invii_catasto%ROWTYPE;
    v_mancanti integer;
BEGIN
    SELECT * INTO v_invio FROM norma.invii_catasto WHERE invio_catasto_id = p_invio_id FOR UPDATE;

    IF NOT FOUND OR v_invio.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_invio.stato <> 'caricato' THEN
        RAISE EXCEPTION 'gli esiti si registrano su un invio caricato, non %', v_invio.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF p_esiti IS NULL OR jsonb_typeof(p_esiti) <> 'array' THEN
        RAISE EXCEPTION 'gli esiti devono essere un array JSON'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    UPDATE norma.invii_rapporti ir
    SET esito = e.esito, causa = e.causa, modificato_da = sec.utente_corrente()
    FROM jsonb_to_recordset(p_esiti) AS e (rapporto_controllo_id uuid, esito text, causa text)
    WHERE ir.invio_catasto_id = p_invio_id
      AND ir.rapporto_controllo_id = e.rapporto_controllo_id;

    SELECT count(*) INTO v_mancanti
    FROM norma.invii_rapporti
    WHERE invio_catasto_id = p_invio_id AND esito = 'in_attesa';

    IF v_mancanti > 0 THEN
        RAISE EXCEPTION 'mancano gli esiti di % righe dell''invio', v_mancanti
            USING ERRCODE = 'check_violation';
    END IF;

    UPDATE norma.rapporti_controllo r
    SET stato = CASE WHEN ir.esito = 'scartato' THEN 'scartato' ELSE 'trasmesso' END,
        modificato_da = sec.utente_corrente()
    FROM norma.invii_rapporti ir
    WHERE ir.invio_catasto_id = p_invio_id
      AND r.rapporto_controllo_id = ir.rapporto_controllo_id;

    UPDATE norma.invii_catasto
    SET stato = 'elaborato', elaborato_il = now(), modificato_da = sec.utente_corrente()
    WHERE invio_catasto_id = p_invio_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.registra_esiti_invio(uuid, integer, jsonb) IS 'Registra il report SIERT di un invio e aggiorna lo stato dei rapporti';

-- Annulla un invio preparato e mai caricato: le sue righe passano a scartato
-- con la causa, così i rapporti possono entrare in un invio nuovo. I rapporti
-- restano firmati o scartati come erano.
CREATE FUNCTION norma.annulla_invio(p_invio_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_invio norma.invii_catasto%ROWTYPE;
BEGIN
    SELECT * INTO v_invio FROM norma.invii_catasto WHERE invio_catasto_id = p_invio_id FOR UPDATE;

    IF NOT FOUND OR v_invio.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_invio.stato <> 'preparato' THEN
        RAISE EXCEPTION 'si annulla solo un invio preparato, non %', v_invio.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    UPDATE norma.invii_rapporti
    SET esito = 'scartato', causa = 'Invio annullato prima del caricamento', modificato_da = sec.utente_corrente()
    WHERE invio_catasto_id = p_invio_id;

    UPDATE norma.invii_catasto
    SET stato = 'annullato', modificato_da = sec.utente_corrente()
    WHERE invio_catasto_id = p_invio_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.annulla_invio(uuid, integer) IS 'Annulla un invio mai caricato e libera i suoi rapporti';

REVOKE EXECUTE ON FUNCTION
    norma.crea_rapporto(uuid, uuid, uuid, text, date, uuid),
    norma.registra_esiti_invio(uuid, integer, jsonb),
    norma.annulla_invio(uuid, integer)
FROM PUBLIC;

GRANT EXECUTE ON FUNCTION
    norma.crea_rapporto(uuid, uuid, uuid, text, date, uuid),
    norma.registra_esiti_invio(uuid, integer, jsonb),
    norma.annulla_invio(uuid, integer)
TO app_api;

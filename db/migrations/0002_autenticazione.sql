-- 0002: autenticazione (ADR 0002).
-- Email unica globale, sessioni di rinnovo a rotazione, codici di recupero del
-- secondo fattore, blocco progressivo, primo avvio e audit dei ruoli.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- Le funzioni nuove non sono eseguibili da PUBLIC: i permessi si danno uno per uno.
ALTER DEFAULT PRIVILEGES IN SCHEMA sec REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Utenti e ruoli
-- ---------------------------------------------------------------------------

-- Si accede con email e password, senza codice azienda: l'email è unica ovunque.
DROP INDEX sec.uq_utenti_azienda_email;
CREATE UNIQUE INDEX uq_utenti_email ON sec.utenti (lower(email));

-- Ultimo passo TOTP accettato: lo stesso codice non vale due volte.
ALTER TABLE sec.utenti
    ADD COLUMN mfa_ultimo_passo bigint,
    ADD CONSTRAINT ck_utenti_mfa_ultimo_passo CHECK (mfa_ultimo_passo >= 0);

-- Quali ruoli obbligano al secondo fattore: è un dato, non codice.
ALTER TABLE sec.ruoli
    ADD COLUMN richiede_mfa boolean NOT NULL DEFAULT false;

UPDATE sec.ruoli
SET richiede_mfa = true
WHERE azienda_id IS NULL
  AND codice IN ('titolare', 'amministratore_sistema');

-- ---------------------------------------------------------------------------
-- Sessioni di rinnovo
-- ---------------------------------------------------------------------------

-- Ogni login apre una famiglia; ogni rinnovo chiude il token usato e ne crea
-- uno nuovo nella stessa famiglia, con la stessa scadenza assoluta. Il token
-- non è mai salvato: solo il suo hash SHA-256.
CREATE TABLE sec.sessioni_rinnovo (
    sessione_id   uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id    uuid        NOT NULL,
    utente_id     uuid        NOT NULL,
    famiglia_id   uuid        NOT NULL DEFAULT uuidv7(),
    token_hash    bytea       NOT NULL,
    creata_il     timestamptz NOT NULL DEFAULT now(),
    scade_il      timestamptz NOT NULL,
    usata_il      timestamptz,
    revocata_il   timestamptz,
    motivo_revoca text,
    dispositivo   text,
    indirizzo     inet,
    CONSTRAINT pk_sessioni_rinnovo PRIMARY KEY (sessione_id),
    CONSTRAINT uq_sessioni_rinnovo_token_hash UNIQUE (token_hash),
    CONSTRAINT fk_sessioni_rinnovo_utenti FOREIGN KEY (azienda_id, utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT ck_sessioni_rinnovo_token_hash CHECK (octet_length(token_hash) = 32),
    CONSTRAINT ck_sessioni_rinnovo_scadenza CHECK (scade_il > creata_il),
    CONSTRAINT ck_sessioni_rinnovo_motivo CHECK (motivo_revoca IN ('uscita', 'riuso', 'utente_non_attivo', 'amministratore')),
    CONSTRAINT ck_sessioni_rinnovo_revoca CHECK ((revocata_il IS NULL) = (motivo_revoca IS NULL))
);

CREATE INDEX ix_sessioni_rinnovo_famiglia ON sec.sessioni_rinnovo (famiglia_id);
CREATE INDEX ix_sessioni_rinnovo_utente_attive ON sec.sessioni_rinnovo (utente_id) WHERE revocata_il IS NULL;

COMMENT ON TABLE sec.sessioni_rinnovo IS 'Token di rinnovo (solo hash), ruotati a ogni uso; il riuso revoca la famiglia';

-- ---------------------------------------------------------------------------
-- Codici di recupero del secondo fattore
-- ---------------------------------------------------------------------------

CREATE TABLE sec.codici_recupero (
    codice_id   uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id  uuid        NOT NULL,
    utente_id   uuid        NOT NULL,
    codice_hash bytea       NOT NULL,
    creato_il   timestamptz NOT NULL DEFAULT now(),
    usato_il    timestamptz,
    CONSTRAINT pk_codici_recupero PRIMARY KEY (codice_id),
    CONSTRAINT uq_codici_recupero_utente_hash UNIQUE (utente_id, codice_hash),
    CONSTRAINT fk_codici_recupero_utenti FOREIGN KEY (azienda_id, utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT ck_codici_recupero_codice_hash CHECK (octet_length(codice_hash) = 32)
);

COMMENT ON TABLE sec.codici_recupero IS 'Codici monouso per accedere senza app TOTP; solo hash SHA-256';

-- ---------------------------------------------------------------------------
-- Funzioni
-- ---------------------------------------------------------------------------

-- Utente della transazione corrente, impostato dall'API con SET LOCAL app.utente_id.
CREATE FUNCTION sec.utente_corrente()
RETURNS uuid
LANGUAGE sql
STABLE
PARALLEL SAFE
AS $$
    SELECT NULLIF(current_setting('app.utente_id', true), '')::uuid
$$;

COMMENT ON FUNCTION sec.utente_corrente() IS 'Utente impostato con SET LOCAL app.utente_id; NULL se assente';

-- Dati per verificare le credenziali. SECURITY DEFINER perché prima del login
-- l'azienda non è nota e la RLS non mostrerebbe nulla. Non restituisce il
-- segreto MFA: quello si legge dopo, sotto RLS.
CREATE FUNCTION sec.dati_accesso(p_email text)
RETURNS TABLE (
    utente_id        uuid,
    azienda_id       uuid,
    password_hash    text,
    stato            text,
    mfa_attivo       boolean,
    bloccato_fino_al timestamptz
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT u.utente_id, u.azienda_id, u.password_hash, u.stato, u.mfa_attivo, u.bloccato_fino_al
    FROM sec.utenti u
    WHERE lower(u.email) = lower(btrim(p_email))
$$;

COMMENT ON FUNCTION sec.dati_accesso(text) IS 'Credenziali per email, prima che l''azienda sia nota';

-- Esito di un tentativo di accesso. Dal quinto errore consecutivo l'account si
-- blocca per 1, 2, 4... minuti, fino a un massimo di un'ora. Gira sotto RLS:
-- l'API imposta prima l'azienda dell'utente. Restituisce il blocco attivo.
CREATE FUNCTION sec.registra_esito_accesso(p_utente_id uuid, p_riuscito boolean)
RETURNS timestamptz
LANGUAGE plpgsql
AS $$
DECLARE
    v_bloccato_fino_al timestamptz;
BEGIN
    IF p_riuscito THEN
        UPDATE sec.utenti
        SET tentativi_falliti = 0,
            bloccato_fino_al  = NULL,
            ultimo_accesso_il = now()
        WHERE utente_id = p_utente_id;

        RETURN NULL;
    END IF;

    UPDATE sec.utenti
    SET tentativi_falliti = tentativi_falliti + 1,
        bloccato_fino_al  = CASE
            WHEN tentativi_falliti + 1 >= 5 THEN
                now() + least(interval '1 minute' * power(2, tentativi_falliti + 1 - 5), interval '1 hour')
            ELSE bloccato_fino_al
        END
    WHERE utente_id = p_utente_id
    RETURNING bloccato_fino_al INTO v_bloccato_fino_al;

    RETURN v_bloccato_fino_al;
END
$$;

COMMENT ON FUNCTION sec.registra_esito_accesso(uuid, boolean) IS 'Azzera o incrementa i tentativi falliti con blocco progressivo';

-- Accetta un passo TOTP solo se è successivo all'ultimo usato: impedisce di
-- riusare lo stesso codice. Restituisce false in caso di riuso.
CREATE FUNCTION sec.registra_passo_totp(p_utente_id uuid, p_passo bigint)
RETURNS boolean
LANGUAGE sql
AS $$
    WITH aggiornato AS (
        UPDATE sec.utenti
        SET mfa_ultimo_passo = p_passo
        WHERE utente_id = p_utente_id
          AND (mfa_ultimo_passo IS NULL OR mfa_ultimo_passo < p_passo)
        RETURNING 1
    )
    SELECT EXISTS (SELECT 1 FROM aggiornato)
$$;

COMMENT ON FUNCTION sec.registra_passo_totp(uuid, bigint) IS 'Protezione contro il riuso dello stesso codice TOTP';

-- Consuma un codice di recupero: vale una volta sola.
CREATE FUNCTION sec.usa_codice_recupero(p_utente_id uuid, p_codice_hash bytea)
RETURNS boolean
LANGUAGE sql
AS $$
    WITH usato AS (
        UPDATE sec.codici_recupero
        SET usato_il = now()
        WHERE utente_id = p_utente_id
          AND codice_hash = p_codice_hash
          AND usato_il IS NULL
        RETURNING 1
    )
    SELECT EXISTS (SELECT 1 FROM usato)
$$;

COMMENT ON FUNCTION sec.usa_codice_recupero(uuid, bytea) IS 'Segna come usato un codice di recupero valido';

-- Permessi effettivi di un utente, unendo i suoi ruoli: se almeno un ruolo
-- concede il permesso su tutto, l'ambito è 'tutti'.
CREATE FUNCTION sec.permessi_utente(p_utente_id uuid)
RETURNS TABLE (codice text, ambito text)
LANGUAGE sql
STABLE
AS $$
    SELECT p.codice,
           CASE WHEN bool_or(rp.ambito = 'tutti') THEN 'tutti' ELSE 'propri' END
    FROM sec.utenti_ruoli ur
    JOIN sec.ruoli_permessi rp ON rp.ruolo_id = ur.ruolo_id
    JOIN sec.permessi p        ON p.permesso_id = rp.permesso_id
    WHERE ur.utente_id = p_utente_id
    GROUP BY p.codice
    ORDER BY p.codice
$$;

COMMENT ON FUNCTION sec.permessi_utente(uuid) IS 'Permessi dell''utente con ambito tutti o propri';

-- Vero se almeno uno dei ruoli dell'utente obbliga al secondo fattore.
CREATE FUNCTION sec.richiede_mfa(p_utente_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM sec.utenti_ruoli ur
        JOIN sec.ruoli r ON r.ruolo_id = ur.ruolo_id
        WHERE ur.utente_id = p_utente_id
          AND r.richiede_mfa
    )
$$;

COMMENT ON FUNCTION sec.richiede_mfa(uuid) IS 'L''utente ha un ruolo con secondo fattore obbligatorio';

-- Rotazione del token di rinnovo. SECURITY DEFINER perché il client presenta
-- solo il token e l'azienda non è nota. Esiti:
--   ok                  token valido: è segnato come usato e ne nasce uno nuovo
--   sconosciuto         nessun token con quell'hash
--   riuso               token già usato: tutta la famiglia viene revocata
--   revocato, scaduto   token non più valido
--   utente_non_attivo   utente sospeso o disattivato: la famiglia viene revocata
-- L'API deve fare COMMIT anche quando l'esito non è ok, perché le revoche restino.
CREATE FUNCTION sec.ruota_sessione(
    p_token_hash  bytea,
    p_nuovo_hash  bytea,
    p_dispositivo text,
    p_indirizzo   inet
)
RETURNS TABLE (esito text, utente_id uuid, azienda_id uuid, scade_il timestamptz)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
#variable_conflict use_column
DECLARE
    v_sessione sec.sessioni_rinnovo%ROWTYPE;
    v_stato    text;
BEGIN
    SELECT s.* INTO v_sessione
    FROM sec.sessioni_rinnovo s
    WHERE s.token_hash = p_token_hash
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY SELECT 'sconosciuto'::text, NULL::uuid, NULL::uuid, NULL::timestamptz;
        RETURN;
    END IF;

    IF v_sessione.revocata_il IS NOT NULL THEN
        RETURN QUERY SELECT 'revocato'::text, v_sessione.utente_id, v_sessione.azienda_id, v_sessione.scade_il;
        RETURN;
    END IF;

    IF v_sessione.usata_il IS NOT NULL THEN
        UPDATE sec.sessioni_rinnovo s
        SET revocata_il = now(), motivo_revoca = 'riuso'
        WHERE s.famiglia_id = v_sessione.famiglia_id
          AND s.revocata_il IS NULL;

        RETURN QUERY SELECT 'riuso'::text, v_sessione.utente_id, v_sessione.azienda_id, v_sessione.scade_il;
        RETURN;
    END IF;

    IF v_sessione.scade_il <= now() THEN
        RETURN QUERY SELECT 'scaduto'::text, v_sessione.utente_id, v_sessione.azienda_id, v_sessione.scade_il;
        RETURN;
    END IF;

    SELECT u.stato INTO v_stato FROM sec.utenti u WHERE u.utente_id = v_sessione.utente_id;

    IF v_stato <> 'attivo' THEN
        UPDATE sec.sessioni_rinnovo s
        SET revocata_il = now(), motivo_revoca = 'utente_non_attivo'
        WHERE s.famiglia_id = v_sessione.famiglia_id
          AND s.revocata_il IS NULL;

        RETURN QUERY SELECT 'utente_non_attivo'::text, v_sessione.utente_id, v_sessione.azienda_id, v_sessione.scade_il;
        RETURN;
    END IF;

    UPDATE sec.sessioni_rinnovo s
    SET usata_il = now()
    WHERE s.sessione_id = v_sessione.sessione_id;

    INSERT INTO sec.sessioni_rinnovo (azienda_id, utente_id, famiglia_id, token_hash, scade_il, dispositivo, indirizzo)
    VALUES (v_sessione.azienda_id, v_sessione.utente_id, v_sessione.famiglia_id, p_nuovo_hash,
            v_sessione.scade_il, p_dispositivo, p_indirizzo);

    RETURN QUERY SELECT 'ok'::text, v_sessione.utente_id, v_sessione.azienda_id, v_sessione.scade_il;
END
$$;

COMMENT ON FUNCTION sec.ruota_sessione(bytea, bytea, text, inet) IS 'Ruota un token di rinnovo e rileva il riuso';

-- Revoca la famiglia del token indicato, solo se appartiene all'utente.
-- Gira sotto RLS. Restituisce il numero di token revocati.
CREATE FUNCTION sec.revoca_sessione(p_utente_id uuid, p_token_hash bytea, p_motivo text)
RETURNS integer
LANGUAGE sql
AS $$
    WITH revocate AS (
        UPDATE sec.sessioni_rinnovo s
        SET revocata_il = now(), motivo_revoca = p_motivo
        WHERE s.revocata_il IS NULL
          AND s.famiglia_id = (
              SELECT f.famiglia_id
              FROM sec.sessioni_rinnovo f
              WHERE f.token_hash = p_token_hash
                AND f.utente_id = p_utente_id
          )
        RETURNING 1
    )
    SELECT count(*)::integer FROM revocate
$$;

COMMENT ON FUNCTION sec.revoca_sessione(uuid, bytea, text) IS 'Revoca la catena di un token di rinnovo dell''utente';

-- Primo avvio: crea l'azienda, il Titolare e, se indicato, l'Amministratore
-- di sistema. Funziona una sola volta, finché non esiste nessuna azienda.
-- Se l'amministratore non è indicato o ha la stessa email del titolare, il
-- titolare riceve entrambi i ruoli (impresa di una sola persona).
CREATE FUNCTION sec.inizializza_installazione(
    p_ragione_sociale       text,
    p_partita_iva           text,
    p_regione               text,
    p_titolare_email        text,
    p_titolare_nome         text,
    p_titolare_cognome      text,
    p_titolare_hash         text,
    p_amministratore_email   text DEFAULT NULL,
    p_amministratore_nome    text DEFAULT NULL,
    p_amministratore_cognome text DEFAULT NULL,
    p_amministratore_hash    text DEFAULT NULL
)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_azienda_id        uuid;
    v_titolare_id       uuid;
    v_amministratore_id uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sec.inizializza_installazione'));

    IF EXISTS (SELECT 1 FROM sec.aziende) THEN
        RAISE EXCEPTION 'installazione già inizializzata'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    INSERT INTO sec.aziende (ragione_sociale, partita_iva, regione)
    VALUES (p_ragione_sociale, p_partita_iva, p_regione)
    RETURNING azienda_id INTO v_azienda_id;

    INSERT INTO sec.utenti (azienda_id, email, nome, cognome, password_hash)
    VALUES (v_azienda_id, btrim(p_titolare_email), p_titolare_nome, p_titolare_cognome, p_titolare_hash)
    RETURNING utente_id INTO v_titolare_id;

    IF p_amministratore_email IS NULL OR lower(btrim(p_amministratore_email)) = lower(btrim(p_titolare_email)) THEN
        v_amministratore_id := v_titolare_id;
    ELSE
        INSERT INTO sec.utenti (azienda_id, email, nome, cognome, password_hash)
        VALUES (v_azienda_id, btrim(p_amministratore_email), p_amministratore_nome, p_amministratore_cognome, p_amministratore_hash)
        RETURNING utente_id INTO v_amministratore_id;
    END IF;

    INSERT INTO sec.utenti_ruoli (azienda_id, utente_id, ruolo_id)
    SELECT v_azienda_id, v_titolare_id, r.ruolo_id
    FROM sec.ruoli r
    WHERE r.azienda_id IS NULL AND r.codice = 'titolare';

    INSERT INTO sec.utenti_ruoli (azienda_id, utente_id, ruolo_id)
    SELECT v_azienda_id, v_amministratore_id, r.ruolo_id
    FROM sec.ruoli r
    WHERE r.azienda_id IS NULL AND r.codice = 'amministratore_sistema';

    INSERT INTO sec.audit_log (azienda_id, utente_id, azione, entita, entita_id)
    VALUES (v_azienda_id, v_titolare_id, 'installazione.inizializzata', 'aziende', v_azienda_id);

    RETURN v_azienda_id;
END
$$;

COMMENT ON FUNCTION sec.inizializza_installazione(text, text, text, text, text, text, text, text, text, text, text)
    IS 'Primo avvio: prima azienda, Titolare e Amministratore di sistema';

-- Vero finché l'installazione non è stata inizializzata.
CREATE FUNCTION sec.installazione_da_inizializzare()
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT NOT EXISTS (SELECT 1 FROM sec.aziende)
$$;

COMMENT ON FUNCTION sec.installazione_da_inizializzare() IS 'Nessuna azienda presente: serve il primo avvio';

-- Audit dei cambi di ruolo, come previsto dal piano.
CREATE FUNCTION sec.audit_utenti_ruoli()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_riga sec.utenti_ruoli;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_riga := OLD;
    ELSE
        v_riga := NEW;
    END IF;

    INSERT INTO sec.audit_log (azienda_id, utente_id, azione, entita, entita_id, dettaglio)
    VALUES (v_riga.azienda_id, coalesce(sec.utente_corrente(), v_riga.creato_da),
            'utenti_ruoli.' || lower(TG_OP), 'utenti', v_riga.utente_id,
            jsonb_build_object('ruolo_id', v_riga.ruolo_id));

    RETURN NULL;
END
$$;

CREATE TRIGGER tr_utenti_ruoli_audit
    AFTER INSERT OR DELETE ON sec.utenti_ruoli
    FOR EACH ROW EXECUTE FUNCTION sec.audit_utenti_ruoli();

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE sec.sessioni_rinnovo ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.codici_recupero  ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_sessioni_rinnovo ON sec.sessioni_rinnovo TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_codici_recupero ON sec.codici_recupero TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON sec.sessioni_rinnovo, sec.codici_recupero FROM PUBLIC;

GRANT SELECT, INSERT, UPDATE         ON sec.sessioni_rinnovo TO app_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON sec.codici_recupero  TO app_api;

REVOKE ALL ON FUNCTION
    sec.utente_corrente(),
    sec.dati_accesso(text),
    sec.registra_esito_accesso(uuid, boolean),
    sec.registra_passo_totp(uuid, bigint),
    sec.usa_codice_recupero(uuid, bytea),
    sec.permessi_utente(uuid),
    sec.richiede_mfa(uuid),
    sec.ruota_sessione(bytea, bytea, text, inet),
    sec.revoca_sessione(uuid, bytea, text),
    sec.inizializza_installazione(text, text, text, text, text, text, text, text, text, text, text),
    sec.installazione_da_inizializzare(),
    sec.audit_utenti_ruoli()
FROM PUBLIC;

GRANT EXECUTE ON FUNCTION
    sec.utente_corrente(),
    sec.dati_accesso(text),
    sec.registra_esito_accesso(uuid, boolean),
    sec.registra_passo_totp(uuid, bigint),
    sec.usa_codice_recupero(uuid, bytea),
    sec.permessi_utente(uuid),
    sec.richiede_mfa(uuid),
    sec.ruota_sessione(bytea, bytea, text, inet),
    sec.revoca_sessione(uuid, bytea, text),
    sec.inizializza_installazione(text, text, text, text, text, text, text, text, text, text, text),
    sec.installazione_da_inizializzare()
TO app_api;

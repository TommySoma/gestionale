-- 0001: schema sec (accessi e audit).
-- Eseguito da app_owner, proprietario del database. I ruoli app_api e app_chatbot
-- esistono già: li crea deploy/initdb/01-ruoli.sh al primo avvio del container.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

CREATE SCHEMA sec;

COMMENT ON SCHEMA sec IS 'Accessi, ruoli, permessi e audit';

-- ---------------------------------------------------------------------------
-- Funzioni di supporto
-- ---------------------------------------------------------------------------

-- Azienda della transazione corrente, impostata dall'API con
-- SET LOCAL app.azienda_id = '...'. Se non è impostata restituisce NULL e
-- le policy RLS non mostrano nessuna riga. NULLIF serve perché, finita una
-- transazione con SET LOCAL, la variabile resta definita ma vuota.
CREATE FUNCTION sec.azienda_corrente()
RETURNS uuid
LANGUAGE sql
STABLE
PARALLEL SAFE
AS $$
    SELECT NULLIF(current_setting('app.azienda_id', true), '')::uuid
$$;

COMMENT ON FUNCTION sec.azienda_corrente() IS 'Azienda impostata con SET LOCAL app.azienda_id; NULL se assente';

-- Colonne comuni in UPDATE: aggiorna modificato_il, incrementa versione e
-- impedisce di riscrivere i dati di creazione. Il controllo di concorrenza
-- ottimistica (WHERE versione = @versione) lo fa l'API.
CREATE FUNCTION sec.aggiorna_metadati()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.creato_il     := OLD.creato_il;
    NEW.creato_da     := OLD.creato_da;
    NEW.modificato_il := now();
    NEW.versione      := OLD.versione + 1;
    RETURN NEW;
END
$$;

COMMENT ON FUNCTION sec.aggiorna_metadati() IS 'Trigger BEFORE UPDATE per le colonne comuni delle tabelle di business';

-- L'audit è solo inserimento: blocca UPDATE e DELETE per chiunque, oltre ai
-- privilegi già tolti ad app_api.
CREATE FUNCTION sec.blocca_modifica_audit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'sec.audit_log è in sola aggiunta: % non consentito', TG_OP
        USING ERRCODE = 'insufficient_privilege';
END
$$;

COMMENT ON FUNCTION sec.blocca_modifica_audit() IS 'Rende sec.audit_log append-only';

-- ---------------------------------------------------------------------------
-- Tabelle
-- ---------------------------------------------------------------------------

CREATE TABLE sec.aziende (
    azienda_id      uuid        NOT NULL DEFAULT uuidv7(),
    ragione_sociale text        NOT NULL,
    partita_iva     text,
    codice_fiscale  text,
    indirizzo       text,
    comune          text,
    provincia       text,
    cap             text,
    regione         text        NOT NULL,
    creato_il       timestamptz NOT NULL DEFAULT now(),
    creato_da       uuid,
    modificato_il   timestamptz,
    modificato_da   uuid,
    versione        integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_aziende PRIMARY KEY (azienda_id),
    CONSTRAINT ck_aziende_ragione_sociale CHECK (btrim(ragione_sociale) <> ''),
    CONSTRAINT ck_aziende_partita_iva CHECK (partita_iva ~ '^[0-9]{11}$'),
    CONSTRAINT ck_aziende_codice_fiscale CHECK (codice_fiscale ~ '^([0-9]{11}|[A-Z0-9]{16})$'),
    CONSTRAINT ck_aziende_provincia CHECK (provincia ~ '^[A-Z]{2}$'),
    CONSTRAINT ck_aziende_cap CHECK (cap ~ '^[0-9]{5}$'),
    -- La regione sceglie l'adattatore del catasto: Toscana (SIERT), poi Lombardia (CURIT).
    CONSTRAINT ck_aziende_regione CHECK (regione IN ('toscana', 'lombardia')),
    CONSTRAINT ck_aziende_versione CHECK (versione >= 1)
);

COMMENT ON TABLE sec.aziende IS 'Tenant: ogni tabella di business la referenzia con azienda_id';

CREATE TABLE sec.utenti (
    utente_id           uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id          uuid        NOT NULL,
    email               text        NOT NULL,
    nome                text        NOT NULL,
    cognome             text        NOT NULL,
    password_hash       text        NOT NULL,
    stato               text        NOT NULL DEFAULT 'attivo',
    mfa_segreto_cifrato bytea,
    mfa_attivo          boolean     NOT NULL DEFAULT false,
    tentativi_falliti   integer     NOT NULL DEFAULT 0,
    bloccato_fino_al    timestamptz,
    ultimo_accesso_il   timestamptz,
    creato_il           timestamptz NOT NULL DEFAULT now(),
    creato_da           uuid,
    modificato_il       timestamptz,
    modificato_da       uuid,
    versione            integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_utenti PRIMARY KEY (utente_id),
    -- Serve alle chiavi esterne composte: un riferimento a un utente porta con sé la sua azienda.
    CONSTRAINT uq_utenti_azienda_utente UNIQUE (azienda_id, utente_id),
    CONSTRAINT fk_utenti_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT ck_utenti_email CHECK (email ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT ck_utenti_stato CHECK (stato IN ('attivo', 'sospeso', 'disattivato')),
    -- L'hash è nel formato PHC di Argon2id: la password in chiaro non arriva mai qui.
    CONSTRAINT ck_utenti_password_hash CHECK (password_hash LIKE '$argon2id$%'),
    CONSTRAINT ck_utenti_mfa CHECK (NOT mfa_attivo OR mfa_segreto_cifrato IS NOT NULL),
    CONSTRAINT ck_utenti_tentativi_falliti CHECK (tentativi_falliti >= 0),
    CONSTRAINT ck_utenti_versione CHECK (versione >= 1)
);

-- Email unica per azienda, senza distinguere maiuscole e minuscole.
CREATE UNIQUE INDEX uq_utenti_azienda_email ON sec.utenti (azienda_id, lower(email));

COMMENT ON TABLE sec.utenti IS 'Account personali; il segreto MFA è sempre cifrato dall''API';

-- Chi ha creato o modificato: riferimenti agli utenti, aggiunti dopo per la dipendenza circolare.
ALTER TABLE sec.aziende
    ADD CONSTRAINT fk_aziende_creato_da     FOREIGN KEY (creato_da)     REFERENCES sec.utenti (utente_id),
    ADD CONSTRAINT fk_aziende_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id);

ALTER TABLE sec.utenti
    ADD CONSTRAINT fk_utenti_creato_da     FOREIGN KEY (creato_da)     REFERENCES sec.utenti (utente_id),
    ADD CONSTRAINT fk_utenti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id);

-- I ruoli di sistema hanno azienda_id NULL e valgono per tutte le aziende;
-- dalla Fase 3 un'azienda potrà avere ruoli propri.
CREATE TABLE sec.ruoli (
    ruolo_id    uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id  uuid,
    codice      text        NOT NULL,
    nome        text        NOT NULL,
    descrizione text,
    di_sistema  boolean     NOT NULL DEFAULT false,
    creato_il   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ruoli PRIMARY KEY (ruolo_id),
    CONSTRAINT fk_ruoli_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT uq_ruoli_azienda_codice UNIQUE NULLS NOT DISTINCT (azienda_id, codice),
    CONSTRAINT ck_ruoli_codice CHECK (codice ~ '^[a-z][a-z_]*$'),
    CONSTRAINT ck_ruoli_di_sistema CHECK (di_sistema = (azienda_id IS NULL))
);

CREATE TABLE sec.permessi (
    permesso_id uuid        NOT NULL DEFAULT uuidv7(),
    codice      text        NOT NULL,
    descrizione text        NOT NULL,
    creato_il   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_permessi PRIMARY KEY (permesso_id),
    CONSTRAINT uq_permessi_codice UNIQUE (codice),
    CONSTRAINT ck_permessi_codice CHECK (codice ~ '^[a-z_]+\.[a-z_]+$')
);

-- ambito 'propri': il permesso vale solo sulle attività assegnate all'utente.
-- Lo applica l'API; la RLS per azienda resta la rete di sicurezza sotto.
CREATE TABLE sec.ruoli_permessi (
    ruolo_id    uuid        NOT NULL,
    permesso_id uuid        NOT NULL,
    ambito      text        NOT NULL DEFAULT 'tutti',
    creato_il   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ruoli_permessi PRIMARY KEY (ruolo_id, permesso_id),
    CONSTRAINT fk_ruoli_permessi_ruoli FOREIGN KEY (ruolo_id) REFERENCES sec.ruoli (ruolo_id) ON DELETE CASCADE,
    CONSTRAINT fk_ruoli_permessi_permessi FOREIGN KEY (permesso_id) REFERENCES sec.permessi (permesso_id),
    CONSTRAINT ck_ruoli_permessi_ambito CHECK (ambito IN ('tutti', 'propri'))
);

CREATE INDEX ix_ruoli_permessi_permesso ON sec.ruoli_permessi (permesso_id);

-- Tabella di collegamento: le righe si inseriscono e si tolgono, non si
-- modificano, quindi ha solo le colonne di creazione.
CREATE TABLE sec.utenti_ruoli (
    azienda_id uuid        NOT NULL,
    utente_id  uuid        NOT NULL,
    ruolo_id   uuid        NOT NULL,
    creato_il  timestamptz NOT NULL DEFAULT now(),
    creato_da  uuid,
    CONSTRAINT pk_utenti_ruoli PRIMARY KEY (utente_id, ruolo_id),
    -- La chiave composta garantisce che l'utente appartenga alla stessa azienda della riga.
    CONSTRAINT fk_utenti_ruoli_utenti FOREIGN KEY (azienda_id, utente_id) REFERENCES sec.utenti (azienda_id, utente_id),
    CONSTRAINT fk_utenti_ruoli_ruoli FOREIGN KEY (ruolo_id) REFERENCES sec.ruoli (ruolo_id),
    CONSTRAINT fk_utenti_ruoli_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id)
);

CREATE INDEX ix_utenti_ruoli_azienda ON sec.utenti_ruoli (azienda_id);
CREATE INDEX ix_utenti_ruoli_ruolo ON sec.utenti_ruoli (ruolo_id);

-- Registro delle attività: chi, cosa, quando, da dove. Nessuna chiave esterna,
-- perché l'audit deve sopravvivere a qualunque cambiamento dei dati che descrive.
CREATE TABLE sec.audit_log (
    audit_id    uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id  uuid        NOT NULL,
    avvenuto_il timestamptz NOT NULL DEFAULT now(),
    utente_id   uuid,
    azione      text        NOT NULL,
    entita      text,
    entita_id   uuid,
    indirizzo   inet,
    dettaglio   jsonb,
    CONSTRAINT pk_audit_log PRIMARY KEY (audit_id),
    CONSTRAINT ck_audit_log_azione CHECK (btrim(azione) <> '')
);

CREATE INDEX ix_audit_log_azienda_avvenuto ON sec.audit_log (azienda_id, avvenuto_il DESC);
CREATE INDEX ix_audit_log_entita ON sec.audit_log (entita, entita_id) WHERE entita_id IS NOT NULL;

COMMENT ON TABLE sec.audit_log IS 'Solo inserimento: UPDATE e DELETE bloccati da privilegi e trigger';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

CREATE TRIGGER tr_aziende_metadati
    BEFORE UPDATE ON sec.aziende
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_utenti_metadati
    BEFORE UPDATE ON sec.utenti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_audit_log_append_only
    BEFORE UPDATE OR DELETE ON sec.audit_log
    FOR EACH ROW EXECUTE FUNCTION sec.blocca_modifica_audit();

CREATE TRIGGER tr_audit_log_no_truncate
    BEFORE TRUNCATE ON sec.audit_log
    FOR EACH STATEMENT EXECUTE FUNCTION sec.blocca_modifica_audit();

-- ---------------------------------------------------------------------------
-- Row-Level Security: ogni tabella con azienda_id vede solo l'azienda corrente.
-- app_owner, proprietario delle tabelle, non è soggetto alle policy (niente
-- FORCE): serve alle migrazioni. app_api non ha BYPASSRLS.
-- ---------------------------------------------------------------------------

ALTER TABLE sec.aziende      ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.utenti       ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.ruoli        ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.utenti_ruoli ENABLE ROW LEVEL SECURITY;
ALTER TABLE sec.audit_log    ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_aziende ON sec.aziende TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_utenti ON sec.utenti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

-- I ruoli di sistema (azienda_id NULL) sono visibili a tutti; app_api non può crearne.
CREATE POLICY rls_ruoli ON sec.ruoli TO app_api
    USING (azienda_id IS NULL OR azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_utenti_ruoli ON sec.utenti_ruoli TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_audit_log ON sec.audit_log TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

-- ---------------------------------------------------------------------------
-- Privilegi: minimo indispensabile. app_chatbot non vede lo schema sec.
-- ---------------------------------------------------------------------------

REVOKE ALL ON SCHEMA sec FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA sec FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA sec FROM PUBLIC;

GRANT USAGE ON SCHEMA sec TO app_api;
GRANT EXECUTE ON FUNCTION sec.azienda_corrente() TO app_api;

GRANT SELECT, UPDATE                 ON sec.aziende        TO app_api;
GRANT SELECT, INSERT, UPDATE         ON sec.utenti         TO app_api;
GRANT SELECT                         ON sec.ruoli          TO app_api;
GRANT SELECT                         ON sec.permessi       TO app_api;
GRANT SELECT                         ON sec.ruoli_permessi TO app_api;
GRANT SELECT, INSERT, DELETE         ON sec.utenti_ruoli   TO app_api;
-- Audit: niente UPDATE, DELETE né TRUNCATE.
GRANT SELECT, INSERT                 ON sec.audit_log      TO app_api;

-- ---------------------------------------------------------------------------
-- Dati: i sei ruoli di sistema e i permessi della matrice del piano
-- (docs/piano-di-sviluppo.md, sezione Autorizzazioni). È una proposta di
-- partenza: le modifiche arrivano con nuove migrazioni.
-- ---------------------------------------------------------------------------

INSERT INTO sec.ruoli (codice, nome, descrizione, di_sistema) VALUES
    ('titolare',               'Titolare',                  'Legale rappresentante; firma come dichiarante le dichiarazioni di conformità', true),
    ('amministrativo',         'Amministrativo',            'Segreteria e back office: anagrafiche, preventivi, pianificazione, scadenze', true),
    ('responsabile_tecnico',   'Responsabile tecnico',      'Tecnico abilitato: valida e firma rapporti di controllo e dichiarazioni di conformità', true),
    ('tecnico',                'Tecnico',                   'Operatore sul campo: interventi e rapporti propri', true),
    ('sola_lettura',           'Sola lettura',              'Consulente o commercialista: consulta senza modificare', true),
    ('amministratore_sistema', 'Amministratore di sistema', 'Utenti, backup, aggiornamenti e configurazione; nessun accesso ai dati di business', true);

INSERT INTO sec.permessi (codice, descrizione) VALUES
    ('clienti.lettura',                    'Consultare clienti e sedi'),
    ('clienti.scrittura',                  'Creare e modificare clienti e sedi'),
    ('impianti.lettura',                   'Consultare impianti e generatori'),
    ('impianti.scrittura',                 'Creare e modificare impianti e generatori'),
    ('interventi.lettura',                 'Consultare interventi e calendario'),
    ('interventi.scrittura',               'Creare e modificare interventi'),
    ('rapporti_controllo.lettura',         'Consultare i rapporti di controllo'),
    ('rapporti_controllo.scrittura',       'Compilare i rapporti di controllo'),
    ('rapporti_controllo.firma',           'Validare e firmare i rapporti di controllo'),
    ('dichiarazioni_conformita.lettura',   'Consultare le dichiarazioni di conformità'),
    ('dichiarazioni_conformita.scrittura', 'Compilare le dichiarazioni di conformità'),
    ('dichiarazioni_conformita.firma',     'Validare e firmare le dichiarazioni di conformità'),
    ('bollini.lettura',                    'Consultare bollini e invii al catasto'),
    ('bollini.scrittura',                  'Gestire bollini e invii al catasto'),
    ('fgas.lettura',                       'Consultare il registro F-Gas'),
    ('fgas.scrittura',                     'Aggiornare il registro F-Gas'),
    ('preventivi.lettura',                 'Consultare i preventivi'),
    ('preventivi.scrittura',               'Creare e modificare i preventivi'),
    ('listino.lettura',                    'Consultare il listino prezzi'),
    ('listino.scrittura',                  'Modificare il listino prezzi'),
    ('report_economici.lettura',           'Consultare i report economici'),
    ('documenti.lettura',                  'Consultare i documenti'),
    ('documenti.scrittura',                'Caricare e modificare i documenti'),
    ('chatbot.uso',                        'Usare l''assistente'),
    ('utenti.lettura',                     'Consultare utenti e ruoli'),
    ('utenti.scrittura',                   'Gestire utenti e ruoli'),
    ('audit.lettura',                      'Consultare il registro attività'),
    ('sistema.lettura',                    'Consultare backup, aggiornamenti e configurazione'),
    ('sistema.scrittura',                  'Gestire backup, aggiornamenti e configurazione');

INSERT INTO sec.ruoli_permessi (ruolo_id, permesso_id, ambito)
SELECT r.ruolo_id, p.permesso_id, m.ambito
FROM (VALUES
    -- Titolare
    ('titolare', 'clienti.lettura', 'tutti'),
    ('titolare', 'clienti.scrittura', 'tutti'),
    ('titolare', 'impianti.lettura', 'tutti'),
    ('titolare', 'impianti.scrittura', 'tutti'),
    ('titolare', 'interventi.lettura', 'tutti'),
    ('titolare', 'interventi.scrittura', 'tutti'),
    ('titolare', 'rapporti_controllo.lettura', 'tutti'),
    ('titolare', 'rapporti_controllo.scrittura', 'tutti'),
    ('titolare', 'rapporti_controllo.firma', 'tutti'),
    ('titolare', 'dichiarazioni_conformita.lettura', 'tutti'),
    ('titolare', 'dichiarazioni_conformita.scrittura', 'tutti'),
    ('titolare', 'dichiarazioni_conformita.firma', 'tutti'),
    ('titolare', 'bollini.lettura', 'tutti'),
    ('titolare', 'bollini.scrittura', 'tutti'),
    ('titolare', 'fgas.lettura', 'tutti'),
    ('titolare', 'fgas.scrittura', 'tutti'),
    ('titolare', 'preventivi.lettura', 'tutti'),
    ('titolare', 'preventivi.scrittura', 'tutti'),
    ('titolare', 'listino.lettura', 'tutti'),
    ('titolare', 'listino.scrittura', 'tutti'),
    ('titolare', 'report_economici.lettura', 'tutti'),
    ('titolare', 'documenti.lettura', 'tutti'),
    ('titolare', 'documenti.scrittura', 'tutti'),
    ('titolare', 'chatbot.uso', 'tutti'),
    ('titolare', 'utenti.lettura', 'tutti'),
    ('titolare', 'utenti.scrittura', 'tutti'),
    ('titolare', 'audit.lettura', 'tutti'),
    -- Amministrativo
    ('amministrativo', 'clienti.lettura', 'tutti'),
    ('amministrativo', 'clienti.scrittura', 'tutti'),
    ('amministrativo', 'impianti.lettura', 'tutti'),
    ('amministrativo', 'impianti.scrittura', 'tutti'),
    ('amministrativo', 'interventi.lettura', 'tutti'),
    ('amministrativo', 'interventi.scrittura', 'tutti'),
    ('amministrativo', 'rapporti_controllo.lettura', 'tutti'),
    ('amministrativo', 'dichiarazioni_conformita.lettura', 'tutti'),
    ('amministrativo', 'bollini.lettura', 'tutti'),
    ('amministrativo', 'bollini.scrittura', 'tutti'),
    ('amministrativo', 'fgas.lettura', 'tutti'),
    ('amministrativo', 'preventivi.lettura', 'tutti'),
    ('amministrativo', 'preventivi.scrittura', 'tutti'),
    ('amministrativo', 'listino.lettura', 'tutti'),
    ('amministrativo', 'listino.scrittura', 'tutti'),
    ('amministrativo', 'report_economici.lettura', 'tutti'),
    ('amministrativo', 'documenti.lettura', 'tutti'),
    ('amministrativo', 'documenti.scrittura', 'tutti'),
    ('amministrativo', 'chatbot.uso', 'tutti'),
    -- Responsabile tecnico
    ('responsabile_tecnico', 'clienti.lettura', 'tutti'),
    ('responsabile_tecnico', 'impianti.lettura', 'tutti'),
    ('responsabile_tecnico', 'impianti.scrittura', 'tutti'),
    ('responsabile_tecnico', 'interventi.lettura', 'tutti'),
    ('responsabile_tecnico', 'interventi.scrittura', 'tutti'),
    ('responsabile_tecnico', 'rapporti_controllo.lettura', 'tutti'),
    ('responsabile_tecnico', 'rapporti_controllo.scrittura', 'tutti'),
    ('responsabile_tecnico', 'rapporti_controllo.firma', 'tutti'),
    ('responsabile_tecnico', 'dichiarazioni_conformita.lettura', 'tutti'),
    ('responsabile_tecnico', 'dichiarazioni_conformita.scrittura', 'tutti'),
    ('responsabile_tecnico', 'dichiarazioni_conformita.firma', 'tutti'),
    ('responsabile_tecnico', 'bollini.lettura', 'tutti'),
    ('responsabile_tecnico', 'bollini.scrittura', 'tutti'),
    ('responsabile_tecnico', 'fgas.lettura', 'tutti'),
    ('responsabile_tecnico', 'fgas.scrittura', 'tutti'),
    ('responsabile_tecnico', 'preventivi.lettura', 'tutti'),
    ('responsabile_tecnico', 'listino.lettura', 'tutti'),
    ('responsabile_tecnico', 'documenti.lettura', 'tutti'),
    ('responsabile_tecnico', 'documenti.scrittura', 'tutti'),
    ('responsabile_tecnico', 'chatbot.uso', 'tutti'),
    -- Tecnico
    ('tecnico', 'clienti.lettura', 'propri'),
    ('tecnico', 'impianti.lettura', 'propri'),
    ('tecnico', 'impianti.scrittura', 'propri'),
    ('tecnico', 'interventi.lettura', 'propri'),
    ('tecnico', 'interventi.scrittura', 'propri'),
    ('tecnico', 'rapporti_controllo.lettura', 'propri'),
    ('tecnico', 'rapporti_controllo.scrittura', 'propri'),
    ('tecnico', 'dichiarazioni_conformita.lettura', 'propri'),
    ('tecnico', 'bollini.lettura', 'propri'),
    ('tecnico', 'fgas.lettura', 'propri'),
    ('tecnico', 'fgas.scrittura', 'propri'),
    ('tecnico', 'listino.lettura', 'tutti'),
    ('tecnico', 'documenti.lettura', 'propri'),
    ('tecnico', 'chatbot.uso', 'tutti'),
    -- Sola lettura
    ('sola_lettura', 'clienti.lettura', 'tutti'),
    ('sola_lettura', 'impianti.lettura', 'tutti'),
    ('sola_lettura', 'interventi.lettura', 'tutti'),
    ('sola_lettura', 'rapporti_controllo.lettura', 'tutti'),
    ('sola_lettura', 'dichiarazioni_conformita.lettura', 'tutti'),
    ('sola_lettura', 'bollini.lettura', 'tutti'),
    ('sola_lettura', 'fgas.lettura', 'tutti'),
    ('sola_lettura', 'preventivi.lettura', 'tutti'),
    ('sola_lettura', 'report_economici.lettura', 'tutti'),
    ('sola_lettura', 'documenti.lettura', 'tutti'),
    -- Amministratore di sistema: nessun dato di business
    ('amministratore_sistema', 'utenti.lettura', 'tutti'),
    ('amministratore_sistema', 'utenti.scrittura', 'tutti'),
    ('amministratore_sistema', 'audit.lettura', 'tutti'),
    ('amministratore_sistema', 'sistema.lettura', 'tutti'),
    ('amministratore_sistema', 'sistema.scrittura', 'tutti')
) AS m (ruolo, permesso, ambito)
JOIN sec.ruoli    r ON r.codice = m.ruolo AND r.azienda_id IS NULL
JOIN sec.permessi p ON p.codice = m.permesso;

-- 0007: schema documenti (foto e documenti caricati, per ora sugli impianti).
-- I file stanno sul disco del server, rinominati con l'id del documento; qui
-- ci sono solo i metadati e l'impronta SHA-256. I testi estratti per la ricerca
-- (documenti_chunk) arrivano con il chatbot.
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

CREATE SCHEMA documenti;

COMMENT ON SCHEMA documenti IS 'Metadati dei file caricati; i file sono sul disco del server';

-- Tipi ammessi e limite di dimensione sono gli stessi controlli dell'API: qui
-- sono la rete di sicurezza. Il collegamento è per tipo e id di entità, perché
-- un documento potrà riguardare impianti, interventi, dichiarazioni; l'esistenza
-- dell'entità la controlla un trigger.
CREATE TABLE documenti.documenti (
    documento_id   uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id     uuid        NOT NULL,
    entita         text        NOT NULL,
    entita_id      uuid        NOT NULL,
    categoria      text        NOT NULL,
    nome_originale text        NOT NULL,
    tipo_mime      text        NOT NULL,
    dimensione     bigint      NOT NULL,
    sha256         bytea       NOT NULL,
    percorso       text        NOT NULL,
    descrizione    text,
    eliminato_il   timestamptz,
    eliminato_da   uuid,
    creato_il      timestamptz NOT NULL DEFAULT now(),
    creato_da      uuid,
    modificato_il  timestamptz,
    modificato_da  uuid,
    versione       integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_documenti PRIMARY KEY (documento_id),
    CONSTRAINT uq_documenti_percorso UNIQUE (percorso),
    CONSTRAINT fk_documenti_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_documenti_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_documenti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_documenti_eliminato_da FOREIGN KEY (eliminato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_documenti_entita CHECK (entita IN ('impianto')),
    CONSTRAINT ck_documenti_categoria CHECK (categoria IN ('foto', 'documento')),
    CONSTRAINT ck_documenti_tipo_mime CHECK (tipo_mime IN ('image/jpeg', 'image/png', 'application/pdf')),
    -- Le foto sono immagini, i documenti PDF.
    CONSTRAINT ck_documenti_categoria_tipo CHECK ((categoria = 'foto') = (tipo_mime <> 'application/pdf')),
    CONSTRAINT ck_documenti_dimensione CHECK (dimensione BETWEEN 1 AND 20971520),
    CONSTRAINT ck_documenti_sha256 CHECK (octet_length(sha256) = 32),
    CONSTRAINT ck_documenti_nome CHECK (btrim(nome_originale) <> '' AND length(nome_originale) <= 255),
    -- Percorso relativo alla cartella dei documenti, senza risalire: azienda/documento.
    CONSTRAINT ck_documenti_percorso CHECK (percorso ~ '^[0-9a-f-]{36}/[0-9a-f-]{36}$'),
    CONSTRAINT ck_documenti_eliminazione CHECK (eliminato_da IS NULL OR eliminato_il IS NOT NULL),
    CONSTRAINT ck_documenti_versione CHECK (versione >= 1)
);

CREATE INDEX ix_documenti_entita ON documenti.documenti (entita, entita_id, creato_il DESC)
    WHERE eliminato_il IS NULL;

COMMENT ON TABLE documenti.documenti IS 'Foto e PDF caricati; si eliminano solo logicamente, il file resta sul disco';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

-- L'entità collegata esiste ed è della stessa azienda del documento.
CREATE FUNCTION documenti.controlla_entita()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.entita = 'impianto' AND NOT EXISTS (
        SELECT 1 FROM impianti.impianti
        WHERE impianto_id = NEW.entita_id AND azienda_id = NEW.azienda_id
    ) THEN
        RAISE EXCEPTION 'l''impianto % non esiste in questa azienda', NEW.entita_id
            USING ERRCODE = 'foreign_key_violation';
    END IF;

    RETURN NEW;
END
$$;

-- Il file non cambia: dopo il caricamento si modificano solo descrizione ed eliminazione.
CREATE FUNCTION documenti.blocca_modifica_file()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.azienda_id <> OLD.azienda_id OR NEW.entita <> OLD.entita OR NEW.entita_id <> OLD.entita_id
       OR NEW.categoria <> OLD.categoria OR NEW.nome_originale <> OLD.nome_originale OR NEW.tipo_mime <> OLD.tipo_mime
       OR NEW.dimensione <> OLD.dimensione OR NEW.sha256 <> OLD.sha256 OR NEW.percorso <> OLD.percorso THEN
        RAISE EXCEPTION 'il file di un documento non si modifica: caricane uno nuovo'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.eliminato_il IS NOT NULL AND NEW.eliminato_il IS NULL THEN
        RAISE EXCEPTION 'un documento eliminato non si ripristina da qui'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

CREATE TRIGGER tr_documenti_entita
    BEFORE INSERT ON documenti.documenti
    FOR EACH ROW EXECUTE FUNCTION documenti.controlla_entita();

CREATE TRIGGER tr_documenti_file
    BEFORE UPDATE ON documenti.documenti
    FOR EACH ROW EXECUTE FUNCTION documenti.blocca_modifica_file();

CREATE TRIGGER tr_documenti_metadati
    BEFORE UPDATE ON documenti.documenti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE documenti.documenti ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_documenti ON documenti.documenti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON SCHEMA documenti FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA documenti FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA documenti FROM PUBLIC;

GRANT USAGE ON SCHEMA documenti TO app_api;
-- Niente DELETE: si elimina con eliminato_il.
GRANT SELECT, INSERT, UPDATE ON documenti.documenti TO app_api;

-- ---------------------------------------------------------------------------
-- Permessi: il Tecnico carica foto e documenti sugli impianti dei propri
-- interventi (decisione del 24/11/2026, il piano gli dava solo la lettura).
-- Eliminare resta all'ufficio: lo controlla l'API.
-- ---------------------------------------------------------------------------

INSERT INTO sec.ruoli_permessi (ruolo_id, permesso_id, ambito)
SELECT r.ruolo_id, p.permesso_id, 'propri'
FROM sec.ruoli r
JOIN sec.permessi p ON p.codice = 'documenti.scrittura'
WHERE r.codice = 'tecnico' AND r.azienda_id IS NULL;

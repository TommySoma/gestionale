-- 0014: promemoria email delle scadenze (M4b, ADR 0007).
--
-- L'invio è a mano: dalla lista delle scadenze si scelgono quelle da ricordare,
-- si vede l'anteprima e si invia. Parte solo verso i clienti con un'email e con
-- il consenso "promemoria_scadenze" in corso. Ogni email inviata resta come un
-- richiamo con esito promemoria_inviato, che porta la scadenza "in attesa".

-- ---------------------------------------------------------------------------
-- Parametri della posta in uscita, uno per azienda
-- ---------------------------------------------------------------------------

CREATE TABLE sec.parametri_posta (
    azienda_id        uuid        NOT NULL,
    host              text        NOT NULL,
    porta             integer     NOT NULL,
    sicurezza         text        NOT NULL,
    utente            text,
    -- Cifrata dall'API (AES-GCM): il database non vede mai la password in chiaro.
    password_cifrata  bytea,
    mittente_email    text        NOT NULL,
    mittente_nome     text,
    creato_il         timestamptz NOT NULL DEFAULT now(),
    creato_da         uuid,
    modificato_il     timestamptz,
    modificato_da     uuid,
    versione          integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_parametri_posta PRIMARY KEY (azienda_id),
    CONSTRAINT fk_parametri_posta_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_parametri_posta_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_parametri_posta_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_parametri_posta_host CHECK (btrim(host) <> '' AND host !~ '\s'),
    CONSTRAINT ck_parametri_posta_porta CHECK (porta BETWEEN 1 AND 65535),
    CONSTRAINT ck_parametri_posta_sicurezza CHECK (sicurezza IN ('nessuna', 'starttls', 'ssl')),
    CONSTRAINT ck_parametri_posta_mittente CHECK (mittente_email ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT ck_parametri_posta_password CHECK (password_cifrata IS NULL OR utente IS NOT NULL),
    CONSTRAINT ck_parametri_posta_versione CHECK (versione >= 1)
);

COMMENT ON TABLE sec.parametri_posta IS 'Server SMTP dell''azienda per i promemoria; la password è cifrata dall''API';

CREATE TRIGGER tr_parametri_posta_metadati
    BEFORE UPDATE ON sec.parametri_posta
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

-- Chi ha cambiato il server di posta, e quando; mai la password.
CREATE TRIGGER tr_parametri_posta_audit
    AFTER INSERT OR UPDATE ON sec.parametri_posta
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('azienda_id');

ALTER TABLE sec.parametri_posta ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_parametri_posta ON sec.parametri_posta TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

GRANT SELECT, INSERT, UPDATE ON sec.parametri_posta TO app_api;

-- ---------------------------------------------------------------------------
-- Il promemoria inviato è un richiamo per email
-- ---------------------------------------------------------------------------

ALTER TABLE lavoro.richiami
    DROP CONSTRAINT ck_richiami_esito,
    ADD CONSTRAINT ck_richiami_esito CHECK (esito IN ('nessuna_risposta', 'da_richiamare', 'appuntamento', 'rifiutato', 'promemoria_inviato')),
    ADD CONSTRAINT ck_richiami_promemoria CHECK (esito <> 'promemoria_inviato' OR canale = 'email');

-- ---------------------------------------------------------------------------
-- Dati per l'anteprima e l'invio
-- ---------------------------------------------------------------------------

-- Per ogni scadenza scelta: cliente (responsabile, poi proprietario, poi titolare
-- della sede), email principale, consenso ai promemoria in corso, impianto e
-- luogo. Le scadenze non aperte o non visibili non compaiono.
CREATE FUNCTION lavoro.dati_promemoria(p_scadenze uuid[])
RETURNS TABLE (
    scadenza_id    uuid,
    versione       integer,
    tipo           text,
    data_scadenza  date,
    cliente_id     uuid,
    cliente        text,
    email          text,
    consenso       boolean,
    tipo_impianto  text,
    indirizzo      text,
    comune         text
)
LANGUAGE sql
STABLE
AS $$
    SELECT s.scadenza_id,
           s.versione,
           s.tipo,
           s.data_scadenza,
           c.cliente_id,
           CASE WHEN c.tipo = 'privato' THEN btrim(c.nome || ' ' || c.cognome) ELSE c.denominazione END,
           e.valore,
           coalesce(v.concesso, false),
           ti.nome,
           btrim(se.indirizzo || ' ' || coalesce(se.civico, '')),
           se.comune
    FROM lavoro.scadenze s
    JOIN impianti.impianti i ON i.impianto_id = s.impianto_id
    JOIN impianti.tipi_impianto ti ON ti.tipo_impianto_id = i.tipo_impianto_id
    JOIN anagrafica.sedi se ON se.sede_id = i.sede_id
    JOIN anagrafica.clienti c ON c.cliente_id = coalesce(i.responsabile_cliente_id, i.proprietario_cliente_id, se.cliente_id)
    LEFT JOIN LATERAL (
        SELECT ct.valore
        FROM anagrafica.contatti ct
        WHERE ct.cliente_id = c.cliente_id AND ct.tipo = 'email'
        ORDER BY ct.principale DESC, ct.creato_il
        LIMIT 1
    ) e ON true
    LEFT JOIN anagrafica.v_consensi_correnti v
           ON v.cliente_id = c.cliente_id AND v.finalita = 'promemoria_scadenze'
    WHERE s.scadenza_id = ANY (p_scadenze)
      AND s.stato = 'aperta'
      AND i.eliminato_il IS NULL
      AND se.eliminata_il IS NULL
      AND c.eliminato_il IS NULL
      AND c.anonimizzato_il IS NULL
    ORDER BY s.data_scadenza, s.scadenza_id
$$;

COMMENT ON FUNCTION lavoro.dati_promemoria(uuid[]) IS 'Destinatario, consenso e dati dell''impianto per i promemoria email delle scadenze scelte';

REVOKE EXECUTE ON FUNCTION lavoro.dati_promemoria(uuid[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION lavoro.dati_promemoria(uuid[]) TO app_api;

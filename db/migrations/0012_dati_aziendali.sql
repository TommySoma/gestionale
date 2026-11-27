-- 0012: dati aziendali modificabili dall'API (M4).
--
-- Il codice ditta SIERT (ID_DITTA_MANUTENTORE) entra in ogni riga del file di
-- import: il Titolare lo inserisce dai dati aziendali, insieme a ragione
-- sociale, partita IVA e indirizzo stampati sui documenti. La regione non si
-- cambia dall'API: da lei dipendono catasto, tipi di bollino e regole.

-- ---------------------------------------------------------------------------
-- Privilegi: solo le colonne anagrafiche, mai regione e metadati di creazione
-- ---------------------------------------------------------------------------

REVOKE UPDATE ON sec.aziende FROM app_api;

GRANT UPDATE (ragione_sociale, partita_iva, codice_fiscale, indirizzo, comune, provincia, cap,
              codice_ditta_siert, modificato_da)
    ON sec.aziende TO app_api;

-- ---------------------------------------------------------------------------
-- Audit: chi ha modificato i dati aziendali e a quale versione
-- ---------------------------------------------------------------------------

CREATE TRIGGER tr_aziende_audit
    AFTER UPDATE ON sec.aziende
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('azienda_id');

-- ---------------------------------------------------------------------------
-- Permessi: lettura per chi prepara rapporti e invii, modifica al Titolare
-- ---------------------------------------------------------------------------

INSERT INTO sec.permessi (codice, descrizione) VALUES
    ('azienda.lettura',   'Consultare i dati aziendali'),
    ('azienda.scrittura', 'Modificare i dati aziendali e il codice ditta del catasto');

INSERT INTO sec.ruoli_permessi (ruolo_id, permesso_id, ambito)
SELECT r.ruolo_id, p.permesso_id, 'tutti'
FROM (VALUES
    ('titolare', 'azienda.lettura'),
    ('titolare', 'azienda.scrittura'),
    ('amministrativo', 'azienda.lettura'),
    ('responsabile_tecnico', 'azienda.lettura')
) AS m (ruolo, permesso)
JOIN sec.ruoli r ON r.codice = m.ruolo AND r.azienda_id IS NULL
JOIN sec.permessi p ON p.codice = m.permesso;

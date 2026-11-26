-- 0010: rapporti di controllo di efficienza energetica (RCEE), bollini regionali,
-- tracciato dell'import massivo SIERT e invii al catasto (schema norma).
--
-- Fonti lette il 26/11/2026:
-- - SIERT, "Funzione import massivo RCEE", guida operativa v3.0
--   (https://siert.regione.toscana.it/upload/Istruzioni.pdf): file txt UTF-8
--   con valori separati da tabulazioni, solo RCEE tipo 1 (gruppi termici) e
--   tipo 2 (gruppi frigo), tabella dei campi numerati da 1 a 68.
-- - SIERT, "Manuale utente impresa di manutenzione", Rev. 21 del 14/09/2026
--   (https://siert.regione.toscana.it/documenti/Manuale_manutentori_20260914.pdf):
--   trasmissione entro 60 giorni dalla redazione, bollino di 13 cifre (le prime
--   4 sono l'anno di emissione), senza scadenza, da rilasciare contestualmente
--   al RCEE (art. 19 c. 8 DPGR 17/R/2023), importi per tipo e fascia
--   (LR 85/2016 art. 22 c. 4).
--
-- Decisioni del 26/11/2026:
-- - In M4 solo RCEE tipo 1 e 2, bollini e file di import; dichiarazione di
--   conformità e F-Gas in una milestone successiva.
-- - Nella guida il campo 46 manca in entrambe le tabelle: il file ha 67 colonne
--   nell'ordine della tabella finché il team SIERT non chiarisce. L'elenco delle
--   colonne è un dato con validità: una risposta diversa diventa una nuova versione.
-- - Tipi e importi dei bollini sono verificati, con fonte il manuale Rev. 21.
--
-- Il RCEE conserva in `dati` tutti i valori stampati sulla copia firmata, già
-- nella codifica del tracciato: il file si genera solo da lì (più il codice
-- ditta e il bollino), così la copia digitale coincide con quella cartacea anche
-- se l'anagrafica cambia dopo la firma.
--
-- Rimandati: bollino congiunto e generatori modulari (suffissi C1, M1 sul
-- numero di bollino), bozza creata dalla chiusura dell'intervento, esiti
-- dell'invio riportati sui rapporti.
--
-- Non modificare questo file dopo che è stato applicato: aggiungi una nuova migrazione.

-- ---------------------------------------------------------------------------
-- Dati di riferimento mancanti nelle anagrafiche
-- ---------------------------------------------------------------------------

ALTER TABLE sec.aziende
    ADD COLUMN codice_ditta_siert integer,
    ADD CONSTRAINT ck_aziende_codice_ditta_siert CHECK (codice_ditta_siert > 0);

COMMENT ON COLUMN sec.aziende.codice_ditta_siert IS 'ID_DITTA_MANUTENTORE assegnato dal team SIERT, visibile nell''area import massivo';

ALTER TABLE anagrafica.sedi
    ADD COLUMN codice_istat_comune text,
    ADD CONSTRAINT ck_sedi_codice_istat_comune CHECK (codice_istat_comune ~ '^[0-9]{6}$');

COMMENT ON COLUMN anagrafica.sedi.codice_istat_comune IS 'Codice ISTAT a 6 cifre del comune, richiesto dal tracciato SIERT';

ALTER TABLE impianti.generatori
    ADD COLUMN sigla_catasto text,
    ADD CONSTRAINT ck_generatori_sigla_catasto CHECK (sigla_catasto ~ '^[A-Z]{2}[0-9]{2}$');

-- Con il codice catasto la sigla identifica il generatore sul CIT.
CREATE UNIQUE INDEX uq_generatori_impianto_sigla_catasto
    ON impianti.generatori (impianto_id, sigla_catasto)
    WHERE sigla_catasto IS NOT NULL;

COMMENT ON COLUMN impianti.generatori.sigla_catasto IS 'Gruppo termico o frigo assegnato dal catasto (GT01, GF01)';

-- ---------------------------------------------------------------------------
-- Lookup normative
-- ---------------------------------------------------------------------------

CREATE TABLE norma.modelli_rapporto (
    modello_rapporto_id uuid        NOT NULL DEFAULT uuidv7(),
    codice              text        NOT NULL,
    nome                text        NOT NULL,
    -- Tipo di generatore dei bollini: GT a fiamma, GF frigo e pompe di calore,
    -- SC teleriscaldamento, CG cogenerazione.
    categoria_bollino   text        NOT NULL,
    valido_dal          date        NOT NULL,
    valido_al           date,
    da_verificare       boolean     NOT NULL DEFAULT true,
    fonte               text,
    creato_il           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_modelli_rapporto PRIMARY KEY (modello_rapporto_id),
    CONSTRAINT uq_modelli_rapporto_codice UNIQUE (codice),
    CONSTRAINT ck_modelli_rapporto_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_modelli_rapporto_categoria CHECK (categoria_bollino IN ('GT', 'GF', 'SC', 'CG')),
    CONSTRAINT ck_modelli_rapporto_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_modelli_rapporto_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

COMMENT ON TABLE norma.modelli_rapporto IS 'Modelli di RCEE gestiti; in M4 solo quelli dell''import massivo SIERT';

INSERT INTO norma.modelli_rapporto (codice, nome, categoria_bollino, valido_dal, da_verificare, fonte) VALUES
    ('tipo_1', 'RCEE tipo 1 - gruppi termici', 'GT', '2026-11-26', false,
     'SIERT, Funzione import massivo RCEE, guida operativa v3.0, letta il 26/11/2026'),
    ('tipo_2', 'RCEE tipo 2 - gruppi frigo', 'GF', '2026-11-26', false,
     'SIERT, Funzione import massivo RCEE, guida operativa v3.0, letta il 26/11/2026');

-- Termine per trasmettere il RCEE al catasto, in giorni dalla redazione.
CREATE TABLE norma.regole_trasmissione (
    regola_trasmissione_id uuid        NOT NULL DEFAULT uuidv7(),
    regione                text        NOT NULL,
    giorni                 integer     NOT NULL,
    valido_dal             date        NOT NULL,
    valido_al              date,
    da_verificare          boolean     NOT NULL DEFAULT true,
    fonte                  text,
    creato_il              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_regole_trasmissione PRIMARY KEY (regola_trasmissione_id),
    CONSTRAINT ck_regole_trasmissione_regione CHECK (regione IN ('toscana', 'lombardia')),
    CONSTRAINT ck_regole_trasmissione_giorni CHECK (giorni BETWEEN 1 AND 365),
    CONSTRAINT ck_regole_trasmissione_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_regole_trasmissione_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL),
    CONSTRAINT ex_regole_trasmissione_sovrapposte EXCLUDE USING gist (
        regione WITH =,
        daterange(valido_dal, valido_al, '[]') WITH &&
    )
);

COMMENT ON TABLE norma.regole_trasmissione IS 'Giorni per trasmettere il RCEE al catasto regionale, per regione e periodo';

INSERT INTO norma.regole_trasmissione (regione, giorni, valido_dal, da_verificare, fonte) VALUES
    ('toscana', 60, '2026-09-14', false,
     'SIERT, Manuale utente impresa di manutenzione Rev. 21 del 14/09/2026, par. 2.2 e 2.2.8');

-- Tipi di bollino: categoria di generatore e fascia di potenza utile (o di
-- superficie per il teleriscaldamento). Le fasce sono intervalli (da, a]: il
-- manuale dice "fino a 35 kW", "tra 35 kW e 100 kW", "superiore a 350 kW"
-- senza precisare a quale fascia appartengono gli estremi.
CREATE TABLE norma.tipi_bollino (
    tipo_bollino_id uuid          NOT NULL DEFAULT uuidv7(),
    codice          text          NOT NULL,
    regione         text          NOT NULL,
    categoria       text          NOT NULL,
    nome            text          NOT NULL,
    importo         numeric(12,2) NOT NULL,
    grandezza       text          NOT NULL,
    fascia_da       numeric(9,2),
    fascia_a        numeric(9,2),
    valido_dal      date          NOT NULL,
    valido_al       date,
    da_verificare   boolean       NOT NULL DEFAULT true,
    fonte           text,
    creato_il       timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT pk_tipi_bollino PRIMARY KEY (tipo_bollino_id),
    CONSTRAINT uq_tipi_bollino_codice UNIQUE (codice),
    CONSTRAINT ck_tipi_bollino_codice CHECK (codice ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_tipi_bollino_regione CHECK (regione IN ('toscana', 'lombardia')),
    CONSTRAINT ck_tipi_bollino_categoria CHECK (categoria IN ('GT', 'GF', 'SC', 'CG')),
    CONSTRAINT ck_tipi_bollino_importo CHECK (importo >= 0),
    CONSTRAINT ck_tipi_bollino_grandezza CHECK (grandezza IN ('potenza_utile_kw', 'superficie_utile_m2')),
    CONSTRAINT ck_tipi_bollino_fascia CHECK (
        (fascia_da IS NULL OR fascia_da >= 0)
        AND (fascia_da IS NULL OR fascia_a IS NULL OR fascia_a > fascia_da)
    ),
    CONSTRAINT ck_tipi_bollino_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_tipi_bollino_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL),
    CONSTRAINT ex_tipi_bollino_sovrapposti EXCLUDE USING gist (
        regione WITH =,
        categoria WITH =,
        numrange(fascia_da, fascia_a, '(]') WITH &&,
        daterange(valido_dal, valido_al, '[]') WITH &&
    )
);

COMMENT ON TABLE norma.tipi_bollino IS 'Tipologie e importi dei bollini regionali per categoria e fascia';

INSERT INTO norma.tipi_bollino (codice, regione, categoria, nome, importo, grandezza, fascia_da, fascia_a, valido_dal, da_verificare, fonte)
SELECT v.codice, 'toscana', v.categoria, v.nome, v.importo, v.grandezza, v.da, v.a, '2026-09-14', false,
       'SIERT, Manuale utente impresa di manutenzione Rev. 21 del 14/09/2026, par. 2.3.3; LR 85/2016 art. 22 c. 4'
FROM (VALUES
    ('gt_fino_35',     'GT', 'GT - Pn utile fino a 35 kW',           20.00, 'potenza_utile_kw',    NULL::numeric, 35::numeric),
    ('gt_35_100',      'GT', 'GT - Pn utile tra 35 kW e 100 kW',     20.00, 'potenza_utile_kw',    35,   100),
    ('gt_100_350',     'GT', 'GT - Pn utile tra 100 kW e 350 kW',    60.00, 'potenza_utile_kw',    100,  350),
    ('gt_oltre_350',   'GT', 'GT - Pn utile superiore a 350 kW',     80.00, 'potenza_utile_kw',    350,  NULL),
    ('gf_fino_35',     'GF', 'GF - Pn utile fino a 35 kW',           10.00, 'potenza_utile_kw',    NULL, 35),
    ('gf_35_100',      'GF', 'GF - Pn utile tra 35 kW e 100 kW',     12.00, 'potenza_utile_kw',    35,   100),
    ('gf_oltre_100',   'GF', 'GF - Pn utile superiore a 100 kW',     20.00, 'potenza_utile_kw',    100,  NULL),
    ('sc_fino_100_m2', 'SC', 'SC - superficie utile fino a 100 m²',  10.00, 'superficie_utile_m2', NULL, 100),
    ('sc_fino_200_m2', 'SC', 'SC - superficie utile fino a 200 m²',  20.00, 'superficie_utile_m2', 100,  200),
    ('sc_fino_300_m2', 'SC', 'SC - superficie utile fino a 300 m²',  30.00, 'superficie_utile_m2', 200,  300),
    ('sc_fino_400_m2', 'SC', 'SC - superficie utile fino a 400 m²',  40.00, 'superficie_utile_m2', 300,  400),
    ('sc_fino_500_m2', 'SC', 'SC - superficie utile fino a 500 m²',  50.00, 'superficie_utile_m2', 400,  500),
    ('cg_fino_100',    'CG', 'CG - Pn utile fino a 100 kW',          15.00, 'potenza_utile_kw',    NULL, 100),
    ('cg_100_350',     'CG', 'CG - Pn utile tra 100 kW e 350 kW',    20.00, 'potenza_utile_kw',    100,  350),
    ('cg_oltre_350',   'CG', 'CG - Pn utile superiore a 350 kW',     30.00, 'potenza_utile_kw',    350,  NULL)
) AS v (codice, categoria, nome, importo, grandezza, da, a);

-- ---------------------------------------------------------------------------
-- Tracciato dell'import massivo SIERT, come dati
-- ---------------------------------------------------------------------------

-- Una riga per colonna del file, nell'ordine di `posizione`. `numero_campo` è
-- il numero della guida (manca il 46). `origine` dice da dove viene il valore:
-- `dati` (chiave del JSON del rapporto), `rapporto` (colonna del rapporto),
-- `azienda` (codice ditta SIERT), `bollino` (codice del bollino assegnato).
-- I nomi sono scritti come nella guida, refusi compresi.
CREATE TABLE norma.tracciati_campi (
    tracciato_campo_id  uuid        NOT NULL DEFAULT uuidv7(),
    sistema             text        NOT NULL,
    versione            text        NOT NULL,
    modello_rapporto_id uuid        NOT NULL,
    posizione           integer     NOT NULL,
    numero_campo        integer     NOT NULL,
    nome                text        NOT NULL,
    tipo                text        NOT NULL,
    lunghezza           integer,
    obbligatorio        boolean     NOT NULL,
    valori_ammessi      text[],
    formato             text,
    origine             text        NOT NULL,
    chiave              text        NOT NULL,
    valido_dal          date        NOT NULL,
    valido_al           date,
    da_verificare       boolean     NOT NULL DEFAULT true,
    fonte               text,
    creato_il           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_tracciati_campi PRIMARY KEY (tracciato_campo_id),
    CONSTRAINT uq_tracciati_campi_posizione UNIQUE (sistema, versione, modello_rapporto_id, posizione),
    CONSTRAINT uq_tracciati_campi_numero UNIQUE (sistema, versione, modello_rapporto_id, numero_campo),
    CONSTRAINT uq_tracciati_campi_chiave UNIQUE (sistema, versione, modello_rapporto_id, origine, chiave),
    CONSTRAINT fk_tracciati_campi_modelli FOREIGN KEY (modello_rapporto_id) REFERENCES norma.modelli_rapporto (modello_rapporto_id),
    CONSTRAINT ck_tracciati_campi_sistema CHECK (sistema IN ('siert', 'curit')),
    CONSTRAINT ck_tracciati_campi_posizione CHECK (posizione >= 1),
    CONSTRAINT ck_tracciati_campi_numero CHECK (numero_campo >= 1),
    CONSTRAINT ck_tracciati_campi_tipo CHECK (tipo IN ('testo', 'intero', 'decimale', 'data', 'ora')),
    CONSTRAINT ck_tracciati_campi_lunghezza CHECK (lunghezza > 0),
    CONSTRAINT ck_tracciati_campi_origine CHECK (origine IN ('dati', 'rapporto', 'azienda', 'bollino')),
    CONSTRAINT ck_tracciati_campi_chiave CHECK (chiave ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT ck_tracciati_campi_validita CHECK (valido_al IS NULL OR valido_al >= valido_dal),
    CONSTRAINT ck_tracciati_campi_fonte CHECK (da_verificare OR nullif(btrim(fonte), '') IS NOT NULL)
);

COMMENT ON TABLE norma.tracciati_campi IS 'Colonne del file di invio al catasto per modello e versione del tracciato';

-- Codifiche ricorrenti della guida: SI=3 NO=4 NC=9 NON RICHIESTO=16;
-- trattamento dell'acqua 16-23; funzionamento in sicurezza SI=21 NO=13.
INSERT INTO norma.tracciati_campi (
    sistema, versione, modello_rapporto_id, posizione, numero_campo, nome, tipo, lunghezza,
    obbligatorio, valori_ammessi, formato, origine, chiave, valido_dal, da_verificare, fonte
)
SELECT 'siert', '3.0', m.modello_rapporto_id,
       row_number() OVER (PARTITION BY m.codice ORDER BY v.numero)::integer,
       v.numero, v.nome, v.tipo, v.lunghezza, v.obbligatorio, v.valori, v.formato, v.origine, v.chiave,
       '2026-11-26', false,
       'SIERT, Funzione import massivo RCEE, guida operativa v3.0, letta il 26/11/2026'
FROM (VALUES
    -- RCEE tipo 1, gruppi termici
    ('tipo_1',  1, 'CODICE_CATASTO', 'testo', 10, true, NULL::text[], '^[0-9]{10}$', 'dati', 'codice_catasto'),
    ('tipo_1',  2, 'POTENZA_TOTALE', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_totale'),
    ('tipo_1',  3, 'COMUNE', 'testo', 6, true, NULL, '^[0-9]{6}$', 'dati', 'comune'),
    ('tipo_1',  4, 'INDIRIZZO_IMPIANTO', 'testo', 400, true, NULL, NULL, 'dati', 'indirizzo_impianto'),
    ('tipo_1',  5, 'RESPONSABILE_IMPIANTO', 'testo', 200, true, NULL, NULL, 'dati', 'responsabile_impianto'),
    ('tipo_1',  6, 'CODICE_FISCALE', 'testo', 16, true, NULL, NULL, 'dati', 'codice_fiscale'),
    ('tipo_1',  7, 'RAGIONE_SOCIALE', 'testo', 200, false, NULL, NULL, 'dati', 'ragione_sociale'),
    ('tipo_1',  8, 'PARTITA_ IVA', 'testo', 11, false, NULL, '^[0-9]{11}$', 'dati', 'partita_iva'),
    ('tipo_1',  9, 'RESIDENZA_RESPONSABILE', 'testo', 300, false, NULL, NULL, 'dati', 'residenza_responsabile'),
    ('tipo_1', 10, 'TITOLO_RESPONSABILE', 'testo', 50, true, ARRAY['PROPRIETARIO', 'OCCUPANTE', 'AMMINISTRATORE CONDOMINIO', 'TERZO RESPONSABILE'], NULL, 'dati', 'titolo_responsabile'),
    ('tipo_1', 11, 'ID_DITTA_MANUTENTORE', 'intero', NULL, true, NULL, NULL, 'azienda', 'codice_ditta_siert'),
    ('tipo_1', 12, 'DICHIARAZIONE_CONFORMITA', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'dichiarazione_conformita'),
    ('tipo_1', 13, 'LIBRETTO_USO_MANUTENZIONE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_uso_manutenzione'),
    ('tipo_1', 14, 'LIBRETTO_CLIMATIZZAZIONE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_climatizzazione'),
    ('tipo_1', 15, 'LIBRETTO_CLIMATTIZAZIONE_COMPILATO_CORRETTAMENTE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_compilato_correttamente'),
    ('tipo_1', 16, 'DUREZZA_ACQUA', 'intero', NULL, false, NULL, '^[0-9]{1,2}$', 'dati', 'durezza_acqua'),
    ('tipo_1', 17, 'TRATTAMENTO_RISCALDAMENTO', 'intero', NULL, true, ARRAY['16', '17', '18', '19', '20', '21', '22', '23'], NULL, 'dati', 'trattamento_riscaldamento'),
    ('tipo_1', 18, 'TRATTAMENTO_ACS', 'intero', NULL, true, ARRAY['16', '17', '18', '19', '20', '21', '22', '23'], NULL, 'dati', 'trattamento_acs'),
    ('tipo_1', 19, 'LOCALE_IDONEO', 'intero', NULL, true, ARRAY['3', '4', '9', '16'], NULL, 'dati', 'locale_idoneo'),
    ('tipo_1', 20, 'GENERATORE_IDONEO_INSTALLAZIONE_ESTERNA', 'intero', NULL, true, ARRAY['3', '4', '9', '16'], NULL, 'dati', 'generatore_idoneo_installazione_esterna'),
    ('tipo_1', 21, 'APERTURE_VENTILAZIONE_AREAZIONE_LIBERE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'aperture_ventilazione_libere'),
    ('tipo_1', 22, 'ADEGUATE_ DIMENSIONI_APERTURE_VENTILAZIONE_AEREAZIONE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'aperture_ventilazione_adeguate'),
    ('tipo_1', 23, 'CANALE_FUMO_IDONEO', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'canale_fumo_idoneo'),
    ('tipo_1', 24, 'SISTEMA_REGOLAZIONE_TEMPERATURA_FUNZIONANTE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'regolazione_temperatura_funzionante'),
    ('tipo_1', 25, 'ASSENZA_PERDITA_LIQUIDO_COMBUSTIBILE', 'intero', NULL, true, ARRAY['3', '4', '9', '16'], NULL, 'dati', 'assenza_perdite_combustibile'),
    ('tipo_1', 26, 'IDONEA_TENUTA_IMPIANTO', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'idonea_tenuta_impianto'),
    ('tipo_1', 27, 'DATA_INSTALLAZIONE', 'data', NULL, true, NULL, NULL, 'dati', 'data_installazione'),
    ('tipo_1', 28, 'FABBRICANTE', 'testo', 500, true, NULL, NULL, 'dati', 'fabbricante'),
    ('tipo_1', 29, 'MODELLO', 'testo', 500, true, NULL, NULL, 'dati', 'modello'),
    ('tipo_1', 30, 'MATRICOLA', 'testo', 200, true, NULL, NULL, 'dati', 'matricola'),
    ('tipo_1', 31, 'GRUPPO_TERMICO', 'testo', 4, true, NULL, '^[A-Z]{2}[0-9]{2}$', 'dati', 'gruppo'),
    ('tipo_1', 32, 'TIPO_GENERATORE', 'testo', 100, true, ARRAY['GRUPPO TERMICO SINGOLO', 'TUBO NASTRO RADIANTE', 'GRUPPO TERMICO MODULARE', 'GENERATORE ARIA CALDAIA'], NULL, 'dati', 'tipo_generatore'),
    ('tipo_1', 33, 'POTENZA_FOCOLARE', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_focolare'),
    ('tipo_1', 34, 'POTENZA_UTILE', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_utile'),
    ('tipo_1', 35, 'CLIMATIZZAZIONE INVERNALE', 'testo', 2, true, ARRAY['SI', 'NO'], NULL, 'dati', 'climatizzazione_invernale'),
    ('tipo_1', 36, 'PRODUZIONE ACS (ACQUA CALDA SANITARIA)', 'testo', 2, true, ARRAY['SI', 'NO'], NULL, 'dati', 'produzione_acs'),
    -- Ammessi GPL, Gas naturale, Gasolio oppure testo libero: nessun elenco chiuso.
    ('tipo_1', 37, 'COMBUSTIBILE', 'testo', 50, true, NULL, NULL, 'dati', 'combustibile'),
    ('tipo_1', 38, 'EVACUAZIONE_FUMI', 'testo', 10, true, ARRAY['Naturale', 'Forzata'], NULL, 'dati', 'evacuazione_fumi'),
    -- Obbligatorio con GPL o gas naturale e tiraggio naturale: lo controlla valida_rapporto.
    ('tipo_1', 39, 'DEPRESSIONE', 'intero', NULL, false, NULL, NULL, 'dati', 'depressione'),
    ('tipo_1', 40, 'DISPOSITIVI_COMANDO_REGOLAZIONE_FUNZIONANTI', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'dispositivi_comando_funzionanti'),
    ('tipo_1', 41, 'DISPOSITIVI_SICUREZZA_NON_MANOMESSI_CORTOCIRCUITATI', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'dispositivi_sicurezza_integri'),
    ('tipo_1', 42, 'VALVOLA_SICUREZZA', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'valvola_sicurezza'),
    ('tipo_1', 43, 'CONTROLLO_PULIZIA_SCAMBIATORE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'pulizia_scambiatore'),
    ('tipo_1', 44, 'PRESENZA_RIFLUSSI', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'presenza_riflussi'),
    ('tipo_1', 45, 'RISULTATI_UNI_10389', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'risultati_uni_10389'),
    ('tipo_1', 47, 'TEMPERATURA_FUMI', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_fumi'),
    ('tipo_1', 48, 'TEMPERATURA_ARIA_COMBURENTE', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_aria_comburente'),
    ('tipo_1', 49, 'O2', 'decimale', NULL, true, NULL, NULL, 'dati', 'o2'),
    ('tipo_1', 50, 'CO2', 'decimale', NULL, true, NULL, NULL, 'dati', 'co2'),
    -- Obbligatorio solo con gasolio: tre misure separate da uno spazio (1 2 1).
    ('tipo_1', 51, 'BACHARAC', 'testo', 10, false, NULL, '^[0-9] [0-9] [0-9]$', 'dati', 'bacharach'),
    ('tipo_1', 52, 'CO', 'decimale', NULL, true, NULL, NULL, 'dati', 'co'),
    ('tipo_1', 53, 'RENDIMENTO_COMBUSTIONE', 'decimale', NULL, true, NULL, NULL, 'dati', 'rendimento_combustione'),
    ('tipo_1', 54, 'RENDIMENTO_MINIMO_LEGGE', 'decimale', NULL, false, NULL, NULL, 'dati', 'rendimento_minimo'),
    ('tipo_1', 55, 'ADOZIONE_VALVOLE_TERMOSTATICHE', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'valvole_termostatiche'),
    ('tipo_1', 56, 'ISOLAMENTO_RETE_DISTRIBUZIONE', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'isolamento_rete_distribuzione'),
    ('tipo_1', 57, 'SISTEMA_TRATTAMENTO_ACQUA', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'sistema_trattamento_acqua'),
    ('tipo_1', 58, 'SOSTITUZIONE_ REGOLAZIONE_ ON/OFF', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'sostituzione_regolazione_on_off'),
    ('tipo_1', 59, 'OSSERVAZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'osservazioni'),
    ('tipo_1', 60, 'RACCOMANDAZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'raccomandazioni'),
    ('tipo_1', 61, 'PRESCRIZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'prescrizioni'),
    ('tipo_1', 62, 'IMPIANTO_PUO_FUNZIONARE_IN_SICUREZZA', 'intero', NULL, true, ARRAY['21', '13'], NULL, 'dati', 'funziona_in_sicurezza'),
    ('tipo_1', 63, 'MANUTENZIONE_ENTRO', 'data', NULL, false, NULL, NULL, 'dati', 'manutenzione_entro'),
    ('tipo_1', 64, 'DATA_CONTROLLO', 'data', NULL, true, NULL, NULL, 'rapporto', 'data_controllo'),
    ('tipo_1', 65, 'ORARIO_ARRIVO', 'ora', 5, false, NULL, NULL, 'dati', 'orario_arrivo'),
    ('tipo_1', 66, 'ORARIO_PARTENZA', 'ora', 5, false, NULL, NULL, 'dati', 'orario_partenza'),
    ('tipo_1', 67, 'MANUTENTORE', 'testo', 200, true, NULL, NULL, 'dati', 'manutentore'),
    ('tipo_1', 68, 'NUMERO_BOLLINO', 'testo', 50, true, NULL, NULL, 'bollino', 'codice'),

    -- RCEE tipo 2, gruppi frigo
    ('tipo_2',  1, 'CODICE_CATASTO', 'testo', 10, true, NULL, '^[0-9]{10}$', 'dati', 'codice_catasto'),
    ('tipo_2',  2, 'POTENZA_TOTALE', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_totale'),
    ('tipo_2',  3, 'COMUNE', 'testo', 6, true, NULL, '^[0-9]{6}$', 'dati', 'comune'),
    ('tipo_2',  4, 'INDIRIZZO_IMPIANTO', 'testo', 400, true, NULL, NULL, 'dati', 'indirizzo_impianto'),
    ('tipo_2',  5, 'RESPONSABILE_IMPIANTO', 'testo', 200, true, NULL, NULL, 'dati', 'responsabile_impianto'),
    ('tipo_2',  6, 'CODICE_FISCALE', 'testo', 16, true, NULL, NULL, 'dati', 'codice_fiscale'),
    ('tipo_2',  7, 'RAGIONE_SOCIALE', 'testo', 200, false, NULL, NULL, 'dati', 'ragione_sociale'),
    ('tipo_2',  8, 'PARTITA_ IVA', 'testo', 11, false, NULL, '^[0-9]{11}$', 'dati', 'partita_iva'),
    ('tipo_2',  9, 'RESIDENZA_RESPONSABILE', 'testo', 300, false, NULL, NULL, 'dati', 'residenza_responsabile'),
    ('tipo_2', 10, 'TITOLO_RESPONSABILE', 'testo', 50, true, ARRAY['PROPRIETARIO', 'OCCUPANTE', 'AMMINISTRATORE CONDOMINIO', 'TERZO RESPONSABILE'], NULL, 'dati', 'titolo_responsabile'),
    ('tipo_2', 11, 'ID_DITTA_MANUTENTORE', 'intero', NULL, true, NULL, NULL, 'azienda', 'codice_ditta_siert'),
    ('tipo_2', 12, 'DICHIARAZIONE_CONFORMITA', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'dichiarazione_conformita'),
    ('tipo_2', 13, 'LIBRETTO_USO_MANUTENZIONE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_uso_manutenzione'),
    ('tipo_2', 14, 'LIBRETTO_CLIMATIZZAZIONE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_climatizzazione'),
    ('tipo_2', 15, 'LIBRETTO_CLIMATTIZAZIONE_COMPILATO_CORRETTAMENTE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'libretto_compilato_correttamente'),
    ('tipo_2', 16, 'DUREZZA_ACQUA', 'intero', NULL, false, NULL, '^[0-9]{1,2}$', 'dati', 'durezza_acqua'),
    ('tipo_2', 17, 'TRATTAMENTO_ACQUA_PRESENTE', 'intero', NULL, true, ARRAY['1', '17'], NULL, 'dati', 'trattamento_acqua_presente'),
    ('tipo_2', 18, 'TIPOLOGIA_TRATTAMENTO_ACQUA', 'intero', NULL, true, ARRAY['16', '17', '18', '19', '20', '21', '22', '23'], NULL, 'dati', 'tipologia_trattamento_acqua'),
    ('tipo_2', 19, 'LOCALE_IDONEO', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'locale_idoneo'),
    ('tipo_2', 20, 'LINEE ELETTRICHE IDONEE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'linee_elettriche_idonee'),
    ('tipo_2', 21, 'APERTURE_VENTILAZIONE_AREAZIONE_LIBERE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'aperture_ventilazione_libere'),
    ('tipo_2', 22, 'ADEGUATE_ DIMENSIONI_APERTURE_VENTILAZIONE_AEREAZIONE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'aperture_ventilazione_adeguate'),
    ('tipo_2', 23, 'COIBENTAZIONI IDONEE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'coibentazioni_idonee'),
    ('tipo_2', 24, 'MACCHINE FRIGORIFERE E/O POMPE DI CALORE A COMPRESSIONE DI VAPORE AD AZIONAMENTO ELETTRICO E AD ASSORBIMENTO A FIAMMA DIRETTA', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'tipologia_compressione_elettrica'),
    ('tipo_2', 25, 'POMPE DI CALORE A COMPRESSIONE DI VAPORE AZIONATE DA MOTORE ENDOTERMICO', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'tipologia_motore_endotermico'),
    ('tipo_2', 26, 'POMPE DI CALORE AD ASSORBIMENTO ALIMENTATE CON ENERGIA TERMICA', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'tipologia_assorbimento_termico'),
    ('tipo_2', 27, 'DATA_INSTALLAZIONE', 'data', NULL, true, NULL, NULL, 'dati', 'data_installazione'),
    ('tipo_2', 28, 'FABBRICANTE', 'testo', 500, true, NULL, NULL, 'dati', 'fabbricante'),
    ('tipo_2', 29, 'MODELLO', 'testo', 500, true, NULL, NULL, 'dati', 'modello'),
    ('tipo_2', 30, 'MATRICOLA', 'testo', 200, true, NULL, NULL, 'dati', 'matricola'),
    ('tipo_2', 31, 'GRUPPO_FRIGO', 'testo', 4, true, NULL, '^[A-Z]{2}[0-9]{2}$', 'dati', 'gruppo'),
    -- "COMNBUSTIBLE" è scritto così nella guida: da confermare con il team SIERT.
    ('tipo_2', 32, 'TIPO_GRUPPO_FRIGO', 'testo', 100, true, ARRAY['AD ASSORBIMENTO PER RECUPERO CALORE', 'AD ASSORBIMENTO A FIAMMA DIRETTA CON COMNBUSTIBLE', 'A CICLO DI COMPRESSIONE CON MOTORE ELETTRICO O ENDOTERMICO'], NULL, 'dati', 'tipo_gruppo_frigo'),
    ('tipo_2', 33, 'POTENZA FRIGORIFERA NOMNINALE IN RAFFRESCAMENTO', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_frigorifera'),
    ('tipo_2', 34, 'POTENZA TERMICA NOMINALE IN RISCALDAMENTO', 'decimale', NULL, true, NULL, NULL, 'dati', 'potenza_termica'),
    ('tipo_2', 35, 'PROVA ESEGUITA IN RAFFRESCAMENTO', 'testo', 2, true, ARRAY['SI', 'NO'], NULL, 'dati', 'prova_raffrescamento'),
    ('tipo_2', 36, 'PROVA ESEGUITA IN RISCALDAMENTO', 'testo', 2, true, ARRAY['SI', 'NO'], NULL, 'dati', 'prova_riscaldamento'),
    -- Ammessi GPL, Gas naturale, Gasolio, NESSUNO oppure testo libero.
    ('tipo_2', 37, 'COMBUSTIBILE', 'testo', 50, true, NULL, NULL, 'dati', 'combustibile'),
    ('tipo_2', 38, 'FLUIDO LATO UTENZE', 'testo', 10, true, ARRAY['ACQUA', 'ARIA', 'ALTRO'], NULL, 'dati', 'fluido_lato_utenze'),
    ('tipo_2', 39, 'NUMERO CIRCUITO', 'intero', NULL, false, NULL, NULL, 'dati', 'numero_circuito'),
    ('tipo_2', 40, 'ASSENZA PERDITA GAS RIFRIGERANTE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'assenza_perdite_refrigerante'),
    ('tipo_2', 41, 'PRESENZA APPARECCHIATURA AUTOMATICA RILEVAZIONE DIRETTA FUGHE REFRIGERANTE (LEAK DETECTOR)', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'rilevazione_diretta_fughe'),
    ('tipo_2', 42, 'PRESENZA APPARECCHIATURA AUTOMATICA RILEVAZIONE INDIRETTA FUGHE REFRIGERANTE (PARAMETRI TERMODINAMICI)', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'rilevazione_indiretta_fughe'),
    ('tipo_2', 43, 'CONTROLLO_PULIZIA_SCAMBIATORE', 'intero', NULL, true, ARRAY['3', '4', '9'], NULL, 'dati', 'pulizia_scambiatore'),
    ('tipo_2', 44, 'GENERATORE MODULARE', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'generatore_modulare'),
    ('tipo_2', 45, 'ESGUITA PROVA', 'intero', NULL, true, ARRAY['3', '4'], NULL, 'dati', 'prova_eseguita'),
    -- Misure della prova di funzionalità: 0 se la prova non è stata eseguita.
    ('tipo_2', 47, 'SURRISACALDAMENTO', 'decimale', NULL, true, NULL, NULL, 'dati', 'surriscaldamento'),
    ('tipo_2', 48, 'SOTTORAFFREDDAMENTO', 'decimale', NULL, true, NULL, NULL, 'dati', 'sottoraffreddamento'),
    ('tipo_2', 49, 'TEMPERATURA CONDENSAZIONE', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_condensazione'),
    ('tipo_2', 50, 'TEMPERATURA EVAPORAZIONE', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_evaporazione'),
    ('tipo_2', 51, 'TEMPERATURA INGRESSO LATO ESTERNO', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_ingresso_esterno'),
    ('tipo_2', 52, 'TEMPERATURA USCITA LATO ESTERNO', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_uscita_esterno'),
    ('tipo_2', 53, 'TEMPERATURA INGRESSO LATO UTENZE', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_ingresso_utenze'),
    ('tipo_2', 54, 'TEMPERATURA USCITA LATO UTENZE', 'decimale', NULL, true, NULL, NULL, 'dati', 'temperatura_uscita_utenze'),
    ('tipo_2', 55, 'SOSTITUZIONE_ GENERATORE_ ON/OFF', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'sostituzione_generatore'),
    ('tipo_2', 56, 'SOSTITUZIONE_ REGOLAZIONE_ ON/OFF', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'sostituzione_regolazione_on_off'),
    ('tipo_2', 57, 'ISOLAMENTO_RETE_DISTRIBUZIONE', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'isolamento_rete_distribuzione'),
    ('tipo_2', 58, 'ISOLAMENTO CANALI DI DISTRIUZIONE', 'testo', 4, false, ARRAY['SI'], NULL, 'dati', 'isolamento_canali_distribuzione'),
    ('tipo_2', 59, 'OSSERVAZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'osservazioni'),
    ('tipo_2', 60, 'RACCOMANDAZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'raccomandazioni'),
    ('tipo_2', 61, 'PRESCRIZIONI', 'testo', 400, false, NULL, NULL, 'dati', 'prescrizioni'),
    ('tipo_2', 62, 'IMPIANTO_PUO_FUNZIONARE_IN_SICUREZZA', 'intero', NULL, true, ARRAY['21', '13'], NULL, 'dati', 'funziona_in_sicurezza'),
    ('tipo_2', 63, 'MANUTENZIONE_ENTRO', 'data', NULL, false, NULL, NULL, 'dati', 'manutenzione_entro'),
    ('tipo_2', 64, 'DATA_CONTROLLO', 'data', NULL, true, NULL, NULL, 'rapporto', 'data_controllo'),
    ('tipo_2', 65, 'ORARIO_ARRIVO', 'ora', 5, false, NULL, NULL, 'dati', 'orario_arrivo'),
    ('tipo_2', 66, 'ORARIO_PARTENZA', 'ora', 5, false, NULL, NULL, 'dati', 'orario_partenza'),
    ('tipo_2', 67, 'MANUTENTORE', 'testo', 200, true, NULL, NULL, 'dati', 'manutentore'),
    ('tipo_2', 68, 'NUMERO_BOLLINO', 'testo', 50, true, NULL, NULL, 'bollino', 'codice')
) AS v (modello, numero, nome, tipo, lunghezza, obbligatorio, valori, formato, origine, chiave)
JOIN norma.modelli_rapporto m ON m.codice = v.modello;

-- ---------------------------------------------------------------------------
-- Bollini
-- ---------------------------------------------------------------------------

-- Registro dei bollini acquistati dall'impresa sul portale SIERT.
-- Stati: disponibile -> assegnato (a un rapporto in bozza o convalidato) ->
-- usato (rapporto firmato); assegnato -> disponibile se il rapporto lo libera
-- o viene annullato prima della firma; disponibile -> annullato (smarrito o
-- errato). Usato e annullato sono finali.
CREATE TABLE norma.bollini (
    bollino_id      uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id      uuid        NOT NULL,
    tipo_bollino_id uuid        NOT NULL,
    codice          text        NOT NULL,
    acquistato_il   date        NOT NULL,
    stato           text        NOT NULL DEFAULT 'disponibile',
    note            text,
    creato_il       timestamptz NOT NULL DEFAULT now(),
    creato_da       uuid,
    modificato_il   timestamptz,
    modificato_da   uuid,
    versione        integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_bollini PRIMARY KEY (bollino_id),
    CONSTRAINT uq_bollini_azienda_bollino UNIQUE (azienda_id, bollino_id),
    -- Unico per azienda: un vincolo globale rivelerebbe i bollini di un'altra azienda.
    CONSTRAINT uq_bollini_azienda_codice UNIQUE (azienda_id, codice),
    CONSTRAINT fk_bollini_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_bollini_tipi FOREIGN KEY (tipo_bollino_id) REFERENCES norma.tipi_bollino (tipo_bollino_id),
    CONSTRAINT fk_bollini_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_bollini_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    -- 13 cifre, le prime quattro sono l'anno di emissione.
    CONSTRAINT ck_bollini_codice CHECK (codice ~ '^(19|20)[0-9]{11}$'),
    CONSTRAINT ck_bollini_stato CHECK (stato IN ('disponibile', 'assegnato', 'usato', 'annullato')),
    CONSTRAINT ck_bollini_versione CHECK (versione >= 1)
);

CREATE INDEX ix_bollini_disponibili
    ON norma.bollini (azienda_id, tipo_bollino_id, acquistato_il, codice)
    WHERE stato = 'disponibile';

COMMENT ON TABLE norma.bollini IS 'Bollini regionali acquistati: disponibili, assegnati, usati, annullati';

-- ---------------------------------------------------------------------------
-- Rapporti di controllo
-- ---------------------------------------------------------------------------

-- Stati: bozza <-> convalidato (nessun errore bloccante) -> firmato (copia
-- cartacea firmata, dati bloccati) -> trasmesso o scartato dal catasto;
-- scartato -> trasmesso dopo un nuovo invio. Si annulla in ogni stato tranne
-- trasmesso; non si cancella.
CREATE TABLE norma.rapporti_controllo (
    rapporto_controllo_id uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id            uuid        NOT NULL,
    impianto_id           uuid        NOT NULL,
    generatore_id         uuid        NOT NULL,
    intervento_id         uuid,
    modello_rapporto_id   uuid        NOT NULL,
    tecnico_id            uuid,
    data_controllo        date        NOT NULL,
    termine_trasmissione  date,
    dati                  jsonb       NOT NULL DEFAULT '{}',
    bollino_id            uuid,
    stato                 text        NOT NULL DEFAULT 'bozza',
    convalidato_il        timestamptz,
    convalidato_da        uuid,
    firmato_il            timestamptz,
    firmato_da            uuid,
    annullato_il          timestamptz,
    annullato_da          uuid,
    motivo_annullamento   text,
    creato_il             timestamptz NOT NULL DEFAULT now(),
    creato_da             uuid,
    modificato_il         timestamptz,
    modificato_da         uuid,
    versione              integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_rapporti_controllo PRIMARY KEY (rapporto_controllo_id),
    CONSTRAINT uq_rapporti_controllo_azienda_rapporto UNIQUE (azienda_id, rapporto_controllo_id),
    -- Un bollino vale per un solo rapporto (il bollino congiunto arriva dopo).
    CONSTRAINT uq_rapporti_controllo_bollino UNIQUE (bollino_id),
    CONSTRAINT fk_rapporti_controllo_impianti FOREIGN KEY (azienda_id, impianto_id) REFERENCES impianti.impianti (azienda_id, impianto_id),
    CONSTRAINT fk_rapporti_controllo_generatori FOREIGN KEY (azienda_id, generatore_id) REFERENCES impianti.generatori (azienda_id, generatore_id),
    CONSTRAINT fk_rapporti_controllo_interventi FOREIGN KEY (azienda_id, intervento_id) REFERENCES lavoro.interventi (azienda_id, intervento_id),
    CONSTRAINT fk_rapporti_controllo_modelli FOREIGN KEY (modello_rapporto_id) REFERENCES norma.modelli_rapporto (modello_rapporto_id),
    CONSTRAINT fk_rapporti_controllo_bollini FOREIGN KEY (azienda_id, bollino_id) REFERENCES norma.bollini (azienda_id, bollino_id),
    CONSTRAINT fk_rapporti_controllo_tecnico FOREIGN KEY (tecnico_id) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_rapporti_controllo_convalidato_da FOREIGN KEY (convalidato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_rapporti_controllo_firmato_da FOREIGN KEY (firmato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_rapporti_controllo_annullato_da FOREIGN KEY (annullato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_rapporti_controllo_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_rapporti_controllo_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_rapporti_controllo_stato CHECK (stato IN ('bozza', 'convalidato', 'firmato', 'trasmesso', 'scartato', 'annullato')),
    CONSTRAINT ck_rapporti_controllo_dati CHECK (jsonb_typeof(dati) = 'object'),
    CONSTRAINT ck_rapporti_controllo_firma CHECK (stato NOT IN ('firmato', 'trasmesso', 'scartato') OR (firmato_il IS NOT NULL AND bollino_id IS NOT NULL)),
    CONSTRAINT ck_rapporti_controllo_annullamento CHECK (
        (stato = 'annullato') = (annullato_il IS NOT NULL)
        AND (stato <> 'annullato' OR nullif(btrim(motivo_annullamento), '') IS NOT NULL)
    ),
    CONSTRAINT ck_rapporti_controllo_versione CHECK (versione >= 1)
);

CREATE INDEX ix_rapporti_controllo_impianto ON norma.rapporti_controllo (impianto_id, data_controllo DESC);
CREATE INDEX ix_rapporti_controllo_intervento ON norma.rapporti_controllo (intervento_id) WHERE intervento_id IS NOT NULL;
CREATE INDEX ix_rapporti_controllo_da_trasmettere
    ON norma.rapporti_controllo (azienda_id, termine_trasmissione)
    WHERE stato IN ('bozza', 'convalidato', 'firmato', 'scartato');

COMMENT ON TABLE norma.rapporti_controllo IS 'RCEE: dati della copia firmata nella codifica del tracciato; bloccato dopo la firma';
COMMENT ON COLUMN norma.rapporti_controllo.dati IS 'Valori del RCEE per chiave di norma.tracciati_campi (origine dati)';

-- ---------------------------------------------------------------------------
-- Invii al catasto
-- ---------------------------------------------------------------------------

-- Un file di import massivo per modello. Il contenuto è quello caricato sul
-- portale e non cambia; la ricevuta è il report PDF che il SIERT restituisce.
-- Stati: preparato -> caricato (sul portale) -> elaborato (report ricevuto);
-- preparato -> annullato.
CREATE TABLE norma.invii_catasto (
    invio_catasto_id    uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id          uuid        NOT NULL,
    regione             text        NOT NULL,
    sistema             text        NOT NULL,
    tipo                text        NOT NULL,
    modello_rapporto_id uuid        NOT NULL,
    versione_tracciato  text        NOT NULL,
    nome_file           text        NOT NULL,
    contenuto           text        NOT NULL,
    impronta_sha256     bytea       NOT NULL,
    stato               text        NOT NULL DEFAULT 'preparato',
    caricato_il         timestamptz,
    elaborato_il        timestamptz,
    ricevuta_documento_id uuid,
    note                text,
    creato_il           timestamptz NOT NULL DEFAULT now(),
    creato_da           uuid,
    modificato_il       timestamptz,
    modificato_da       uuid,
    versione            integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_invii_catasto PRIMARY KEY (invio_catasto_id),
    CONSTRAINT uq_invii_catasto_azienda_invio UNIQUE (azienda_id, invio_catasto_id),
    CONSTRAINT fk_invii_catasto_aziende FOREIGN KEY (azienda_id) REFERENCES sec.aziende (azienda_id),
    CONSTRAINT fk_invii_catasto_modelli FOREIGN KEY (modello_rapporto_id) REFERENCES norma.modelli_rapporto (modello_rapporto_id),
    CONSTRAINT fk_invii_catasto_ricevuta FOREIGN KEY (ricevuta_documento_id) REFERENCES documenti.documenti (documento_id),
    CONSTRAINT fk_invii_catasto_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_invii_catasto_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_invii_catasto_sistema CHECK (
        (regione = 'toscana' AND sistema = 'siert') OR (regione = 'lombardia' AND sistema = 'curit')
    ),
    CONSTRAINT ck_invii_catasto_tipo CHECK (tipo IN ('import_massivo_rcee')),
    CONSTRAINT ck_invii_catasto_nome_file CHECK (nome_file ~ '^[A-Za-z0-9_.-]+$'),
    CONSTRAINT ck_invii_catasto_impronta CHECK (octet_length(impronta_sha256) = 32),
    CONSTRAINT ck_invii_catasto_stato CHECK (stato IN ('preparato', 'caricato', 'elaborato', 'annullato')),
    CONSTRAINT ck_invii_catasto_date CHECK (
        (stato IN ('caricato', 'elaborato')) = (caricato_il IS NOT NULL) OR stato = 'annullato'
    ),
    CONSTRAINT ck_invii_catasto_versione CHECK (versione >= 1)
);

COMMENT ON TABLE norma.invii_catasto IS 'File inviati al catasto regionale: contenuto immutabile, stato e ricevuta';

-- Rapporti contenuti in un invio, con l'esito riga per riga del report SIERT:
-- importato, importato in bozza (da confermare sul CIT) o scartato con la causa.
CREATE TABLE norma.invii_rapporti (
    invio_rapporto_id     uuid        NOT NULL DEFAULT uuidv7(),
    azienda_id            uuid        NOT NULL,
    invio_catasto_id      uuid        NOT NULL,
    rapporto_controllo_id uuid        NOT NULL,
    riga                  integer     NOT NULL,
    versione_rapporto     integer     NOT NULL,
    esito                 text        NOT NULL DEFAULT 'in_attesa',
    causa                 text,
    creato_il             timestamptz NOT NULL DEFAULT now(),
    creato_da             uuid,
    modificato_il         timestamptz,
    modificato_da         uuid,
    versione              integer     NOT NULL DEFAULT 1,
    CONSTRAINT pk_invii_rapporti PRIMARY KEY (invio_rapporto_id),
    CONSTRAINT uq_invii_rapporti_riga UNIQUE (invio_catasto_id, riga),
    CONSTRAINT uq_invii_rapporti_rapporto UNIQUE (invio_catasto_id, rapporto_controllo_id),
    CONSTRAINT fk_invii_rapporti_invii FOREIGN KEY (azienda_id, invio_catasto_id) REFERENCES norma.invii_catasto (azienda_id, invio_catasto_id),
    CONSTRAINT fk_invii_rapporti_rapporti FOREIGN KEY (azienda_id, rapporto_controllo_id) REFERENCES norma.rapporti_controllo (azienda_id, rapporto_controllo_id),
    CONSTRAINT fk_invii_rapporti_creato_da FOREIGN KEY (creato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT fk_invii_rapporti_modificato_da FOREIGN KEY (modificato_da) REFERENCES sec.utenti (utente_id),
    CONSTRAINT ck_invii_rapporti_riga CHECK (riga >= 1),
    CONSTRAINT ck_invii_rapporti_esito CHECK (esito IN ('in_attesa', 'importato', 'importato_bozza', 'scartato')),
    CONSTRAINT ck_invii_rapporti_causa CHECK (esito <> 'scartato' OR nullif(btrim(causa), '') IS NOT NULL),
    CONSTRAINT ck_invii_rapporti_versione CHECK (versione >= 1)
);

-- I reinvii non creano doppioni: un rapporto sta in un solo invio vivo; dopo
-- uno scarto può entrare in un invio nuovo.
CREATE UNIQUE INDEX uq_invii_rapporti_rapporto_vivo
    ON norma.invii_rapporti (rapporto_controllo_id)
    WHERE esito <> 'scartato';

COMMENT ON TABLE norma.invii_rapporti IS 'Righe di un invio al catasto con l''esito restituito dal report';

-- ---------------------------------------------------------------------------
-- Trigger
-- ---------------------------------------------------------------------------

-- Termine di trasmissione: data del controllo più i giorni della regola
-- regionale valida a quella data. NULL se nessuna regola vale.
CREATE FUNCTION norma.imposta_termine_trasmissione()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    SELECT NEW.data_controllo + r.giorni
    INTO NEW.termine_trasmissione
    FROM norma.regole_trasmissione r
    JOIN sec.aziende a ON a.regione = r.regione
    WHERE a.azienda_id = NEW.azienda_id
      AND r.valido_dal <= NEW.data_controllo
      AND (r.valido_al IS NULL OR r.valido_al >= NEW.data_controllo);

    IF NOT FOUND THEN
        NEW.termine_trasmissione := NULL;
    END IF;

    RETURN NEW;
END
$$;

-- Coerenza e immutabilità del rapporto: il contenuto cambia solo in bozza, il
-- bollino solo prima della firma, gli stati seguono i passaggi ammessi.
CREATE FUNCTION norma.controlla_rapporto()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_categoria text;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM impianti.generatori WHERE generatore_id = NEW.generatore_id AND impianto_id = NEW.impianto_id
    ) THEN
        RAISE EXCEPTION 'il generatore non è dell''impianto del rapporto'
            USING ERRCODE = 'check_violation';
    END IF;

    IF NEW.intervento_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM lavoro.interventi WHERE intervento_id = NEW.intervento_id AND impianto_id = NEW.impianto_id
    ) THEN
        RAISE EXCEPTION 'l''intervento non è dell''impianto del rapporto'
            USING ERRCODE = 'check_violation';
    END IF;

    IF NEW.data_controllo > (now() AT TIME ZONE 'Europe/Rome')::date THEN
        RAISE EXCEPTION 'la data del controllo non può essere futura'
            USING ERRCODE = 'check_violation';
    END IF;

    IF TG_OP = 'INSERT' THEN
        IF NEW.stato <> 'bozza' OR NEW.bollino_id IS NOT NULL THEN
            RAISE EXCEPTION 'un rapporto nasce in bozza e senza bollino'
                USING ERRCODE = 'check_violation';
        END IF;

        RETURN NEW;
    END IF;

    IF NEW.azienda_id <> OLD.azienda_id THEN
        RAISE EXCEPTION 'l''azienda di un rapporto non cambia'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.stato <> 'bozza' AND (
        NEW.impianto_id, NEW.generatore_id, NEW.intervento_id, NEW.modello_rapporto_id,
        NEW.tecnico_id, NEW.data_controllo, NEW.dati
    ) IS DISTINCT FROM (
        OLD.impianto_id, OLD.generatore_id, OLD.intervento_id, OLD.modello_rapporto_id,
        OLD.tecnico_id, OLD.data_controllo, OLD.dati
    ) THEN
        RAISE EXCEPTION 'un rapporto % non si modifica: si modifica solo in bozza', OLD.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF NEW.bollino_id IS DISTINCT FROM OLD.bollino_id THEN
        IF OLD.stato NOT IN ('bozza', 'convalidato') THEN
            RAISE EXCEPTION 'il bollino di un rapporto % non cambia', OLD.stato
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;

        IF NEW.bollino_id IS NOT NULL THEN
            SELECT t.categoria INTO v_categoria
            FROM norma.bollini b
            JOIN norma.tipi_bollino t ON t.tipo_bollino_id = b.tipo_bollino_id
            WHERE b.bollino_id = NEW.bollino_id AND b.stato = 'disponibile';

            IF NOT FOUND THEN
                RAISE EXCEPTION 'il bollino non è disponibile'
                    USING ERRCODE = 'check_violation';
            END IF;

            IF v_categoria <> (SELECT categoria_bollino FROM norma.modelli_rapporto WHERE modello_rapporto_id = NEW.modello_rapporto_id) THEN
                RAISE EXCEPTION 'il bollino % non è per questo modello di rapporto', v_categoria
                    USING ERRCODE = 'check_violation';
            END IF;
        END IF;
    END IF;

    IF NEW.stato <> OLD.stato AND NOT (
        (OLD.stato = 'bozza' AND NEW.stato IN ('convalidato', 'annullato'))
        OR (OLD.stato = 'convalidato' AND NEW.stato IN ('bozza', 'firmato', 'annullato'))
        OR (OLD.stato = 'firmato' AND NEW.stato IN ('trasmesso', 'scartato', 'annullato'))
        OR (OLD.stato = 'scartato' AND NEW.stato IN ('trasmesso', 'annullato'))
    ) THEN
        RAISE EXCEPTION 'un rapporto non passa da % a %', OLD.stato, NEW.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF OLD.stato IN ('trasmesso', 'annullato') AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'un rapporto % non si modifica', OLD.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF OLD.firmato_il IS NOT NULL
       AND (NEW.firmato_il, NEW.firmato_da) IS DISTINCT FROM (OLD.firmato_il, OLD.firmato_da) THEN
        RAISE EXCEPTION 'la firma di un rapporto non cambia'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

-- Passaggi di stato del bollino, coerenti con il rapporto che lo usa.
CREATE FUNCTION norma.controlla_bollino()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.stato <> 'disponibile' THEN
            RAISE EXCEPTION 'un bollino si registra disponibile'
                USING ERRCODE = 'check_violation';
        END IF;

        RETURN NEW;
    END IF;

    IF (NEW.azienda_id, NEW.codice, NEW.tipo_bollino_id, NEW.acquistato_il)
       IS DISTINCT FROM (OLD.azienda_id, OLD.codice, OLD.tipo_bollino_id, OLD.acquistato_il)
       AND OLD.stato <> 'disponibile' THEN
        RAISE EXCEPTION 'codice, tipo e acquisto di un bollino % non cambiano', OLD.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF NEW.stato = OLD.stato THEN
        RETURN NEW;
    END IF;

    IF OLD.stato = 'disponibile' AND NEW.stato = 'assegnato' THEN
        IF NOT EXISTS (
            SELECT 1 FROM norma.rapporti_controllo
            WHERE bollino_id = NEW.bollino_id AND stato IN ('bozza', 'convalidato')
        ) THEN
            RAISE EXCEPTION 'un bollino si assegna insieme al rapporto'
                USING ERRCODE = 'check_violation';
        END IF;
    ELSIF OLD.stato = 'assegnato' AND NEW.stato = 'disponibile' THEN
        IF EXISTS (SELECT 1 FROM norma.rapporti_controllo WHERE bollino_id = NEW.bollino_id) THEN
            RAISE EXCEPTION 'il bollino è ancora sul rapporto'
                USING ERRCODE = 'check_violation';
        END IF;
    ELSIF OLD.stato = 'assegnato' AND NEW.stato = 'usato' THEN
        IF NOT EXISTS (
            SELECT 1 FROM norma.rapporti_controllo WHERE bollino_id = NEW.bollino_id AND stato = 'firmato'
        ) THEN
            RAISE EXCEPTION 'un bollino è usato quando il suo rapporto è firmato'
                USING ERRCODE = 'check_violation';
        END IF;
    ELSIF NOT (OLD.stato = 'disponibile' AND NEW.stato = 'annullato') THEN
        RAISE EXCEPTION 'un bollino non passa da % a %', OLD.stato, NEW.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    RETURN NEW;
END
$$;

-- Il file inviato è un documento: dopo la creazione cambiano solo stato,
-- date, ricevuta e note.
CREATE FUNCTION norma.controlla_invio()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.stato <> 'preparato' THEN
            RAISE EXCEPTION 'un invio nasce preparato'
                USING ERRCODE = 'check_violation';
        END IF;

        IF NEW.impronta_sha256 <> sha256(convert_to(NEW.contenuto, 'UTF8')) THEN
            RAISE EXCEPTION 'l''impronta non corrisponde al contenuto del file'
                USING ERRCODE = 'check_violation';
        END IF;

        RETURN NEW;
    END IF;

    IF (NEW.azienda_id, NEW.regione, NEW.sistema, NEW.tipo, NEW.modello_rapporto_id,
        NEW.versione_tracciato, NEW.nome_file, NEW.contenuto, NEW.impronta_sha256)
       IS DISTINCT FROM
       (OLD.azienda_id, OLD.regione, OLD.sistema, OLD.tipo, OLD.modello_rapporto_id,
        OLD.versione_tracciato, OLD.nome_file, OLD.contenuto, OLD.impronta_sha256) THEN
        RAISE EXCEPTION 'il file di un invio non si modifica'
            USING ERRCODE = 'check_violation';
    END IF;

    IF NEW.stato <> OLD.stato AND NOT (
        (OLD.stato = 'preparato' AND NEW.stato IN ('caricato', 'annullato'))
        OR (OLD.stato = 'caricato' AND NEW.stato = 'elaborato')
    ) THEN
        RAISE EXCEPTION 'un invio non passa da % a %', OLD.stato, NEW.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    RETURN NEW;
END
$$;

-- In un invio entrano solo rapporti firmati o scartati, del modello del file,
-- finché il file è preparato; dopo cambia solo l'esito.
CREATE FUNCTION norma.controlla_invio_rapporto()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NOT EXISTS (
            SELECT 1 FROM norma.invii_catasto WHERE invio_catasto_id = NEW.invio_catasto_id AND stato = 'preparato'
        ) THEN
            RAISE EXCEPTION 'si aggiungono rapporti solo a un invio preparato'
                USING ERRCODE = 'object_not_in_prerequisite_state';
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM norma.rapporti_controllo r
            JOIN norma.invii_catasto i ON i.invio_catasto_id = NEW.invio_catasto_id
            WHERE r.rapporto_controllo_id = NEW.rapporto_controllo_id
              AND r.stato IN ('firmato', 'scartato')
              AND r.modello_rapporto_id = i.modello_rapporto_id
              AND r.versione = NEW.versione_rapporto
        ) THEN
            RAISE EXCEPTION 'in un invio entrano solo rapporti firmati o scartati, del modello del file e nella versione corrente'
                USING ERRCODE = 'check_violation';
        END IF;

        IF NEW.esito <> 'in_attesa' THEN
            RAISE EXCEPTION 'una riga di invio nasce in attesa di esito'
                USING ERRCODE = 'check_violation';
        END IF;

        RETURN NEW;
    END IF;

    IF (NEW.azienda_id, NEW.invio_catasto_id, NEW.rapporto_controllo_id, NEW.riga, NEW.versione_rapporto)
       IS DISTINCT FROM (OLD.azienda_id, OLD.invio_catasto_id, OLD.rapporto_controllo_id, OLD.riga, OLD.versione_rapporto) THEN
        RAISE EXCEPTION 'di una riga di invio cambia solo l''esito'
            USING ERRCODE = 'check_violation';
    END IF;

    IF NEW.esito <> OLD.esito AND OLD.esito <> 'in_attesa' THEN
        RAISE EXCEPTION 'l''esito % di una riga di invio è definitivo', OLD.esito
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    RETURN NEW;
END
$$;

-- Audit di rapporti, bollini e invii: chi, cosa, quale stato. Mai i dati del
-- rapporto, che contengono dati personali. TG_ARGV[0] è la colonna chiave.
CREATE FUNCTION norma.registra_audit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_nuovo jsonb := to_jsonb(NEW);
    v_vecchio jsonb := CASE WHEN TG_OP = 'UPDATE' THEN to_jsonb(OLD) END;
BEGIN
    INSERT INTO sec.audit_log (azienda_id, utente_id, azione, entita, entita_id, dettaglio)
    VALUES (
        NEW.azienda_id,
        coalesce(sec.utente_corrente(), (v_nuovo ->> 'modificato_da')::uuid, (v_nuovo ->> 'creato_da')::uuid),
        TG_TABLE_NAME || '.' || lower(TG_OP),
        TG_TABLE_NAME,
        (v_nuovo ->> TG_ARGV[0])::uuid,
        jsonb_strip_nulls(jsonb_build_object(
            'stato', v_nuovo ->> 'stato',
            'stato_precedente', CASE WHEN v_vecchio ->> 'stato' IS DISTINCT FROM v_nuovo ->> 'stato' THEN v_vecchio ->> 'stato' END,
            'versione', (v_nuovo ->> 'versione')::integer
        ))
    );

    RETURN NULL;
END
$$;

CREATE TRIGGER tr_rapporti_controllo_termine
    BEFORE INSERT OR UPDATE OF data_controllo ON norma.rapporti_controllo
    FOR EACH ROW EXECUTE FUNCTION norma.imposta_termine_trasmissione();

CREATE TRIGGER tr_rapporti_controllo_controlli
    BEFORE INSERT OR UPDATE ON norma.rapporti_controllo
    FOR EACH ROW EXECUTE FUNCTION norma.controlla_rapporto();

CREATE TRIGGER tr_rapporti_controllo_metadati
    BEFORE UPDATE ON norma.rapporti_controllo
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_rapporti_controllo_audit
    AFTER INSERT OR UPDATE ON norma.rapporti_controllo
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('rapporto_controllo_id');

CREATE TRIGGER tr_bollini_controlli
    BEFORE INSERT OR UPDATE ON norma.bollini
    FOR EACH ROW EXECUTE FUNCTION norma.controlla_bollino();

CREATE TRIGGER tr_bollini_metadati
    BEFORE UPDATE ON norma.bollini
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_bollini_audit
    AFTER INSERT OR UPDATE ON norma.bollini
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('bollino_id');

CREATE TRIGGER tr_invii_catasto_controlli
    BEFORE INSERT OR UPDATE ON norma.invii_catasto
    FOR EACH ROW EXECUTE FUNCTION norma.controlla_invio();

CREATE TRIGGER tr_invii_catasto_metadati
    BEFORE UPDATE ON norma.invii_catasto
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

CREATE TRIGGER tr_invii_catasto_audit
    AFTER INSERT OR UPDATE ON norma.invii_catasto
    FOR EACH ROW EXECUTE FUNCTION norma.registra_audit('invio_catasto_id');

CREATE TRIGGER tr_invii_rapporti_controlli
    BEFORE INSERT OR UPDATE ON norma.invii_rapporti
    FOR EACH ROW EXECUTE FUNCTION norma.controlla_invio_rapporto();

CREATE TRIGGER tr_invii_rapporti_metadati
    BEFORE UPDATE ON norma.invii_rapporti
    FOR EACH ROW EXECUTE FUNCTION sec.aggiorna_metadati();

-- ---------------------------------------------------------------------------
-- Funzioni
-- ---------------------------------------------------------------------------

-- Errori che bloccherebbero l'invio, campo per campo secondo il tracciato in
-- vigore oggi, più gli avvisi non bloccanti (fascia del bollino, bollino
-- acquistato dopo il controllo) che il SIERT segnala senza rifiutare.
CREATE FUNCTION norma.valida_rapporto(p_rapporto_id uuid)
RETURNS TABLE (campo text, messaggio text, bloccante boolean)
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    v_rapporto  norma.rapporti_controllo%ROWTYPE;
    v_modello   norma.modelli_rapporto%ROWTYPE;
    v_ditta     integer;
    v_bollino   norma.bollini%ROWTYPE;
    v_tipo      norma.tipi_bollino%ROWTYPE;
    v_oggi      date := (now() AT TIME ZONE 'Europe/Rome')::date;
    v_campi     integer := 0;
    v_campo     record;
    v_valore    text;
    v_potenza   numeric;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'rapporto di controllo % non trovato', p_rapporto_id
            USING ERRCODE = 'no_data_found';
    END IF;

    SELECT * INTO v_modello FROM norma.modelli_rapporto WHERE modello_rapporto_id = v_rapporto.modello_rapporto_id;
    SELECT codice_ditta_siert INTO v_ditta FROM sec.aziende WHERE azienda_id = v_rapporto.azienda_id;

    IF v_rapporto.bollino_id IS NOT NULL THEN
        SELECT * INTO v_bollino FROM norma.bollini WHERE bollino_id = v_rapporto.bollino_id;
        SELECT * INTO v_tipo FROM norma.tipi_bollino WHERE tipo_bollino_id = v_bollino.tipo_bollino_id;
    END IF;

    IF v_rapporto.stato = 'annullato' THEN
        campo := NULL; messaggio := 'il rapporto è annullato'; bloccante := true;
        RETURN NEXT;
        RETURN;
    END IF;

    FOR v_campo IN
        SELECT t.*
        FROM norma.tracciati_campi t
        WHERE t.modello_rapporto_id = v_rapporto.modello_rapporto_id
          AND t.sistema = 'siert'
          AND t.valido_dal <= v_oggi
          AND (t.valido_al IS NULL OR t.valido_al >= v_oggi)
        ORDER BY t.posizione
    LOOP
        v_campi := v_campi + 1;
        v_valore := CASE v_campo.origine
            WHEN 'dati'     THEN nullif(btrim(v_rapporto.dati ->> v_campo.chiave), '')
            WHEN 'rapporto' THEN to_char(v_rapporto.data_controllo, 'YYYY-MM-DD')
            WHEN 'azienda'  THEN v_ditta::text
            WHEN 'bollino'  THEN v_bollino.codice
        END;

        campo := v_campo.nome;
        bloccante := true;

        IF v_valore IS NULL THEN
            IF v_campo.obbligatorio THEN
                messaggio := CASE v_campo.origine
                    WHEN 'azienda' THEN 'manca il codice ditta SIERT dell''azienda'
                    WHEN 'bollino' THEN 'nessun bollino assegnato'
                    ELSE 'campo obbligatorio'
                END;
                RETURN NEXT;
            END IF;
            CONTINUE;
        END IF;

        IF v_valore ~ '[\t\r\n]' THEN
            messaggio := 'contiene tabulazioni o a capo, che il file non ammette';
            RETURN NEXT;
            CONTINUE;
        END IF;

        IF (v_campo.tipo = 'intero' AND v_valore !~ '^-?[0-9]+$')
           OR (v_campo.tipo = 'decimale' AND v_valore !~ '^-?[0-9]+(\.[0-9]+)?$')
           OR (v_campo.tipo = 'data' AND (v_valore !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' OR NOT pg_input_is_valid(v_valore, 'date')))
           OR (v_campo.tipo = 'ora' AND v_valore !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$') THEN
            messaggio := 'valore non valido per il tipo ' || v_campo.tipo;
            RETURN NEXT;
            CONTINUE;
        END IF;

        IF v_campo.lunghezza IS NOT NULL AND char_length(v_valore) > v_campo.lunghezza THEN
            messaggio := format('al massimo %s caratteri', v_campo.lunghezza);
            RETURN NEXT;
        END IF;

        IF v_campo.valori_ammessi IS NOT NULL AND NOT (v_valore = ANY (v_campo.valori_ammessi)) THEN
            messaggio := 'valori ammessi: ' || array_to_string(v_campo.valori_ammessi, ', ');
            RETURN NEXT;
        END IF;

        IF v_campo.formato IS NOT NULL AND v_valore !~ v_campo.formato THEN
            messaggio := 'formato non valido';
            RETURN NEXT;
        END IF;
    END LOOP;

    IF v_campi = 0 THEN
        campo := NULL; messaggio := 'nessun tracciato in vigore per questo modello'; bloccante := true;
        RETURN NEXT;
    END IF;

    -- Regole condizionate della guida, solo per i gruppi termici.
    IF v_modello.codice = 'tipo_1' THEN
        IF v_rapporto.dati ->> 'combustibile' IN ('GPL', 'Gas naturale')
           AND v_rapporto.dati ->> 'evacuazione_fumi' = 'Naturale'
           AND nullif(btrim(v_rapporto.dati ->> 'depressione'), '') IS NULL THEN
            campo := 'DEPRESSIONE'; messaggio := 'obbligatorio con GPL o gas naturale e tiraggio naturale'; bloccante := true;
            RETURN NEXT;
        END IF;

        IF v_rapporto.dati ->> 'combustibile' = 'Gasolio'
           AND nullif(btrim(v_rapporto.dati ->> 'bacharach'), '') IS NULL THEN
            campo := 'BACHARAC'; messaggio := 'obbligatorio con il gasolio'; bloccante := true;
            RETURN NEXT;
        END IF;
    END IF;

    IF v_rapporto.termine_trasmissione IS NULL THEN
        campo := 'DATA_CONTROLLO'; messaggio := 'nessuna regola di trasmissione per la regione dell''azienda'; bloccante := false;
        RETURN NEXT;
    ELSIF v_rapporto.stato <> 'trasmesso' AND v_oggi > v_rapporto.termine_trasmissione THEN
        campo := 'DATA_CONTROLLO';
        messaggio := format('termine di trasmissione superato il %s', to_char(v_rapporto.termine_trasmissione, 'DD/MM/YYYY'));
        bloccante := true;
        RETURN NEXT;
    END IF;

    IF v_rapporto.bollino_id IS NOT NULL THEN
        IF v_tipo.categoria <> v_modello.categoria_bollino THEN
            campo := 'NUMERO_BOLLINO'; messaggio := 'il bollino non è della categoria del rapporto'; bloccante := true;
            RETURN NEXT;
        END IF;

        IF v_bollino.acquistato_il > v_rapporto.data_controllo THEN
            campo := 'NUMERO_BOLLINO';
            messaggio := 'bollino acquistato dopo il controllo: va rilasciato contestualmente al rapporto';
            bloccante := false;
            RETURN NEXT;
        END IF;

        IF v_tipo.grandezza = 'potenza_utile_kw' AND v_rapporto.dati ->> 'potenza_totale' ~ '^[0-9]+(\.[0-9]+)?$' THEN
            v_potenza := (v_rapporto.dati ->> 'potenza_totale')::numeric;

            IF NOT (numrange(v_tipo.fascia_da, v_tipo.fascia_a, '(]') @> v_potenza) THEN
                campo := 'NUMERO_BOLLINO';
                messaggio := format('la potenza %s kW non rientra nella fascia del bollino (%s)', v_potenza, v_tipo.nome);
                bloccante := false;
                RETURN NEXT;
            END IF;
        END IF;
    END IF;
END
$$;

COMMENT ON FUNCTION norma.valida_rapporto(uuid) IS 'Errori bloccanti e avvisi del rapporto secondo il tracciato SIERT in vigore';

-- Assegna il primo bollino disponibile del tipo scelto, il più vecchio. Con
-- SKIP LOCKED due assegnazioni contemporanee non prendono lo stesso bollino.
-- Restituisce il bollino, oppure NULL se la versione del rapporto non coincide.
CREATE FUNCTION norma.assegna_bollino(p_rapporto_id uuid, p_versione integer, p_tipo_bollino_id uuid)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
    v_bollino  uuid;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN NULL;
    END IF;

    IF v_rapporto.stato NOT IN ('bozza', 'convalidato') THEN
        RAISE EXCEPTION 'il bollino si assegna a un rapporto in bozza o convalidato, non %', v_rapporto.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    IF v_rapporto.bollino_id IS NOT NULL THEN
        RAISE EXCEPTION 'il rapporto ha già un bollino: liberalo prima di assegnarne un altro'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    SELECT bollino_id INTO v_bollino
    FROM norma.bollini
    WHERE azienda_id = v_rapporto.azienda_id
      AND tipo_bollino_id = p_tipo_bollino_id
      AND stato = 'disponibile'
    ORDER BY acquistato_il, codice
    LIMIT 1
    FOR UPDATE SKIP LOCKED;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'nessun bollino disponibile del tipo scelto'
            USING ERRCODE = 'no_data_found';
    END IF;

    UPDATE norma.rapporti_controllo
    SET bollino_id = v_bollino, modificato_da = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    UPDATE norma.bollini
    SET stato = 'assegnato', modificato_da = sec.utente_corrente()
    WHERE bollino_id = v_bollino;

    RETURN v_bollino;
END
$$;

COMMENT ON FUNCTION norma.assegna_bollino(uuid, integer, uuid) IS 'Assegna al rapporto il bollino disponibile più vecchio del tipo scelto';

-- Toglie il bollino da un rapporto non ancora firmato e lo rende disponibile.
CREATE FUNCTION norma.libera_bollino(p_rapporto_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_rapporto.bollino_id IS NULL THEN
        RAISE EXCEPTION 'il rapporto non ha un bollino'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    UPDATE norma.rapporti_controllo
    SET bollino_id = NULL, modificato_da = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    UPDATE norma.bollini
    SET stato = 'disponibile', modificato_da = sec.utente_corrente()
    WHERE bollino_id = v_rapporto.bollino_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.libera_bollino(uuid, integer) IS 'Rende disponibile il bollino di un rapporto non firmato';

-- Elenco leggibile degli errori bloccanti, per i messaggi delle eccezioni.
CREATE FUNCTION norma.errori_bloccanti(p_rapporto_id uuid)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT string_agg(coalesce(v.campo || ': ', '') || v.messaggio, '; ')
    FROM norma.valida_rapporto(p_rapporto_id) v
    WHERE v.bloccante
$$;

-- Bozza -> convalidato, solo senza errori bloccanti.
CREATE FUNCTION norma.convalida_rapporto(p_rapporto_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
    v_errori   text;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_rapporto.stato <> 'bozza' THEN
        RAISE EXCEPTION 'si convalida solo una bozza, non un rapporto %', v_rapporto.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    v_errori := norma.errori_bloccanti(p_rapporto_id);

    IF v_errori IS NOT NULL THEN
        RAISE EXCEPTION 'il rapporto ha errori bloccanti: %', v_errori
            USING ERRCODE = 'check_violation';
    END IF;

    UPDATE norma.rapporti_controllo
    SET stato = 'convalidato', convalidato_il = now(), convalidato_da = sec.utente_corrente(),
        modificato_da = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.convalida_rapporto(uuid, integer) IS 'Convalida una bozza senza errori bloccanti';

-- Convalidato -> bozza, per correggere prima della firma.
CREATE FUNCTION norma.riapri_rapporto(p_rapporto_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_rapporto.stato <> 'convalidato' THEN
        RAISE EXCEPTION 'si riapre solo un rapporto convalidato, non %', v_rapporto.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    UPDATE norma.rapporti_controllo
    SET stato = 'bozza', convalidato_il = NULL, convalidato_da = NULL, modificato_da = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.riapri_rapporto(uuid, integer) IS 'Riporta in bozza un rapporto convalidato';

-- Convalidato -> firmato: registra che la copia cartacea è firmata, blocca i
-- dati e segna il bollino come usato. La firma resta esterna al sistema.
CREATE FUNCTION norma.firma_rapporto(p_rapporto_id uuid, p_versione integer)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
    v_errori   text;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_rapporto.stato <> 'convalidato' THEN
        RAISE EXCEPTION 'si firma solo un rapporto convalidato, non %', v_rapporto.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    v_errori := norma.errori_bloccanti(p_rapporto_id);

    IF v_errori IS NOT NULL THEN
        RAISE EXCEPTION 'il rapporto ha errori bloccanti: %', v_errori
            USING ERRCODE = 'check_violation';
    END IF;

    UPDATE norma.rapporti_controllo
    SET stato = 'firmato', firmato_il = now(), firmato_da = sec.utente_corrente(),
        modificato_da = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    UPDATE norma.bollini
    SET stato = 'usato', modificato_da = sec.utente_corrente()
    WHERE bollino_id = v_rapporto.bollino_id;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.firma_rapporto(uuid, integer) IS 'Segna come firmato un rapporto convalidato e usa il suo bollino';

-- Annulla un rapporto non trasmesso. Prima della firma il bollino torna
-- disponibile; dopo resta usato, perché è stato applicato sulla copia cartacea.
CREATE FUNCTION norma.annulla_rapporto(p_rapporto_id uuid, p_versione integer, p_motivo text)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    v_rapporto norma.rapporti_controllo%ROWTYPE;
BEGIN
    SELECT * INTO v_rapporto FROM norma.rapporti_controllo WHERE rapporto_controllo_id = p_rapporto_id FOR UPDATE;

    IF NOT FOUND OR v_rapporto.versione <> p_versione THEN
        RETURN false;
    END IF;

    IF v_rapporto.stato IN ('trasmesso', 'annullato') THEN
        RAISE EXCEPTION 'un rapporto % non si annulla', v_rapporto.stato
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    UPDATE norma.rapporti_controllo
    SET stato               = 'annullato',
        bollino_id          = CASE WHEN v_rapporto.stato IN ('bozza', 'convalidato') THEN NULL ELSE bollino_id END,
        annullato_il        = now(),
        annullato_da        = sec.utente_corrente(),
        motivo_annullamento = p_motivo,
        modificato_da       = sec.utente_corrente()
    WHERE rapporto_controllo_id = p_rapporto_id;

    IF v_rapporto.stato IN ('bozza', 'convalidato') AND v_rapporto.bollino_id IS NOT NULL THEN
        UPDATE norma.bollini
        SET stato = 'disponibile', modificato_da = sec.utente_corrente()
        WHERE bollino_id = v_rapporto.bollino_id;
    END IF;

    RETURN true;
END
$$;

COMMENT ON FUNCTION norma.annulla_rapporto(uuid, integer, text) IS 'Annulla un rapporto non trasmesso; prima della firma libera il bollino';

-- ---------------------------------------------------------------------------
-- Row-Level Security e privilegi
-- ---------------------------------------------------------------------------

ALTER TABLE norma.bollini            ENABLE ROW LEVEL SECURITY;
ALTER TABLE norma.rapporti_controllo ENABLE ROW LEVEL SECURITY;
ALTER TABLE norma.invii_catasto      ENABLE ROW LEVEL SECURITY;
ALTER TABLE norma.invii_rapporti     ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_bollini ON norma.bollini TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_rapporti_controllo ON norma.rapporti_controllo TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_invii_catasto ON norma.invii_catasto TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

CREATE POLICY rls_invii_rapporti ON norma.invii_rapporti TO app_api
    USING (azienda_id = sec.azienda_corrente())
    WITH CHECK (azienda_id = sec.azienda_corrente());

REVOKE ALL ON ALL TABLES IN SCHEMA norma FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA norma FROM PUBLIC;

-- Niente DELETE: rapporti, bollini e invii si annullano.
GRANT SELECT                 ON norma.modelli_rapporto    TO app_api;
GRANT SELECT                 ON norma.regole_trasmissione TO app_api;
GRANT SELECT                 ON norma.tipi_bollino        TO app_api;
GRANT SELECT                 ON norma.tracciati_campi     TO app_api;
GRANT SELECT, INSERT, UPDATE ON norma.bollini             TO app_api;
GRANT SELECT, INSERT, UPDATE ON norma.rapporti_controllo  TO app_api;
GRANT SELECT, INSERT, UPDATE ON norma.invii_catasto       TO app_api;
GRANT SELECT, INSERT, UPDATE ON norma.invii_rapporti      TO app_api;

GRANT EXECUTE ON FUNCTION
    norma.valida_rapporto(uuid),
    norma.errori_bloccanti(uuid),
    norma.assegna_bollino(uuid, integer, uuid),
    norma.libera_bollino(uuid, integer),
    norma.convalida_rapporto(uuid, integer),
    norma.riapri_rapporto(uuid, integer),
    norma.firma_rapporto(uuid, integer),
    norma.annulla_rapporto(uuid, integer, text)
TO app_api;

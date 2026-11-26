# ADR 0006 – Rapporti di controllo e tracciato SIERT come dati

- **Stato:** accettato
- **Data:** 26 novembre 2026
- **Riguarda:** M4, compliance Toscana (migrazione 0010)

## Contesto

Il criterio di uscita di M4 chiede un rapporto di controllo completo, con bollino e file di invio, generato e validato contro la specifica SIERT. Le fonti lette il 26/11/2026:

| Fonte | Cosa dice |
| --- | --- |
| SIERT, [Funzione import massivo RCEE](https://siert.regione.toscana.it/upload/Istruzioni.pdf), guida operativa v3.0 | File txt UTF-8 con valori separati da tabulazioni, una riga per RCEE; solo RCEE tipo 1 (gruppi termici) e tipo 2 (gruppi frigo); tabella dei campi con tipo, obbligatorietà e codifiche; il SIERT elabora il file e restituisce un report PDF con l'esito riga per riga |
| SIERT, [Manuale utente impresa di manutenzione](https://siert.regione.toscana.it/documenti/Manuale_manutentori_20260914.pdf), Rev. 21 del 14/09/2026 | Trasmissione entro 60 giorni dalla redazione; bollino di 13 cifre (le prime 4 sono l'anno), senza scadenza, da rilasciare contestualmente al RCEE (art. 19 c. 8 DPGR 17/R/2023); tipi e importi per categoria e fascia di potenza (LR 85/2016 art. 22 c. 4) |

Il regolamento in vigore è il DPGR 6 aprile 2023 n. 17/R: il piano citava ancora il 25/R/2015.

Nella guida i campi sono numerati da 1 a 68, ma il 46 manca in entrambe le tabelle.

## Decisione

- **Perimetro di M4:** RCEE tipo 1 e 2, registro dei bollini, file di import massivo. Dichiarazione di conformità e registro F-Gas passano a una milestone successiva (decisione dell'utente del 26/11/2026).
- **Il tracciato è un dato:** `norma.tracciati_campi` ha una riga per colonna del file, con posizione, numero della guida, tipo, lunghezza, obbligatorietà, valori ammessi, formato e origine del valore. Una nuova versione della guida è una nuova serie di righe con `valido_dal`, senza toccare il codice.
- **67 colonne:** finché il team SIERT non chiarisce, il file segue le righe della tabella così come sono, senza una colonna vuota al posto del 46.
- **Il RCEE conserva tutto:** `rapporti_controllo.dati` (JSONB) contiene ogni valore stampato sulla copia firmata, già nella codifica del tracciato (SI=3, NO=4, NC=9 e così via). Il file si genera solo da lì, più il codice ditta SIERT dell'azienda e il codice del bollino. Così la copia digitale coincide con quella cartacea anche se l'anagrafica cambia dopo la firma, come chiede la guida.
- **La validazione sta nel database:** `norma.valida_rapporto` legge il tracciato in vigore e restituisce errori bloccanti e avvisi; convalida e firma rifiutano un rapporto con errori bloccanti. Gli avvisi sono quelli che il SIERT segnala senza rifiutare: potenza fuori dalla fascia del bollino, bollino acquistato dopo il controllo.
- **Il bollino lo sceglie chi compila:** `norma.assegna_bollino` riceve il tipo e prende il bollino disponibile più vecchio con `FOR UPDATE SKIP LOCKED`. La categoria deve essere quella del modello (GT per il tipo 1, GF per il tipo 2); la fascia di potenza dà solo un avviso, perché la guida non dice quale potenza confronta né a quale fascia appartengono gli estremi.
- **Tipi e importi dei bollini** sono caricati come dati verificati, con fonte il manuale Rev. 21, validi dal 14/09/2026.
- **Stati:** bozza ⇄ convalidato → firmato → trasmesso o scartato; scartato → trasmesso dopo un nuovo invio; annullato da ogni stato tranne trasmesso. La firma resta esterna: lo stato firmato registra che la copia cartacea è firmata e blocca i dati. Il bollino passa a usato con la firma e non torna più disponibile.
- **Invii:** `norma.invii_catasto` conserva il file caricato (contenuto e SHA-256, immutabili) e la ricevuta; `norma.invii_rapporti` l'esito di ogni riga. Un rapporto sta in un solo invio vivo: dopo uno scarto può entrare in un invio nuovo.

## Domande aperte, da porre al team SIERT

- Il campo 46 è un refuso della numerazione o una colonna omessa?
- Il file ha una riga di intestazione? Per ora no.
- Le righe vanno chiuse da CRLF o da LF? Per ora CRLF, in UTF-8 senza BOM: la guida dice solo "UTF8".
- Il valore ammesso "AD ASSORBIMENTO A FIAMMA DIRETTA CON COMNBUSTIBLE" (tipo 2, campo 32) è scritto così anche nel sistema?
- Quale potenza si confronta con la fascia del bollino, e gli estremi ("fino a 35 kW", "tra 35 kW e 100 kW") a quale fascia appartengono?

## Conseguenze

- Il file di invio si può validare riga per riga prima di caricarlo, con gli stessi controlli di struttura della guida.
- Se la risposta del SIERT cambia il tracciato, basta una migrazione con le nuove righe.
- La migrazione 0011 aggiunge la bozza precompilata dalle anagrafiche (`norma.crea_rapporto`, senza trasformare i valori), gli esiti del report riportati sui rapporti (importato e importato in bozza: trasmesso; scartato: scartato) e l'annullamento di un invio mai caricato.
- Annullare un rapporto firmato richiede il permesso di firma: è un documento con valore legale.
- Rimandati: bollino congiunto e generatori modulari (suffissi C1, M1 sul numero di bollino), bozza creata alla chiusura dell'intervento, PDF del rapporto.
- Il SIERT non ha un'API pubblica nota: l'invio resta assistito, l'utente carica il file sul portale e poi registra il report.

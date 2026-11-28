# ADR 0008 – Registro F-Gas, controlli delle perdite e Banca dati FGAS

- **Stato:** accettato
- **Data:** 28 novembre 2026
- **Riguarda:** M4b (ADR 0007), migrazione 0015

## Contesto

Fonti lette il 28/11/2026:

| Fonte | Cosa dice |
| --- | --- |
| [Reg. (UE) 2024/573](https://eur-lex.europa.eu/legal-content/IT/TXT/HTML/?uri=CELEX:32024R0573), art. 5 | Controllo delle perdite per apparecchiature fisse di refrigerazione, condizionamento, pompe di calore e altre, con almeno 5 t di CO2 equivalente di gas dell'allegato I o almeno 1 kg di gas dell'allegato II, sezione 1. Frequenza: sotto 50 t (o 10 kg) almeno ogni 12 mesi, ogni 24 con un sistema di rilevamento delle perdite; da 50 a 500 t (da 10 a 100 kg) ogni 6 mesi, ogni 12 con il sistema; da 500 t (100 kg) in su ogni 3 mesi, ogni 6 con il sistema. Esenti le apparecchiature ermeticamente sigillate ed etichettate sotto 10 t (allegato I) o sotto 2 kg (allegato II); negli edifici residenziali, sotto 3 kg |
| Stesso regolamento, art. 4, par. 5 | Una perdita si ripara senza indebito ritardo; dopo la riparazione l'apparecchiatura si ricontrolla non prima di 24 ore di funzionamento e comunque entro un mese |
| Stesso regolamento, art. 6 | Sistema di rilevamento obbligatorio da 500 t di CO2 equivalente (100 kg per l'allegato II), da controllare ogni 12 mesi |
| Stesso regolamento, art. 7 | Registro per ogni apparecchiatura soggetta: gas e quantità (anche quella aggiunta all'installazione); gas aggiunto con la data; gas recuperato; gas riciclato o rigenerato con l'impianto e il suo certificato; impresa e persona con il numero di certificato; date ed esiti di controlli e riparazioni; misure di recupero allo smantellamento. Conservazione per almeno cinque anni |
| Stesso regolamento, art. 3 e allegati I, II, VI | CO2 equivalente = tonnellate di gas × GWP a 100 anni. GWP dei gas puri negli allegati (HFC-32 675, HFC-125 3 500, HFC-134a 1 430, HFC-143a 4 470, HFC-1234yf 0,501, HFC-1234ze 1,37); per le miscele, media pesata (allegato VI). Le composizioni delle miscele commerciali non ci sono |
| [DPR 146/2018](https://www.normattiva.it/uri-res/N2Ls?urn:nir:stato:decreto.del.presidente.della.repubblica:2018-11-16;146~art16), art. 16 | L'impresa certificata comunica alla Banca dati FGAS ogni installazione, controllo delle perdite, manutenzione, riparazione e smantellamento entro 30 giorni, con i dati dei commi 4, 5 e 7 |
| [Chiarimento del MASE del 27/02/2025](https://www.fgas.it/News) | La comunicazione vale per ogni intervento, qualunque sia la quantità di gas; la soglia di 5 t riguarda solo la frequenza dei controlli periodici |

Non risulta, al 28/11/2026, un decreto italiano di attuazione del Reg. 2024/573 che sostituisca il DPR 146/2018, né un'API pubblica della Banca dati.

## Decisione

Scelte dell'utente del 28/11/2026:

1. **Controlli delle perdite come scadenze.** Nuovo tipo di scadenza `controllo_perdite`, calcolato dal database. Le scadenze sono per impianto: la data è la più vicina tra le apparecchiature soggette dell'impianto (ultimo controllo più la frequenza, oppure subito se non è mai stato controllato), e un controllo dopo una riparazione la anticipa a un mese. Così entrano nella lista da richiamare, nella pagina Oggi e nei promemoria.
2. **Banca dati FGAS assistita.** Per ogni intervento F-Gas il programma prepara i dati dell'art. 16 e mostra il termine dei 30 giorni; chi li inserisce sul portale segna l'intervento come comunicato, e da allora non si modifica.
3. **Gas come dati.** I gas puri si caricano dal regolamento come voci verificate. Le miscele comuni (R410A, R407C, R404A) hanno la composizione della norma ISO 817, che non abbiamo letto: restano `da_verificare` e il GWP si calcola dalla composizione con l'allegato VI.
4. **Certificati.** Numero, ente e scadenza del certificato F-Gas dell'impresa e di ogni tecnico. Un tecnico senza certificato in corso non si assegna a una manutenzione, riparazione o installazione di un impianto con apparecchiature F-Gas, e non registra un intervento F-Gas.

Le soglie, le frequenze e le esenzioni sono dati con `valido_dal` e la fonte, come cadenze e bollini.

## Conseguenze

- Ogni operazione F-Gas appartiene a un intervento del gestionale: chiude la scadenza del controllo con quell'intervento e ne ricava la successiva.
- I numeri di certificato si copiano sull'operazione al momento della registrazione: un rinnovo successivo non cambia lo storico.
- Il registro si conserva per sempre come gli altri documenti normativi; il minimo di legge è cinque anni.
- Da verificare: le composizioni delle miscele (ISO 817); se la Banca dati accetta importazioni in blocco; un eventuale decreto italiano che sostituisca il DPR 146/2018.

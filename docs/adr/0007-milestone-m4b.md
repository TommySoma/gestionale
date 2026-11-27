# ADR 0007 – Milestone M4b prima del chatbot

- **Stato:** accettato
- **Data:** 27 novembre 2026
- **Riguarda:** piano di sviluppo, perimetro dell'MVP

## Contesto

Il piano mette nell'MVP dodici aree. Al termine di M4 quattro di queste non avevano una milestone:

- **Registro F-Gas e dichiarazione di conformità.** Erano in M4 e il 26/11/2026 sono passate "a una milestone successiva" (ADR 0006), senza dire quale.
- **Importazione da Excel/CSV di clienti e impianti, promemoria email con le scadenze.** Non erano mai state assegnate a una milestone.

M5 (chatbot base) e M6 (rilascio dell'MVP) non le comprendono. Senza una decisione, l'MVP sarebbe uscito senza due aree normative e senza due funzioni che servono a chi inizia a usarlo con dati veri.

## Decisione

Decisione del 27/11/2026: le quattro funzioni formano la milestone **M4b**, prima di M5. M5 e M6 restano con lo stesso numero e lo stesso contenuto, così piano, ADR e CLAUDE.md non vanno rinumerati.

Ordine dentro M4b:

1. **Importazione** di clienti, sedi e impianti da CSV. L'Excel viene dopo una scelta della libreria con controllo della licenza: le candidate sono ClosedXML ed ExcelDataReader, entrambe MIT, da verificare prima di aggiungerle. Prima di scrivere qualcosa l'importazione mostra un'anteprima con gli errori riga per riga; i doppioni si riconoscono con le regole già presenti (partita IVA, PDR, indirizzo).
2. **Promemoria email** delle scadenze, con SMTP configurato dal Titolare e credenziali cifrate (`parametri_azienda`). Si inviano solo ai clienti con il consenso `promemoria_scadenze` in corso. Ogni invio resta registrato, legato alla scadenza come un richiamo.
3. **Registro F-Gas.** Prima di scrivere lo schema vanno verificati sui testi ufficiali frequenze e soglie dei controlli perdite (Reg. UE 2024/573, DPR 146/2018) e i dati per la banca dati F-Gas.
4. **Dichiarazione di conformità.** Prima di scrivere lo schema vanno verificati gli allegati obbligatori (DM 37/08). La bozza è guidata, la numerazione è per azienda e anno, e dopo la firma la dichiarazione non si modifica; la firma resta esterna.

Importazione e promemoria vengono prima perché non dipendono da regole normative da verificare: intanto si leggono i testi per F-Gas e dichiarazione di conformità.

## Conseguenze

- L'MVP passa da circa 20 a circa 24 settimane stimate; le date del piano slittano di quattro settimane da M5 in poi.
- Il chatbot arriva dopo: può già interrogare anche F-Gas, dichiarazioni e promemoria, con le sue funzioni predefinite.
- Le regole normative di F-Gas e dichiarazione di conformità saranno dati con `valido_dal` e `valido_al`, come cadenze e bollini. Finché non sono verificate restano voci `da_verificare`, mai valori inventati.

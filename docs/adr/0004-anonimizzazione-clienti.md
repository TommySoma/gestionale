# ADR 0004 – Anonimizzazione dei clienti (GDPR)

- **Stato:** accettato
- **Data:** 23 novembre 2026
- **Riguarda:** funzione `anonimizza_cliente(cliente)` del piano, diritto alla cancellazione

## Contesto

L'impresa che usa il gestionale è titolare del trattamento dei dati dei propri clienti. Quando un interessato chiede la cancellazione, i suoi dati personali vanno tolti. Alcune informazioni però servono ai documenti con valore legale che arriveranno con M2-M4 (impianti, rapporti di controllo, dichiarazioni di conformità), e lo storico dei consensi dimostra come è stato gestito il trattamento. La cancellazione fisica romperebbe i riferimenti e il registro delle attività.

## Decisione

Il cliente non si cancella: si **anonimizza** con la funzione SQL `anagrafica.anonimizza_cliente(cliente, versione)` (migrazione 0004), in una sola transazione e con controllo di versione.

| Dato | Cosa succede |
| --- | --- |
| Nominativo, codice fiscale, partita IVA, PEC, note del cliente | Messi a NULL; resta il tipo (privato, condominio, azienda) |
| Contatti | Cancellati |
| Sedi | Restano indirizzo e coordinate, che descrivono dove si trova l'impianto; descrizione e note di accesso a NULL |
| Storico dei consensi | Resta (finalità, esito, fonte, data); le note, testo libero, a NULL |
| Audit | Una riga `cliente.anonimizzato` con soli conteggi, nessun dato personale |

- Il cliente anonimizzato è anche eliminato dagli elenchi. Un trigger impedisce ogni modifica successiva: l'operazione non si annulla.
- Lo storico dei consensi resta immutabile. Il trigger ammette un solo cambiamento, la cancellazione delle note, e `app_api` ha il privilegio `UPDATE` solo su quella colonna.
- La funzione gira con i privilegi di `app_api` (nessun `SECURITY DEFINER`), quindi sotto Row-Level Security: non può toccare clienti di altre aziende.
- Nuovo permesso `clienti.anonimizzazione`, assegnato solo al **Titolare**. L'Amministrativo modifica ed elimina i clienti, ma non compie un'azione irreversibile.
- API: `POST /api/clienti/{id}/anonimizzazione?versione=N`. Nel client, il pulsante "Anonimizza" si attiva solo dopo aver scritto la parola di conferma.

## Motivi

- L'indirizzo di una sede identifica un edificio e il suo impianto, non la persona, una volta tolti nome e recapiti. Il catasto regionale e i documenti degli impianti ne hanno bisogno anche dopo il cambio di proprietario.
- Tenere la riga del cliente con l'identificativo conserva l'integrità referenziale verso documenti e audit, senza dati personali.

## Limiti e lavoro futuro

- **Da verificare con chi conosce la normativa:** quali dati i documenti con valore legale devono conservare e per quanto tempo (obblighi fiscali, catasto, F-Gas). Quando arriveranno impianti e documenti (M2-M4), la funzione va estesa: i documenti conservano la propria copia dei dati e non vengono toccati.
- Un cliente già eliminato si può anonimizzare dalla funzione SQL, ma l'API e il client per ora lo trattano come non trovato: servirà una vista "clienti eliminati".
- La riautenticazione per le azioni critiche prevista dal piano non è ancora implementata; quando arriverà, l'anonimizzazione dovrà richiederla.
- L'esportazione dei dati dell'interessato (diritto di accesso e portabilità) è un lavoro separato.
- Le copie di backup contengono ancora i dati fino alla loro scadenza: va scritto nella documentazione sulla conservazione.

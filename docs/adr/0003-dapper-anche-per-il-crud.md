# ADR 0003 – Dapper anche per il CRUD semplice

- **Stato:** accettato
- **Data:** 22 novembre 2026
- **Sostituisce:** "EF Core per il CRUD semplice" nella riga "Accesso ai dati" del piano

## Contesto

Il piano prevedeva Dapper per funzioni e report ed EF Core per il CRUD semplice. Con M1 l'accesso ai dati è già scritto con Dapper: ogni unità di lavoro apre una transazione e imposta azienda e utente con `set_config(..., true)`, cioè `SET LOCAL`, su cui si basa la Row-Level Security. Le anagrafiche (clienti, sedi, contatti, consensi) sono il primo vero CRUD.

## Decisione

Anche il CRUD usa Npgsql e Dapper, con SQL esplicito, tramite la stessa classe `Database` che gestisce transazione e contesto RLS. EF Core non viene aggiunto.

## Motivi

- Un solo modo di accedere ai dati: un solo punto in cui si imposta il contesto RLS, nessun interceptor da tenere allineato.
- Le migrazioni sono già script SQL scritti a mano (DbUp): con EF Core il modello andrebbe mantenuto due volte.
- La concorrenza ottimistica (`WHERE versione = @versione`) e le colonne calcolate restano visibili nell'SQL.

## Conseguenze

- Un po' più di SQL da scrivere per le operazioni ripetitive; si compensa con query piccole e test di integrazione su PostgreSQL reale.
- Se in futuro il CRUD crescesse molto, si potrà rivalutare EF Core con un nuovo ADR.

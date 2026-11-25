# Gestionale impianti termici e clima

Gestionale desktop on-premise (Windows e macOS) per piccole imprese che installano e manutengono caldaie, climatizzatori e pompe di calore. Trasforma gli obblighi normativi in scadenze e documenti già pronti. Progetto da portfolio, pubblico su GitHub e LinkedIn, con licenza MIT.

Il piano completo è in `docs/piano-di-sviluppo.md`: leggilo prima di lavorare su una nuova area. Questo file riassume solo ciò che serve in ogni sessione.

## Stato

M0 completata e unita il 22/11/2026. M1 completata e unita il 23/11/2026: autenticazione (ADR 0002), anagrafiche (ADR 0003), client desktop, anonimizzazione GDPR (ADR 0004) e tema scuro. M2 (impianti e interventi) in corso: schema impianti unito; seconda PR con API impianti e scheda impianto nel client (il Tecnico non vede impianti finché non arrivano gli interventi); le lookup normative (tipi di impianto, di generatore, combustibili) hanno solo voci di prova `da_verificare`, da sostituire con i valori dei moduli ufficiali. Terza PR: schema interventi (migrazione 0006: `commerciale.contatori` e `prossimo_numero`, `lavoro.interventi` con vincolo di esclusione GiST, `lavoro.interventi_righe`, `lavoro.chiudi_intervento`). Quarta PR: API interventi (`/api/interventi`: elenco e calendario, scheda, pianificazione, avvio, chiusura con righe, annullamento); il Tecnico vede, avvia e chiude solo i propri, e vede e aggiorna gli impianti dei propri interventi (non ne crea, non li elimina). Quinta PR: calendario nel client (settimana e giorno per tecnico, da pianificare), scheda intervento con avvio, chiusura con righe e annullamento, storico nella scheda impianto. Sesta PR: trascinamento nel calendario (sposta giorno e tecnico tenendo orario e durata; da pianificare apre il dialogo). Settima PR: rapportino PDF generato dall'API con SkiaSharp e il carattere Inter incorporato (ADR 0005), scaricabile dalla scheda di un intervento completato. Ottava PR: foto e documenti degli impianti, schema e API (migrazione 0007, JPEG/PNG/PDF fino a 20 MB, tipo dai primi byte, file su disco rinominato, eliminazione logica; il Tecnico carica sui propri impianti ma non elimina); nona PR: nella scheda impianto la sezione Foto e documenti (carica più file, apri con il programma predefinito, salva copia, elimina per l'ufficio). M3 (scadenze e preventivi) iniziata il 25/11/2026, impilata sulle PR di M2 ancora aperte: prima PR con lo schema delle scadenze (migrazione 0008: schema `norma` con `regole_cadenza` solo di prova `da_verificare`, `impianti.manutenzione_mesi`, `interventi.controllo_efficienza`, `lavoro.scadenze`, `lavoro.richiami`, `ricalcola_scadenze`, `scadenze_in_arrivo`, `chiudi_intervento` che chiude le scadenze); seconda PR con l'API (`/api/scadenze`: lista in arrivo con le scadute, scheda con i richiami, correzione della data, registrazione dei richiami; `/api/impianti/{id}/scadenze`; `manutenzioneMesi` sugli impianti e `controlloEfficienza` in chiusura; il Tecnico legge solo le proprie, senza telefono né richiami); poi client delle scadenze, quindi listino e preventivi. Proposta grafica: https://claude.ai/artifact/VU9RMVAS2a8shTESLADN5v Chi sviluppa lavora su Windows 11; la verifica su macOS passa dai runner di GitHub Actions.

## Decisioni già prese (non riaprirle senza un motivo)

- **Settore:** manutenzione e installazione di impianti termici e climatizzazione. La manutenzione è il motore; installazione, interventi e preventivi lo alimentano.
- **Regioni:** Toscana per prima (catasto SIERT), Lombardia seconda (CURIT). Un'interfaccia comune e un adattatore per regione.
- **Stack:** Avalonia 12 con MVVM (CommunityToolkit.Mvvm); ASP.NET Core su .NET 10 LTS con minimal API; Npgsql + Dapper per tutto l'accesso ai dati, CRUD compreso (ADR 0003); PostgreSQL 18 con pgvector; migrazioni DbUp (script SQL numerati); xUnit, Testcontainers, pgTAP; Docker Compose con Caddy; Ollama per l'AI locale; Velopack per installer; Serilog e OpenTelemetry.
- **Il client desktop non parla mai direttamente con il database:** passa sempre dall'API.
- **Logica transazionale in funzioni SQL**, non in procedure (le procedure solo se serve un COMMIT interno). Validazioni e orchestrazione stanno nell'API.
- **Licenza MIT.** Se un giorno si vende, si ristruttura: non preoccuparti di questo ora.
- **Dipendenze:** controlla la licenza prima di aggiungerne una. Evita librerie passate a licenza commerciale (ad esempio MediatR e AutoMapper). PDF con SkiaSharp (MIT), non QuestPDF né PDFsharp: vedi ADR 0005.
- **Dati di test sempre fittizi.** Mai dati reali di clienti nel repository.
- **Ancora aperto:** PWA di Fase 2 (Blazor WebAssembly oppure React con TypeScript), da decidere a M7 con un ADR. Convenzione dei nomi nel codice C#: proposta di usare le entità di dominio in italiano come le tabelle (Impianto, Intervento, RapportoControllo) e i suffissi tecnici in inglese; da confermare in M0.

## Struttura del repository (proposta, da creare in M0)

```
/src        Domain, Application, Infrastructure, Api, Desktop
/db         migrations (script DbUp numerati), tests (pgTAP)
/deploy     docker-compose.yml, Caddyfile
/docs       piano-di-sviluppo.md, adr/
/tests      unit, integration (Testcontainers), e2e
global.json blocca l'SDK .NET 10
```

## Ruoli

Titolare, Amministrativo, Responsabile tecnico, Tecnico, Sola lettura, Amministratore di sistema. L'amministratore di sistema non vede i dati di business. La matrice completa dei permessi è nel piano. Le restrizioni "solo propri" le applica l'API; la RLS per azienda è la rete di sicurezza sotto.

## Sicurezza (regole che non si discutono)

- Password con Argon2id, minimo 12 caratteri; TOTP obbligatorio per Titolare e Amministratore di sistema.
- Token di accesso JWT a 15 minuti con token di rinnovo a rotazione e revoca lato server; sul computer solo nel portachiavi di sistema (Windows Credential Manager, macOS Keychain).
- Row-Level Security su ogni tabella con `azienda_id`: l'API esegue `SET LOCAL app.azienda_id` a inizio transazione (attenzione al pooling di Npgsql). Tre ruoli del database: `app_owner`, `app_api` (senza `BYPASSRLS`), `app_chatbot` (sola lettura su viste `v_chatbot_*`).
- Audit append-only: all'utente dell'API sono tolti UPDATE e DELETE su `audit_log`.
- Nessun segreto nel repository (gitleaks in CI). Upload di file solo con tipi ammessi, limite di dimensione e rinomina.
- Chatbot: elenco chiuso di funzioni, conferma esplicita per ogni modifica, il contenuto dei documenti caricati è dato e mai istruzione. Eredita i permessi dell'utente.

## Convenzioni del database

- `snake_case` in italiano, tabelle al plurale, chiavi `<entità>_id`, vincoli con prefisso `pk_`, `fk_`, `uq_`, `ck_` (`ex_` per i vincoli di esclusione).
- Chiavi UUID v7 (`uuidv7()`, PostgreSQL 18), `timestamptz`, importi `numeric(12,2)`. Niente ENUM nativi: lookup per ciò che è normativo, `CHECK` per gli stati interni.
- Ogni tabella di business ha `azienda_id`, `creato_il`, `creato_da`, `modificato_il`, `modificato_da`, `versione`.
- I documenti normativi non si cancellano: si annullano con uno stato. Dopo la firma un trigger ne blocca la modifica.
- Le regole normative (cadenze, tipi e importi dei bollini, modelli) sono dati con `valido_dal` e `valido_al`, non codice.
- Ogni funzione SQL ha test pgTAP. Mai modificare una migrazione già applicata: se ne aggiunge una nuova.
- Con PostgreSQL 18 il volume Docker si monta su `/var/lib/postgresql`, non su `/var/lib/postgresql/data`.

## Normativa: cosa NON è verificato

Non inventare campi o regole. Prima di implementare verifica sui testi ufficiali:

- tracciato di invio SIERT (import massivo dei rapporti di controllo) e campi dei rapporti, nella versione corrente;
- tipi e importi dei bollini regionali;
- allegati obbligatori della dichiarazione di conformità (DM 37/08);
- frequenze e soglie dei controlli perdite F-Gas (Reg. UE 2024/573 e DPR 146/2018);
- non è noto se SIERT o CURIT abbiano API pubbliche: per ora l'invio è assistito (file o dati precompilati).

Il software è un supporto alla compilazione: la firma e la responsabilità restano all'utente. Il disclaimer va nel README e in ogni documento generato.

## Come lavorare

- Procedi per milestone (M0-M6, poi Fase 2 e 3), una alla volta, rispettando i criteri di uscita del piano.
- Se cambia una decisione, aggiorna il piano e scrivi un ADR in `docs/adr/`.
- Prima di scrivere codice di dominio, controlla lo schema del database nel piano.
- Commit piccoli, pull request, CI verde. Nessun merge con test rossi.
- Interfaccia e messaggi in italiano, con i testi in file di traduzione.
- Dove sei incerto su una regola normativa o sul comportamento atteso, chiedi invece di assumere.

## Comandi

```
dotnet build
dotnet test                                                   # tutti i test, serve Docker (Testcontainers + pgTAP)
dotnet test --filter-not-trait "Category=Integration"         # solo unitari, senza Docker
dotnet format whitespace --verify-no-changes
docker compose -f deploy/docker-compose.yml up -d db          # richiede deploy/.env (copia di .env.example)
dotnet run --project src/Gestionale.Migrator -- "<stringa di connessione come app_owner>"
```

- `global.json` attiva Microsoft.Testing.Platform: niente VSTest, niente `Microsoft.NET.Test.Sdk`.
- Versioni dei pacchetti centralizzate in `Directory.Packages.props`; warning trattati come errori.
- Test pgTAP in `db/tests/*.sql`: ognuno viene eseguito con `pg_prove` da `TestDatabase` e deve chiudersi con `ROLLBACK`.

## Primo compito (M0)

Crea lo scheletro: solution con i progetti sopra, `global.json` su .NET 10, `.editorconfig`, `.gitignore`, `LICENSE` MIT, `README.md` con disclaimer, `docker-compose.yml` con PostgreSQL 18 e pgvector (tag `pgvector/pgvector:pg18`, da verificare su Docker Hub), un runner DbUp con la prima migrazione (schema `sec`: `aziende`, `utenti`, `ruoli`, `permessi`, `ruoli_permessi`, `utenti_ruoli`, `audit_log`) e il primo test pgTAP sull'isolamento tra aziende, più un workflow GitHub Actions che compila e testa su Windows, macOS e Linux.

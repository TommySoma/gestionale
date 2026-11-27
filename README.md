# Gestionale impianti termici e clima

Gestionale desktop on-premise (Windows e macOS) per piccole imprese che installano e manutengono caldaie, climatizzatori e pompe di calore. Trasforma gli obblighi normativi in scadenze e documenti già pronti.

> **Stato:** milestone M0-M4 completate (27/11/2026): accessi con secondo fattore, anagrafiche, impianti, interventi, scadenze, preventivi e compliance Toscana (rapporti di controllo, bollini, file di import per il SIERT). Prossima milestone: M5, chatbot base. Il piano completo è in [docs/piano-di-sviluppo.md](docs/piano-di-sviluppo.md).

## Funzioni

- **Accessi e ruoli:** Titolare, Amministrativo, Responsabile tecnico, Tecnico, Sola lettura, Amministratore di sistema; password Argon2id, TOTP obbligatorio per Titolare e Amministratore di sistema; il Tecnico vede e chiude solo i propri interventi.
- **Anagrafiche:** clienti, sedi (con codice ISTAT del comune), contatti, consensi privacy, anonimizzazione su richiesta dell'interessato.
- **Impianti:** generatori con la sigla del catasto, riferimenti al catasto regionale, foto e documenti.
- **Interventi:** numerazione senza buchi, calendario per tecnico con trascinamento, chiusura con righe di materiali e manodopera, rapportino PDF.
- **Scadenze:** manutenzione dalla periodicità dell'impianto e controllo di efficienza dalle regole di cadenza, lista da richiamare con i richiami; pagina Oggi come pagina iniziale.
- **Preventivi:** listino con storico dei prezzi, preventivi numerati all'emissione con PDF, intervento generato da un preventivo accettato.
- **Compliance Toscana:** rapporti di controllo RCEE tipo 1 e 2 compilati su un modulo costruito dal tracciato SIERT v3.0 e validati contro di esso; registro dei bollini e assegnazione al rapporto; convalida e firma; file di import massivo per il portale SIERT ed esiti del report. La bozza del rapporto nasce anche chiudendo una manutenzione con il controllo di efficienza. Scelte e domande aperte nell'[ADR 0006](docs/adr/0006-rapporti-controllo-e-tracciato-siert.md).
- **Non ancora presenti:** dichiarazione di conformità, registro F-Gas, PDF del rapporto di controllo, catasto lombardo (CURIT), chatbot. L'invio al SIERT è assistito: il file si carica a mano sul portale, perché non è nota un'API pubblica.

Tipi di impianto, tipi di generatore, combustibili e regole di cadenza contengono per ora solo **voci di prova** con valori fittizi, da sostituire con quelli dei testi ufficiali.

## Avvertenza

Il software è un **supporto alla compilazione** e non sostituisce la verifica normativa né la responsabilità di chi firma. I documenti generati (rapporti di controllo, file di invio al catasto e, nelle prossime milestone, dichiarazioni di conformità e registri F-Gas) vanno controllati dall'utente sui testi ufficiali prima della firma e dell'invio. Le regole regionali e nazionali incluse sono ricavate da documenti pubblici e **non sono validate sul campo**. Il software è fornito "così com'è", senza garanzie (vedi [LICENSE](LICENSE)).

Tutti i dati presenti nel repository sono **fittizi**.

## Architettura

| Livello | Tecnologia |
| --- | --- |
| Desktop | Avalonia 12, MVVM con CommunityToolkit.Mvvm |
| API | ASP.NET Core su .NET 10 LTS, minimal API |
| Database | PostgreSQL 18 con pgvector, Row-Level Security per azienda |
| Migrazioni | DbUp, script SQL numerati in `db/migrations` |
| Test | xUnit, Testcontainers, pgTAP |

Il client desktop non parla mai direttamente con il database: passa sempre dall'API.

```
src/
  Gestionale.Domain          entità e regole di dominio
  Gestionale.Application     casi d'uso
  Gestionale.Infrastructure  accesso ai dati e servizi esterni
  Gestionale.Api             API REST
  Gestionale.Desktop         client Avalonia
  Gestionale.Migrator        applica le migrazioni DbUp
db/
  migrations/                script SQL numerati (mai modificare uno script già applicato)
  tests/                     test pgTAP e immagine Docker per eseguirli
deploy/
  docker-compose.yml         PostgreSQL 18 con pgvector
  initdb/                    creazione dei ruoli del database al primo avvio
docs/                        piano di sviluppo e ADR
tests/
  Gestionale.Tests.Unit
  Gestionale.Tests.Integration   Testcontainers + pgTAP (richiede Docker)
```

## Requisiti

- [.NET SDK 10](https://dotnet.microsoft.com/download) (la versione è fissata in `global.json`)
- Docker (Docker Desktop su Windows e macOS) per il database e per i test di integrazione

## Avvio in sviluppo

**Percorso rapido su Windows.** Lo script prepara tutto la prima volta (password e chiavi casuali in `deploy/.env` e nei user secrets, database, migrazioni) e avvia l'API; il secondo crea l'azienda e il Titolare, chiedendo i dati nel terminale:

```
powershell -ExecutionPolicy Bypass -File .\scripts\sviluppo\avvia.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\sviluppo\primo-avvio.ps1
```

Poi `dotnet run --project src/Gestionale.Desktop` e accedi con il Titolare. I passi a mano, per capire cosa fanno gli script:

1. Copia `deploy/.env.example` in `deploy/.env` e scegli password robuste (il file `.env` non va mai nel repository).
2. Avvia il database:

   ```
   docker compose -f deploy/docker-compose.yml up -d db
   ```

3. Applica le migrazioni come `app_owner` (porta e password come in `deploy/.env`):

   ```
   dotnet run --project src/Gestionale.Migrator -- "Host=localhost;Port=55432;Database=gestionale;Username=app_owner;Password=<password>"
   ```

   In alternativa imposta la variabile d'ambiente `GESTIONALE_DB_MIGRAZIONI`.

4. Configura l'API. I segreti non vanno mai in `appsettings.json`: in sviluppo usa i *user secrets* di .NET, in produzione le variabili d'ambiente (`Autenticazione__ChiaveFirma` e così via). Per generare una chiave casuale di 32 byte in PowerShell:

   ```
   [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   ```

   | Impostazione | Contenuto |
   | --- | --- |
   | `ConnectionStrings:Database` | Stringa di connessione come `app_api` |
   | `Autenticazione:ChiaveFirma` | Chiave dei token, Base64, almeno 32 byte |
   | `Autenticazione:ChiaveCifraturaMfa` | Chiave AES dei segreti TOTP, Base64, esattamente 32 byte |
   | `Autenticazione:TokenInstallazione` | Token per il primo avvio; lascialo vuoto dopo l'installazione |

   ```
   dotnet user-secrets --project src/Gestionale.Api set "Autenticazione:ChiaveFirma" "<chiave>"
   ```

5. Avvia l'API con `dotnet run --project src/Gestionale.Api` e completa il primo avvio con `POST /api/installazione` (intestazione `X-Token-Installazione`): crea l'azienda, il Titolare e l'Amministratore di sistema. Al primo accesso entrambi devono configurare il secondo fattore con un'app TOTP.

## Accesso

| Passo | Endpoint |
| --- | --- |
| Email e password | `POST /api/accesso`: esito `completato`, `mfa_richiesto` o `mfa_da_configurare` |
| Codice TOTP o di recupero | `POST /api/accesso/mfa` con il token di passaggio |
| Configurazione del secondo fattore | `POST /api/mfa/configurazione`, poi `POST /api/mfa/attivazione` |
| Rinnovo (token a rotazione) | `POST /api/sessione/rinnovo` |
| Uscita | `POST /api/sessione/uscita` |
| Profilo e permessi | `GET /api/io` |
| Utenti | `GET` e `POST /api/utenti` |

Il token di accesso dura 15 minuti, la sessione al massimo 12 ore. Dopo 5 errori l'account si blocca per 1, 2, 4 minuti e così via, fino a un'ora.

## Anagrafiche

| Cosa | Endpoint |
| --- | --- |
| Elenco e ricerca (nome, codice fiscale, partita IVA, tipo; a pagine) | `GET /api/clienti?cerca=&tipo=&pagina=&dimensione=` |
| Scheda con sedi, contatti e consensi correnti | `GET /api/clienti/{id}` |
| Nuovo, modifica, eliminazione logica | `POST /api/clienti`, `PUT /api/clienti/{id}`, `DELETE /api/clienti/{id}?versione=` |
| Sedi e contatti | `/api/clienti/{id}/sedi` e `/api/clienti/{id}/contatti` |
| Consensi privacy (storico, vale l'ultimo) | `GET` e `POST /api/clienti/{id}/consensi`, `GET /api/finalita-privacy` |

Ogni modifica e cancellazione richiede la `versione` letta: se nel frattempo un collega ha cambiato il dato, l'API risponde 409 invece di sovrascriverlo. Codice fiscale (anche con omocodia) e partita IVA sono controllati con la cifra di controllo. Il Tecnico per ora non consulta elenco e schede dei clienti: indirizzo e responsabile li trova negli interventi e negli impianti.

**Anonimizzazione (GDPR).** Quando un cliente chiede la cancellazione dei propri dati, il Titolare lo anonimizza con `POST /api/clienti/{id}/anonimizzazione?versione=N`: nominativo, codici fiscali, PEC, note e contatti spariscono per sempre; restano gli indirizzi delle sedi (servono ai documenti degli impianti) e lo storico dei consensi senza note. Non si annulla. Scelte e limiti nell'[ADR 0004](docs/adr/0004-anonimizzazione-clienti.md).

## Client desktop

```
dotnet run --project src/Gestionale.Desktop
```

Alla prima apertura indica l'indirizzo del server (ad esempio `http://localhost:5225` in sviluppo) con "Cambia server". Il client parla solo con l'API, mai con il database.

- **Accesso:** email e password, poi il codice dell'app di autenticazione. Titolare e Amministratore di sistema configurano il secondo fattore al primo accesso (codice QR, chiave manuale, codici di recupero).
- **Sessione:** il token di accesso resta in memoria e si rinnova da solo; il token di rinnovo sta solo nel portachiavi del sistema (Windows Credential Manager, portachiavi di macOS), così riaprendo l'app nella stessa giornata non serve accedere di nuovo. Le preferenze non segrete (server, ultima email, tema) sono in `impostazioni.json` nella cartella dei dati dell'utente.
- **Anagrafiche:** elenco con ricerca, filtri e pagine; scheda con sedi, contatti e consensi; creazione e modifica con i messaggi d'errore dell'API; anonimizzazione per il Titolare, con una parola da scrivere per confermare.
- **Impianti, interventi e scadenze:** scheda dell'impianto con generatori, catasto, documenti, interventi, scadenze e rapporti; calendario settimanale per tecnico; chiusura dell'intervento con righe, esito e, per una manutenzione, controllo di efficienza e bozza del rapporto.
- **Preventivi e listino:** elenco con filtri, editor con le voci del listino, PDF, conversione in intervento.
- **Rapporti di controllo:** elenco con il termine di trasmissione, scheda con il modulo del tracciato SIERT, errori sotto ogni campo, bollino, convalida, firma e annullamento.
- **Bollini e invii al SIERT:** registro dei bollini con i disponibili per tipo; preparazione del file di import, scarico, caricamento sul portale ed esiti del report.
- **Dati aziendali:** intestazione dei documenti e codice ditta SIERT, modificabili dal Titolare.
- **Tema:** chiaro, scuro o come il sistema, dal menu utente. I colori sono in `src/Gestionale.Desktop/Tema/Stili.axaml`, con le due varianti e contrasti almeno AA.
- **Testi:** tutti in `src/Gestionale.Desktop/Risorse/Testi.resx`.

## Build e test

```
dotnet build
dotnet test
```

I test di integrazione avviano un PostgreSQL usa e getta con Testcontainers, applicano le migrazioni, eseguono i test pgTAP di `db/tests` e provano l'API dall'accesso al database. Richiedono Docker con container Linux; senza Docker escludili:

```
dotnet test --filter-not-trait "Category=Integration"
```

## Sicurezza del database

- Tre ruoli: `app_owner` (migrazioni, proprietario degli oggetti), `app_api` (runtime, soggetto a RLS, senza `BYPASSRLS`), `app_chatbot` (sola lettura su viste dedicate).
- Ogni tabella con `azienda_id` ha una policy di Row-Level Security: l'API imposta `SET LOCAL app.azienda_id` all'inizio di ogni transazione.
- `sec.audit_log` è in sola aggiunta: UPDATE, DELETE e TRUNCATE sono negati da privilegi e trigger.

## Licenza

[MIT](LICENSE).

Il carattere Inter, incorporato nei documenti PDF generati dall'API, è distribuito con la SIL Open Font License 1.1: vedi [src/Gestionale.Infrastructure/Documenti/Caratteri/OFL-Inter.txt](src/Gestionale.Infrastructure/Documenti/Caratteri/OFL-Inter.txt). I PDF si generano con SkiaSharp (MIT); le scelte sono in [ADR 0005](docs/adr/0005-pdf-con-skiasharp.md).

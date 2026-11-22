# Gestionale impianti termici e clima

Gestionale desktop on-premise (Windows e macOS) per piccole imprese che installano e manutengono caldaie, climatizzatori e pompe di calore. Trasforma gli obblighi normativi in scadenze e documenti già pronti.

> **Stato:** milestone M0 (fondamenta). Nessuna funzione di business è ancora disponibile. Il piano completo è in [docs/piano-di-sviluppo.md](docs/piano-di-sviluppo.md).

## Avvertenza

Il software è un **supporto alla compilazione** e non sostituisce la verifica normativa né la responsabilità di chi firma. I documenti generati (rapporti di controllo, dichiarazioni di conformità, registri F-Gas) vanno controllati dall'utente sui testi ufficiali prima della firma e dell'invio. Le regole regionali e nazionali incluse sono ricavate da documenti pubblici e **non sono validate sul campo**. Il software è fornito "così com'è", senza garanzie (vedi [LICENSE](LICENSE)).

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

1. Copia `deploy/.env.example` in `deploy/.env` e scegli password robuste (il file `.env` non va mai nel repository).
2. Avvia il database:

   ```
   docker compose -f deploy/docker-compose.yml up -d db
   ```

3. Applica le migrazioni come `app_owner` (porta e password come in `deploy/.env`):

   ```
   dotnet run --project src/Gestionale.Migrator -- "Host=localhost;Port=5432;Database=gestionale;Username=app_owner;Password=<password>"
   ```

   In alternativa imposta la variabile d'ambiente `GESTIONALE_DB_MIGRAZIONI`.

## Build e test

```
dotnet build
dotnet test
```

I test di integrazione avviano un PostgreSQL usa e getta con Testcontainers, applicano le migrazioni ed eseguono i test pgTAP di `db/tests`. Richiedono Docker con container Linux; senza Docker escludili:

```
dotnet test --filter "Category!=Integration"
```

## Sicurezza del database

- Tre ruoli: `app_owner` (migrazioni, proprietario degli oggetti), `app_api` (runtime, soggetto a RLS, senza `BYPASSRLS`), `app_chatbot` (sola lettura su viste dedicate).
- Ogni tabella con `azienda_id` ha una policy di Row-Level Security: l'API imposta `SET LOCAL app.azienda_id` all'inizio di ogni transazione.
- `sec.audit_log` è in sola aggiunta: UPDATE, DELETE e TRUNCATE sono negati da privilegi e trigger.

## Licenza

[MIT](LICENSE).

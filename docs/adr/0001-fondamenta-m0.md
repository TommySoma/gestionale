# ADR 0001 – Scelte tecniche delle fondamenta (M0)

- **Stato:** proposto
- **Data:** 22 novembre 2026

## Contesto

Il piano fissa lo stack ma lascia aperti alcuni dettagli che servono già in M0: dove vive il runner delle migrazioni, come si eseguono i test pgTAP, chi crea i ruoli del database e quale runner di test usare con .NET 10.

## Decisioni

1. **Progetto `Gestionale.Migrator`.** Un eseguibile console separato applica le migrazioni DbUp; gli script restano in `db/migrations` e vengono incorporati come risorse. La sostituzione di variabili di DbUp è disattivata: gli script sono SQL puro.
2. **Ruoli del database creati da `deploy/initdb/01-ruoli.sh`.** Al primo avvio del container lo script crea `app_owner`, `app_api` e `app_chatbot` con le password prese dall'ambiente e rende `app_owner` proprietario del database. Le migrazioni girano come `app_owner`, mai come superutente, e assumono che i ruoli esistano.
3. **pgTAP tramite Testcontainers.** I test di integrazione costruiscono l'immagine `db/tests/Dockerfile` (la stessa base `pgvector/pgvector:0.8.7-pg18` del deploy, più pgTAP e `pg_prove`), applicano le migrazioni ed eseguono ogni file `db/tests/*.sql` con `pg_prove`. Così `dotnet test` è l'unico comando per tutti i test.
4. **Microsoft.Testing.Platform.** xUnit v3 non supporta più VSTest con l'SDK .NET 10: `global.json` attiva il nuovo `dotnet test`. Per escludere i test di integrazione si usa `--filter-not-trait "Category=Integration"`.
5. **CI.** Su Linux girano tutti i test; su Windows e macOS solo gli unitari, perché quei runner non hanno Docker con container Linux. Lo stile del codice è verificato in compilazione (`EnforceCodeStyleInBuild`), la formattazione con `dotnet format whitespace`.
6. **Nomi nel codice C#.** Proposta applicata in M0, da confermare: classi e membri di dominio e applicativi in italiano (`Migratore`, `PercorsiRepository`), suffissi e termini tecnici in inglese (`DatabaseFixture`, `ViewModel`).

## Conseguenze

- Su Windows e macOS l'isolamento tra aziende è verificato solo in locale, con Docker Desktop.
- Cambiare una password dei ruoli dopo il primo avvio richiede un `ALTER ROLE` manuale: lo script di init non viene rieseguito su un volume esistente.
- La RLS non usa `FORCE`: `app_owner` vede tutte le righe, ed è voluto per le migrazioni. L'API non deve mai connettersi come `app_owner`.

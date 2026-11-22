# ADR 0002 – Autenticazione su misura sullo schema sec

- **Stato:** accettato
- **Data:** 22 novembre 2026
- **Sostituisce:** la riga "Autenticazione" della sezione Stack tecnologico del piano (ASP.NET Core Identity)

## Contesto

Utenti, ruoli e permessi vivono già nello schema `sec`, con chiavi UUID v7, Row-Level Security per azienda e funzioni SQL testate con pgTAP. ASP.NET Core Identity si aspetta EF Core e le proprie tabelle: per usarlo servirebbero store personalizzati che duplicano quello che lo schema fa già. Inoltre l'email era unica solo dentro un'azienda, quindi al login il server non sapeva in quale azienda cercare l'utente.

## Decisioni

1. **Niente ASP.NET Core Identity.** L'autenticazione è scritta su misura con componenti piccoli e con licenza MIT:
   - hash delle password con Argon2id (Konscious.Security.Cryptography.Argon2), in formato PHC (`$argon2id$v=19$m=...,t=...,p=...$sale$hash`), parametri configurabili e minimi OWASP;
   - secondo fattore TOTP (Otp.NET), con il segreto cifrato in AES-GCM e protezione contro il riuso dello stesso codice;
   - token di accesso JWT firmati HMAC-SHA256 (Microsoft.AspNetCore.Authentication.JwtBearer), validi 15 minuti, con i permessi dell'utente come claim;
   - token di rinnovo casuali, salvati solo come hash SHA-256 in `sec.sessioni_rinnovo`, ruotati a ogni uso; il riuso di un token già ruotato revoca tutta la catena.
2. **Email unica su tutto il database.** Si accede con email e password, senza codice azienda. In un'installazione on-premise c'è di fatto una sola azienda; se un giorno un server ospiterà più imprese, si aggiungerà il codice azienda con un nuovo ADR.
3. **Funzioni `SECURITY DEFINER` solo dove la RLS non può sapere l'azienda:** lettura dei dati di accesso per email, rotazione dei token di rinnovo, inizializzazione dell'installazione. Ognuna fa una sola cosa, fissa `search_path` ed è eseguibile solo da `app_api`. Tutto il resto gira come `app_api` sotto RLS.
4. **Chi assegna i ruoli.** Il Titolare può assegnare qualsiasi ruolo; chi non è Titolare può assegnare solo ruoli che ha già. Così l'Amministratore di sistema gestisce gli account ma non può creare un utente con accesso ai dati di business e usarlo per sé. La regola è applicata dall'API ed è da confermare con l'uso reale.
5. **Primo avvio protetto da un token di installazione.** L'endpoint che crea la prima azienda, il Titolare e l'Amministratore di sistema risponde solo finché non esiste nessuna azienda e solo con il token impostato nella configurazione del server.

## Conseguenze

- Più codice nostro da testare: ogni passo del flusso ha test unitari, test pgTAP e un test di integrazione end-to-end.
- Lockout, rotazione e revoca stanno in funzioni SQL transazionali, coerenti con le altre regole del progetto.
- Il controllo contro elenchi di password note è previsto da un'interfaccia; l'elenco vero va scelto e aggiunto, verificandone la licenza.

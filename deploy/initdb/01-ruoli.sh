#!/bin/sh
# Eseguito una sola volta da docker-entrypoint al primo avvio, con volume vuoto.
# Crea i tre ruoli del database e rende app_owner proprietario del database,
# così le migrazioni non girano come superutente. Le password arrivano solo
# dalle variabili d'ambiente (deploy/.env, mai nel repository).
set -eu

: "${APP_OWNER_PASSWORD:?APP_OWNER_PASSWORD non impostata}"
: "${APP_API_PASSWORD:?APP_API_PASSWORD non impostata}"
: "${APP_CHATBOT_PASSWORD:?APP_CHATBOT_PASSWORD non impostata}"

psql -v ON_ERROR_STOP=1 --no-psqlrc \
    --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -v db="$POSTGRES_DB" \
    -v owner_pw="$APP_OWNER_PASSWORD" \
    -v api_pw="$APP_API_PASSWORD" \
    -v chatbot_pw="$APP_CHATBOT_PASSWORD" <<'SQL'
-- Proprietario degli oggetti: esegue le migrazioni.
SELECT format('CREATE ROLE app_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'owner_pw') \gexec
-- Runtime dell'API: soggetto alla Row-Level Security.
SELECT format('CREATE ROLE app_api LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', :'api_pw') \gexec
-- Chatbot: sola lettura sulle viste v_chatbot_*.
SELECT format('CREATE ROLE app_chatbot LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', :'chatbot_pw') \gexec

SELECT format('ALTER DATABASE %I OWNER TO app_owner', :'db') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'db') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO app_api, app_chatbot', :'db') \gexec
SQL

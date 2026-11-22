# Immagine per i test: la stessa base del deploy più pgTAP e pg_prove.
# Usata da Testcontainers nei test di integrazione, mai in produzione.
FROM pgvector/pgvector:0.8.7-pg18

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        postgresql-18-pgtap \
        libtap-parser-sourcehandler-pgtap-perl \
    && rm -rf /var/lib/apt/lists/*

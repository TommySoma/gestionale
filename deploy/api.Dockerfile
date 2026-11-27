# API e migrazioni in un'unica immagine Linux, costruita dalla radice del repository:
#   docker build -f deploy/api.Dockerfile .
# Due servizi del compose la usano con due comandi diversi: le migrazioni girano una
# volta come app_owner, poi l'API resta attiva come app_api.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilazione
WORKDIR /sorgenti

# Prima i progetti, così il ripristino dei pacchetti resta in cache finché non cambiano.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Gestionale.Domain/Gestionale.Domain.csproj src/Gestionale.Domain/
COPY src/Gestionale.Application/Gestionale.Application.csproj src/Gestionale.Application/
COPY src/Gestionale.Infrastructure/Gestionale.Infrastructure.csproj src/Gestionale.Infrastructure/
COPY src/Gestionale.Api/Gestionale.Api.csproj src/Gestionale.Api/
COPY src/Gestionale.Migrator/Gestionale.Migrator.csproj src/Gestionale.Migrator/
RUN dotnet restore src/Gestionale.Api/Gestionale.Api.csproj \
    && dotnet restore src/Gestionale.Migrator/Gestionale.Migrator.csproj

COPY src/ src/
COPY db/migrations/ db/migrations/
RUN dotnet publish src/Gestionale.Api/Gestionale.Api.csproj -c Release --no-restore -o /app/api \
    && dotnet publish src/Gestionale.Migrator/Gestionale.Migrator.csproj -c Release --no-restore -o /app/migratore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=compilazione /app ./

# Npgsql cerca la libreria GSSAPI all'avvio: senza, scrive un errore innocuo nei log.
# I documenti caricati stanno in un volume; il processo non gira come root.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /dati/documenti && chown -R $APP_UID /dati
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Documenti__Cartella=/dati/documenti
EXPOSE 8080
WORKDIR /app/api
ENTRYPOINT ["dotnet", "Gestionale.Api.dll"]

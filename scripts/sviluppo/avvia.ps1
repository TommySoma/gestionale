# Avvia l'ambiente di sviluppo: database, migrazioni, configurazione dell'API e API.
# Al primo uso genera password e chiavi casuali, che restano solo su questo computer:
# deploy/.env (escluso da git) e i user secrets di .NET. Non serve modificarle a mano.
#
# Uso, dalla cartella del progetto:  .\scripts\sviluppo\avvia.ps1

$ErrorActionPreference = 'Stop'
$radice = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$fileEnv = Join-Path $radice 'deploy\.env'
$progettoApi = Join-Path $radice 'src\Gestionale.Api'

function Get-Casuale([int] $byte) {
    $dati = New-Object byte[] $byte
    $generatore = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generatore.GetBytes($dati) } finally { $generatore.Dispose() }
    return $dati
}

function Get-PasswordCasuale { -join ((Get-Casuale 24) | ForEach-Object { $_.ToString('x2') }) }

function Get-ChiaveCasuale { [Convert]::ToBase64String((Get-Casuale 32)) }

# 1. Password del database (solo la prima volta).
if (-not (Test-Path $fileEnv)) {
    Write-Host 'Creo deploy/.env con password casuali...'
    @(
        '# Generato da scripts/sviluppo/avvia.ps1: password solo per lo sviluppo su questo computer.'
        "POSTGRES_PASSWORD=$(Get-PasswordCasuale)"
        "APP_OWNER_PASSWORD=$(Get-PasswordCasuale)"
        "APP_API_PASSWORD=$(Get-PasswordCasuale)"
        "APP_CHATBOT_PASSWORD=$(Get-PasswordCasuale)"
        'DB_PORTA=55432'
    ) | Set-Content -Path $fileEnv -Encoding ascii
}

$valori = @{}
Get-Content $fileEnv | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object {
    $null = $_ -match '^\s*([A-Z_]+)=(.*)$'
    $valori[$Matches[1]] = $Matches[2]
}
$porta = if ($valori['DB_PORTA']) { $valori['DB_PORTA'] } else { '55432' }

# 2. Database e migrazioni.
Write-Host 'Avvio PostgreSQL...'
docker compose -f (Join-Path $radice 'deploy\docker-compose.yml') up -d --wait db
if ($LASTEXITCODE -ne 0) { throw 'Docker non ha avviato il database: controlla il messaggio qui sopra (Docker Desktop aperto? porta già occupata? cambia DB_PORTA in deploy\.env).' }

Write-Host 'Applico le migrazioni...'
dotnet run --project (Join-Path $radice 'src\Gestionale.Migrator') -- "Host=localhost;Port=$porta;Database=gestionale;Username=app_owner;Password=$($valori['APP_OWNER_PASSWORD'])"
if ($LASTEXITCODE -ne 0) { throw 'Migrazioni non applicate.' }

# 3. Configurazione dell'API nei user secrets (solo i valori mancanti).
$segreti = (dotnet user-secrets list --project $progettoApi 2>$null) -join "`n"
function Set-SeManca([string] $chiave, [string] $valore) {
    if ($segreti -notmatch [regex]::Escape($chiave) + ' = ') {
        dotnet user-secrets set $chiave $valore --project $progettoApi | Out-Null
    }
}
Set-SeManca 'ConnectionStrings:Database' "Host=localhost;Port=$porta;Database=gestionale;Username=app_api;Password=$($valori['APP_API_PASSWORD'])"
Set-SeManca 'Autenticazione:ChiaveFirma' (Get-ChiaveCasuale)
Set-SeManca 'Autenticazione:ChiaveCifraturaMfa' (Get-ChiaveCasuale)
Set-SeManca 'Autenticazione:TokenInstallazione' (Get-PasswordCasuale)

# 4. API su http://localhost:5225 (Ctrl+C per fermarla).
Write-Host ''
Write-Host 'Avvio l''API su http://localhost:5225 - premi Ctrl+C per fermarla.'
Write-Host 'Al primo uso, in un altro terminale:  .\scripts\sviluppo\primo-avvio.ps1'
dotnet run --project $progettoApi --launch-profile http

# Avvia l'ambiente di sviluppo in Docker: database, migrazioni e API, tutti in container Linux.
# Così Smart App Control di Windows non blocca i file appena compilati; sul computer gira
# solo il client desktop. Al primo uso genera password e chiavi casuali, che restano solo
# su questo computer in deploy/.env (escluso da git). Non serve modificarle a mano.
#
# Uso, dalla cartella del progetto:  .\scripts\sviluppo\avvia.ps1
# Per fermare tutto:                 docker compose -f deploy\docker-compose.yml --profile api down

$ErrorActionPreference = 'Stop'
$radice = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$fileEnv = Join-Path $radice 'deploy\.env'
$compose = Join-Path $radice 'deploy\docker-compose.yml'
$progettoApi = Join-Path $radice 'src\Gestionale.Api'

function Get-Casuale([int] $byte) {
    $dati = New-Object byte[] $byte
    $generatore = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generatore.GetBytes($dati) } finally { $generatore.Dispose() }
    return $dati
}

function Get-PasswordCasuale { -join ((Get-Casuale 24) | ForEach-Object { $_.ToString('x2') }) }

function Get-ChiaveCasuale { [Convert]::ToBase64String((Get-Casuale 32)) }

function Read-Env {
    $letti = @{}
    Get-Content $fileEnv | ForEach-Object { if ($_ -match '^\s*([A-Z_]+)=(.*)$') { $letti[$Matches[1]] = $Matches[2] } }
    return $letti
}

# 1. Password del database (solo la prima volta).
if (-not (Test-Path $fileEnv)) {
    Write-Host 'Creo deploy/.env con password casuali...'
    @(
        '# Generato da scripts/sviluppo/avvia.ps1: password solo per lo sviluppo su questo computer.'
        "POSTGRES_PASSWORD=$(Get-PasswordCasuale)"
        "APP_OWNER_PASSWORD=$(Get-PasswordCasuale)"
        "APP_API_PASSWORD=$(Get-PasswordCasuale)"
        "APP_CHATBOT_PASSWORD=$(Get-PasswordCasuale)"
        'DB_PORTA=15432'
    ) | Set-Content -Path $fileEnv -Encoding ascii
}

# 2. Chiavi dell'API (solo quelle mancanti). Se l'API girava già con dotnet run, si
#    riprendono dai user secrets: con chiavi nuove i secondi fattori già configurati
#    non si leggerebbero più.
$valori = Read-Env
$segreti = @{}
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (dotnet user-secrets list --project $progettoApi 2>$null) | ForEach-Object {
        if ($_ -match '^(\S+) = (.*)$') { $segreti[$Matches[1]] = $Matches[2] }
    }
}
$nuove = @()
foreach ($voce in @(
        @{ Env = 'API_CHIAVE_FIRMA'; Segreto = 'Autenticazione:ChiaveFirma'; Nuova = { Get-ChiaveCasuale } },
        @{ Env = 'API_CHIAVE_CIFRATURA_MFA'; Segreto = 'Autenticazione:ChiaveCifraturaMfa'; Nuova = { Get-ChiaveCasuale } },
        @{ Env = 'API_TOKEN_INSTALLAZIONE'; Segreto = 'Autenticazione:TokenInstallazione'; Nuova = { Get-PasswordCasuale } })) {
    if (-not $valori[$voce.Env]) {
        $valore = if ($segreti[$voce.Segreto]) { $segreti[$voce.Segreto] } else { & $voce.Nuova }
        $nuove += "$($voce.Env)=$valore"
    }
}
if ($nuove.Count -gt 0) {
    Write-Host 'Aggiungo le chiavi dell''API a deploy/.env...'
    $nuove | Add-Content -Path $fileEnv -Encoding ascii
}

# 3. Database.
Write-Host 'Avvio PostgreSQL...'
docker compose -f $compose up -d --wait db
if ($LASTEXITCODE -ne 0) { throw 'Docker non ha avviato il database: controlla il messaggio qui sopra (Docker Desktop aperto? porta già occupata o riservata da Windows? Le porte riservate si vedono con "netsh interface ipv4 show excludedportrange protocol=tcp": scegline una fuori da quegli intervalli e cambia DB_PORTA in deploy\.env).' }

# 4. Immagine dell'API e migrazioni (la prima compilazione richiede qualche minuto).
Write-Host 'Compilo l''immagine dell''API e applico le migrazioni...'
docker compose -f $compose --profile api build migrazioni
if ($LASTEXITCODE -ne 0) { throw 'Immagine dell''API non compilata: controlla il messaggio qui sopra.' }
docker compose -f $compose --profile api run --rm migrazioni
if ($LASTEXITCODE -ne 0) { throw 'Migrazioni non applicate.' }

# 5. API su http://localhost:5225, in background.
Write-Host 'Avvio l''API...'
docker compose -f $compose --profile api up -d --force-recreate api
if ($LASTEXITCODE -ne 0) { throw 'API non avviata: controlla il messaggio qui sopra.' }

$server = 'http://localhost:5225'
$pronta = $false
Write-Host 'Attendo che l''API risponda (fino a 90 secondi)...'
for ($tentativo = 0; $tentativo -lt 90 -and -not $pronta; $tentativo++) {
    try { Invoke-RestMethod 'http://127.0.0.1:5225/api/installazione' -TimeoutSec 5 | Out-Null; $pronta = $true }
    catch { Start-Sleep -Seconds 1 }
}
if (-not $pronta) {
    throw "L'API non risponde su $server. Guarda i log con: docker compose -f deploy\docker-compose.yml --profile api logs api"
}

Write-Host ''
Write-Host "API pronta su $server."
Write-Host 'Al primo uso:        .\scripts\sviluppo\primo-avvio.ps1'
Write-Host 'Poi il client:       dotnet run --project src\Gestionale.Desktop'
Write-Host 'Log dell''API:        docker compose -f deploy\docker-compose.yml --profile api logs -f api'

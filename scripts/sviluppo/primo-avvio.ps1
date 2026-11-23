# Primo avvio di un'installazione di sviluppo: crea l'azienda e il Titolare.
# Serve una volta sola, con l'API già avviata (scripts/sviluppo/avvia.ps1).
# La password la scegli tu qui e non viene salvata da nessuna parte: usa dati fittizi.
#
# Uso, dalla cartella del progetto:  .\scripts\sviluppo\primo-avvio.ps1

$ErrorActionPreference = 'Stop'
$radice = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$progettoApi = Join-Path $radice 'src\Gestionale.Api'
$server = 'http://localhost:5225'

function Read-ConPredefinito([string] $domanda, [string] $predefinito) {
    $risposta = Read-Host "$domanda [$predefinito]"
    if ([string]::IsNullOrWhiteSpace($risposta)) { return $predefinito }
    return $risposta.Trim()
}

$stato = Invoke-RestMethod "$server/api/installazione"
if (-not $stato.daInizializzare) {
    Write-Host 'L''installazione è già configurata: accedi dal client con il Titolare che hai creato.'
    return
}

$segreti = (dotnet user-secrets list --project $progettoApi) -join "`n"
if ($segreti -notmatch 'Autenticazione:TokenInstallazione = (\S+)') {
    throw 'Token di installazione non trovato: esegui prima .\scripts\sviluppo\avvia.ps1'
}
$token = $Matches[1]

Write-Host 'Dati dell''azienda e del Titolare (usa dati fittizi). Invio per accettare il valore tra parentesi.'
$ragioneSociale = Read-ConPredefinito 'Ragione sociale' 'Termoidraulica di prova Srl (fittizia)'
$email = Read-ConPredefinito 'Email del Titolare' 'titolare@prova.example'
$nome = Read-ConPredefinito 'Nome' 'Mario'
$cognome = Read-ConPredefinito 'Cognome' 'Rossi'

do {
    $sicura = Read-Host 'Password del Titolare (almeno 12 caratteri)' -AsSecureString
    $puntatore = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sicura)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntatore) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntatore) }
} while ($password.Length -lt 12)

$corpo = @{
    ragioneSociale = $ragioneSociale
    regione        = 'toscana'
    titolare       = @{ email = $email; nome = $nome; cognome = $cognome; password = $password }
} | ConvertTo-Json -Depth 3

try {
    Invoke-RestMethod "$server/api/installazione" -Method Post -ContentType 'application/json; charset=utf-8' `
        -Headers @{ 'X-Token-Installazione' = $token } -Body ([Text.Encoding]::UTF8.GetBytes($corpo)) | Out-Null
}
catch {
    $dettaglio = $_.ErrorDetails.Message
    if ($dettaglio) { $dettaglio = ($dettaglio | ConvertFrom-Json).detail }
    throw "Primo avvio non riuscito: $dettaglio"
}
finally {
    $password = $null
}

Write-Host ''
Write-Host "Fatto. Apri il client e accedi con $email."
Write-Host 'Senza un Amministratore separato, il Titolare ha anche quel ruolo: al primo accesso configurerai il secondo fattore.'

#Requires -Version 5.1

# The Windows news tasks must follow the live production database after a cutover.
# Keep the connection in the process environment only; never write it to local JSON.
$projectId = '1c16fd2d-4bfb-4eb7-8357-b49400233490'
$secretKey = 'ConnectionStrings__QueenZoneLegacyCanadaEast'

if ([string]::IsNullOrWhiteSpace($env:BWS_ACCESS_TOKEN)) {
    $env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable('BWS_ACCESS_TOKEN', 'User')
}
if ([string]::IsNullOrWhiteSpace($env:BWS_ACCESS_TOKEN)) {
    throw 'BWS_ACCESS_TOKEN is unavailable for the scheduled-task account.'
}

$bws = Get-Command bws -ErrorAction SilentlyContinue
if ($bws) {
    $bwsPath = $bws.Source
}
else {
    $bwsPath = Join-Path $env:USERPROFILE 'bin\bws.exe'
    if (-not (Test-Path -LiteralPath $bwsPath)) {
        throw 'bws was not found for the scheduled-task account.'
    }
}

# Capture stdout so no secret value reaches Task Scheduler logs.
$secretJson = & $bwsPath secret list $projectId --output json 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not read the NewsAgent connection from Bitwarden Secrets Manager.'
}
$secrets = $secretJson | ConvertFrom-Json
$matchingSecrets = @($secrets | Where-Object { $_.key -eq $secretKey })
if ($matchingSecrets.Count -ne 1 -or [string]::IsNullOrWhiteSpace($matchingSecrets[0].value)) {
    throw "Expected exactly one non-empty $secretKey secret in Bitwarden project $projectId."
}

try {
    $connection = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($matchingSecrets[0].value)
}
catch {
    throw "$secretKey is not a valid SQL Server connection string."
}
if ($connection.DataSource -ine 'tcp:queenzone-prod-sql.database.windows.net,1433' -or
    $connection.InitialCatalog -ine 'queenzone-db' -or
    $connection.IntegratedSecurity -or
    [string]::IsNullOrWhiteSpace($connection.UserID) -or
    [string]::IsNullOrWhiteSpace($connection.Password)) {
    throw "$secretKey does not target the Canada East production database with SQL credentials."
}

$env:ConnectionStrings__QueenZoneLegacy = $matchingSecrets[0].value
Write-Host 'Loaded NewsAgent production SQL connection from Bitwarden Secrets Manager.'

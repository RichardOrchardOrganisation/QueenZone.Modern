#Requires -Version 7.4
<#
.SYNOPSIS
  Builds and verifies the capped, sanitised dev snapshot.

.DESCRIPTION
  Production SQL and Blob access must be read-only. The script extracts schema only,
  republishes the isolated dev schema, streams curated rows through QueenZone.Tools,
  copies only manifest blobs, applies migrations, rebuilds search, and runs guards.
  The checked-in record limits keep each refresh small and repeatable.

  Without -Apply, it validates boundaries and prints the planned targets without changing
  SQL or Blob Storage. The App Service connection is deliberately outside this script;
  refresh-dev-snapshot.yml enables it only after this script and live checks pass.
#>
[CmdletBinding()]
param(
    [switch] $Apply,
    [string] $ConfigPath = "config/dev-snapshot.json",
    [string] $OutputDirectory = "artifacts/dev-snapshot"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

function Get-RequiredEnvironment([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is required."
    }
    return $value
}

function Assert-SqlBoundary([string] $ConnectionString, [string] $Database, [bool] $ReadOnly) {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
    if ($builder.InitialCatalog -ne $Database) {
        throw "SQL boundary failed: expected database $Database."
    }

    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT CONVERT(bit, CASE WHEN
    HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 1
    OR EXISTS
    (
        SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'INSERT') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'UPDATE') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'DELETE') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'ALTER') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'CONTROL') = 1
    )
    OR EXISTS
    (
        SELECT 1 FROM sys.procedures p JOIN sys.schemas s ON s.schema_id=p.schema_id
        WHERE HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'EXECUTE') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'ALTER') = 1
           OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'CONTROL') = 1
    )
    THEN 1 ELSE 0 END)
"@
        $canMutate = [bool]$command.ExecuteScalar()
        if ($ReadOnly -and $canMutate) {
            throw "Production SQL credential has mutation, DDL, control, or execute permission. Use a dedicated read-only credential."
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Clear-DevSearchReindexRunRequests([string] $ConnectionString) {
    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "DELETE FROM dbo.SearchReindexRunRequests;"
        $deleted = $command.ExecuteNonQuery()
        Write-Information -InformationAction Continue "Removed $deleted refresh-only search reindex run record(s)."
    }
    finally {
        $connection.Dispose()
    }
}

$sourceSql = Get-RequiredEnvironment "DEV_SNAPSHOT_SOURCE_SQL_READONLY"
$targetSql = Get-RequiredEnvironment "DEV_SNAPSHOT_TARGET_SQL"
Assert-SqlBoundary $sourceSql "queenzone-db" $true
Assert-SqlBoundary $targetSql "queenzone-dev-db" $false

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
Write-Information -InformationAction Continue "Source: read-only queenzone-db"
Write-Information -InformationAction Continue "Target: isolated queenzone-dev-db and queenzonedev"
Write-Information -InformationAction Continue "Newest content: $($config.forumThreadCount) complete forum threads; $($config.newsArticleCount) news articles; $($config.articleCount) rows per article source; $($config.photosPerCategory) photos per category"
Write-Information -InformationAction Continue "Guards: database <= $($config.databaseMaximumUsedMb) MB; gallery/forum blobs <= $([math]::Round($config.galleryBudgetBytes / 1MB))/$([math]::Round($config.forumAttachmentBudgetBytes / 1MB)) MB"

if (-not $Apply) {
    Write-Information -InformationAction Continue "Dry run only. Pass -Apply after reviewing the targets and approval gate."
    exit 0
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$dacpacPath = Join-Path ([System.IO.Path]::GetTempPath()) "queenzone-dev-schema-$([guid]::NewGuid().ToString('N')).dacpac"

try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }

    dotnet tool run sqlpackage /Action:Extract `
        "/SourceConnectionString:$sourceSql" `
        "/TargetFile:$dacpacPath" `
        /p:ExtractAllTableData=False `
        /p:VerifyExtraction=False
    if ($LASTEXITCODE -ne 0) { throw "Schema-only production extract failed." }

    Write-Information -InformationAction Continue "Building the newest-first dev snapshot."
    dotnet tool run sqlpackage /Action:Publish `
        "/SourceFile:$dacpacPath" `
        "/TargetConnectionString:$targetSql" `
        /p:ExcludeObjectTypes=Views `
        /p:DropObjectsNotInSource=True `
        /p:BlockOnPossibleDataLoss=False
    if ($LASTEXITCODE -ne 0) { throw "Dev schema publish failed." }

    $manifest = Join-Path $OutputDirectory "manifest.json"
    $summary = Join-Path $OutputDirectory "summary.json"
    dotnet run --project src/QueenZone.Tools --configuration Release --no-restore -- `
        dev-snapshot copy --config $ConfigPath --manifest $manifest --summary $summary
    if ($LASTEXITCODE -ne 0) { throw "Curated row/blob copy failed with exit code $LASTEXITCODE." }

    $env:ConnectionStrings__QueenZoneLegacy = $targetSql
    dotnet ef database update `
        --project src/QueenZone.Data/QueenZone.Data.csproj `
        --startup-project src/QueenZone.Web/QueenZone.Web.csproj
    if ($LASTEXITCODE -ne 0) { throw "Dev migrations failed." }

    dotnet run --project src/QueenZone.SearchReindex.Worker --configuration Release --no-restore -- reindex --force
    if ($LASTEXITCODE -ne 0) { throw "Dev search rebuild failed." }
    Clear-DevSearchReindexRunRequests $targetSql

    dotnet run --project src/QueenZone.Tools --configuration Release --no-restore -- `
        dev-snapshot verify --config $ConfigPath --manifest $manifest --summary $summary
    if ($LASTEXITCODE -ne 0) { throw "Dev snapshot verification failed with exit code $LASTEXITCODE." }

    Write-Information -InformationAction Continue "Dev snapshot passed all database, privacy, relationship, blob, and search guards."
}
finally {
    Remove-Item Env:ConnectionStrings__QueenZoneLegacy -ErrorAction SilentlyContinue
    if (Test-Path $dacpacPath) {
        Remove-Item $dacpacPath -Force
    }
}

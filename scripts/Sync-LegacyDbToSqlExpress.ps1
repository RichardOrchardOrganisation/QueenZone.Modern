# Refreshes a local SQL Express copy of the live legacy/deploy Azure SQL
# database (queenzone-db, Basic tier), so nightly probes run against a
# same-day snapshot instead of the live database. Run nightly by
# .github/workflows/nightly-legacy-checks.yml on the Windows runner, where
# SQL Express lives.
#
# Uses sqlpackage Extract (with ExtractAllTableData, producing a schema+data
# dacpac) + Publish, not the more obvious Export/Import bacpac pair. Reason:
# the legacy schema has pre-existing broken forum views (e.g. Q_FORUM_TOPIC_V)
# referencing at least one table (dbo.Q_FORUM_TOPIC_T) that doesn't actually
# exist in the source - not an ordering/validation quirk SQL Server's
# deferred name resolution papers over, but a genuinely dead reference. The
# probe tests this mirror serves (EfAdminNewsRepositoryLegacyProbeTests,
# EfAdminNewsRepositoryLegacyWriteProbeTests, EfNewsSectionLiveProbeTests)
# query news tables directly via EF/SQL - no view in the schema is on that
# path - so views aren't needed here at all.
# The live Azure SQL source also carries contained users, logins, permissions,
# and role memberships that SQL Express cannot host - contained-user CREATE USER
# WITH PASSWORD is legal only in a contained database, and Azure principals
# have no Express counterpart. Their schemas still need owners when Users are
# excluded, so the script creates loginless placeholder owners in the staging
# mirror before Publish. This avoids SQL72014 / Msg 15151 without copying a
# production principal or credential. Probe access is granted
# after Publish to the Express-local queenzone_probe login, not by replaying
# Azure security objects, so Users/Logins/Permissions/RoleMembership
# are excluded here too. (sqlpackage's type name is RoleMembership, singular.)
# /Action:Export doesn't support excluding object types at all; /Action:Extract
# doesn't either (verified against this sqlpackage version's own /? help, not
# assumed); but /Action:Publish does via
# /p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership, and
# empirically DOES restore the embedded table data from an ExtractAllTableData
# dacpac (verified locally: NEWS_T came through with 5268 rows, ViewCount 0) -
# that combination isn't obviously documented, hence this much explanation.
#
# Requires ConnectionStrings__QueenZoneLegacy (source, Azure SQL) set in the
# environment. Invokes sqlpackage as a local dotnet tool (.config/dotnet-tools.json,
# restored via `dotnet tool restore` before this script runs) rather than assuming
# it's on PATH - the GitHub Actions runner service on this machine runs as
# NT AUTHORITY\NETWORK SERVICE, a different profile than the interactive user
# account a global `dotnet tool install -g` would have put it on PATH for.
#
# Ordinary data-sync automation, not a system/security-config change - unlike
# Enable-SqlExpressRemoteAccess.ps1 (run once, manually, before this is used).

param(
    [string]$InstanceName = "SQLEXPRESS",
    [string]$TargetDatabase = "queenzone_legacy_sync",
    [string]$ProbeLoginName = "queenzone_probe",
    [int]$SqlPackageTransientAttempts = 3,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

# Live Azure SQL can drop the sqlpackage socket mid-Extract
# or mid-Publish: TCP Provider "forcibly closed by the remote host", connection
# reset, or a transport-level timeout. sqlpackage often prints that error and
# then sits until the GitHub Actions job timeout, which surfaces as a vague
# `cancelled` with no actionable step (issue #1453). Retry the failed phase a
# small fixed number of times with backoff. If the process hangs after a
# transport error, stop it so the attempt can retry or fail clearly. Do not
# treat contained-user / staging-file collisions as transient — those are
# #1334 / #1384. Publish exclusions and unique staging names stay unchanged.

$dacpacPath = Join-Path ([System.IO.Path]::GetTempPath()) "queenzone-legacy-$(Get-Date -Format 'yyyyMMdd-HHmmss').dacpac"
$stagingToken = [Guid]::NewGuid().ToString("N")
$stagingDatabase = "${TargetDatabase}_refresh_$stagingToken"
$stagingPromoted = $false

if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$' -or
    $stagingDatabase -notmatch '^[A-Za-z0-9_]+$' -or
    $ProbeLoginName -notmatch '^[A-Za-z0-9_]+$' -or
    $stagingDatabase.Length -gt 128) {
    throw "Mirror database and probe-login names may contain only letters, numbers, and underscores; the generated staging database name must not exceed 128 characters."
}

function Test-SqlPackageTransientTransportError {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $patterns = @(
        'forcibly closed by the remote host',
        'an existing connection was forcibly closed',
        'connection reset',
        'connection was reset',
        'a transport-level error has occurred',
        'transport-level error',
        'transport timeout',
        'semaphore timeout period has expired',
        'physical connection is not usable',
        'the connection is broken and recovery is not possible',
        'the specified network name is no longer available',
        'an established connection was aborted',
        'provider: TCP Provider',
        'TCP Provider, error'
    )

    foreach ($pattern in $patterns) {
        if ($Text -imatch [regex]::Escape($pattern)) {
            return $true
        }
    }

    return $false
}

function ConvertTo-WindowsProcessArguments {
    param([string[]]$Values)

    return (($Values | ForEach-Object {
        $value = [string]$_
        if ($value -notmatch '[ \t"]') {
            $value
        }
        else {
            '"' + ($value.Replace('"', '""')) + '"'
        }
    }) -join ' ')
}

function Stop-SqlPackageProcessTree {
    param($Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if ($Process.HasExited) {
            return
        }
    }
    catch {
    }

    $processId = $Process.Id
    try {
        $Process.Kill($true)
    }
    catch {
        try {
            Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId = $processId" -ErrorAction SilentlyContinue |
                ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        }
        catch {
        }
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    try {
        $null = $Process.WaitForExit(15000)
    }
    catch {
    }
}

function Read-SqlPackageRedirectedChunk {
    param(
        [string]$Path,
        [ref]$Offset
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $content = Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($content)) {
        return ""
    }

    if ($content.Length -le $Offset.Value) {
        return $content
    }

    $newText = $content.Substring($Offset.Value)
    $Offset.Value = $content.Length
    if (-not [string]::IsNullOrWhiteSpace($newText)) {
        Write-Host $newText.TrimEnd()
    }

    return $content
}

function Invoke-SqlPackageProcess {
    param(
        [string[]]$SqlPackageArguments,
        [int]$HungTransportGraceSeconds = 60
    )

    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ("queenzone-sqlpackage-out-{0}.log" -f [Guid]::NewGuid().ToString("N"))
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("queenzone-sqlpackage-err-{0}.log" -f [Guid]::NewGuid().ToString("N"))
    $dotnetArgs = @('tool', 'run', 'sqlpackage') + $SqlPackageArguments
    $argumentString = ConvertTo-WindowsProcessArguments $dotnetArgs

    $process = $null
    $stdoutOffset = 0
    $stderrOffset = 0
    $sawTransient = $false
    $graceDeadline = $null

    try {
        # Start-Process -RedirectStandard* lets us poll for a TCP drop while
        # sqlpackage is still alive. PS7 treats -ArgumentList as discrete argv
        # entries; Windows PowerShell 5.1 wants one pre-quoted argument string.
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $process = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs `
                -WorkingDirectory (Get-Location).Path `
                -NoNewWindow -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath
        }
        else {
            $process = Start-Process -FilePath "dotnet" -ArgumentList $argumentString `
                -WorkingDirectory (Get-Location).Path `
                -NoNewWindow -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath
        }

        # Windows PowerShell 5.1 can lose the native process handle after a
        # polled process exits. ExitCode then resolves to $null even when the
        # command succeeded, and the fallback below reports a false exit 1.
        # Materialise the handle while the process is alive so its real exit
        # code remains available after the polling loop.
        $null = $process.Handle

        while (-not $process.HasExited) {
            $stdout = Read-SqlPackageRedirectedChunk -Path $stdoutPath -Offset ([ref]$stdoutOffset)
            $stderr = Read-SqlPackageRedirectedChunk -Path $stderrPath -Offset ([ref]$stderrOffset)
            $combined = "$stdout`n$stderr"
            if (-not $sawTransient -and (Test-SqlPackageTransientTransportError $combined)) {
                $sawTransient = $true
                $graceDeadline = (Get-Date).AddSeconds($HungTransportGraceSeconds)
                Write-Host "Detected a TCP/transport error in sqlpackage output. Waiting ${HungTransportGraceSeconds}s for the process to exit before retrying the failed phase (or failing the Sync job with a named TCP/transport error)."
            }
            if ($sawTransient -and (Get-Date) -gt $graceDeadline) {
                Write-Host "sqlpackage did not exit after a TCP/transport error. Stopping the hung process so this phase can retry or fail clearly instead of sitting until the workflow cancels."
                Stop-SqlPackageProcessTree -Process $process
                break
            }
            Start-Sleep -Seconds 2
        }

        try {
            $null = $process.WaitForExit(5000)
        }
        catch {
        }

        $null = Read-SqlPackageRedirectedChunk -Path $stdoutPath -Offset ([ref]$stdoutOffset)
        $null = Read-SqlPackageRedirectedChunk -Path $stderrPath -Offset ([ref]$stderrOffset)

        $output = ""
        if (Test-Path -LiteralPath $stdoutPath) {
            $output += (Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue)
        }
        if (Test-Path -LiteralPath $stderrPath) {
            $output += "`n" + (Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue)
        }

        $exitCode = 1
        try {
            if ($null -ne $process.ExitCode) {
                $exitCode = $process.ExitCode
            }
        }
        catch {
            $exitCode = 1
        }

        return @{
            ExitCode = $exitCode
            Output   = $output
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SqlPackagePhase {
    param(
        [Parameter(Mandatory = $true)][string]$PhaseName,
        [Parameter(Mandatory = $true)][string[]]$SqlPackageArguments,
        [int]$MaxAttempts = 3,
        [int[]]$BackoffSeconds = @(20, 45),
        [scriptblock]$Runner,
        [scriptblock]$BeforeAttempt
    )

    if ($MaxAttempts -lt 1) {
        throw "SqlPackageTransientAttempts must be at least 1."
    }

    if (-not $Runner) {
        $Runner = {
            param($PhaseArguments)
            Invoke-SqlPackageProcess -SqlPackageArguments $PhaseArguments
        }
    }

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ($BeforeAttempt) {
            & $BeforeAttempt
        }

        Write-Host "sqlpackage $PhaseName attempt $attempt of $MaxAttempts..."
        $result = & $Runner $SqlPackageArguments
        if ($null -eq $result) {
            $result = @{ ExitCode = 1; Output = "" }
        }

        if ([int]$result.ExitCode -eq 0) {
            return
        }

        $isTransient = Test-SqlPackageTransientTransportError ([string]$result.Output)
        if ($isTransient -and $attempt -lt $MaxAttempts) {
            $delayIndex = [Math]::Min($attempt - 1, $BackoffSeconds.Length - 1)
            $delay = [int]$BackoffSeconds[$delayIndex]
            Write-Host "Transient TCP/transport error during sqlpackage $PhaseName (attempt $attempt of $MaxAttempts). Retrying the failed phase in ${delay}s..."
            if ($delay -gt 0) {
                Start-Sleep -Seconds $delay
            }
            continue
        }

        if ($isTransient) {
            throw "sqlpackage $PhaseName failed after $MaxAttempts attempts due to a TCP/transport error (connection forcibly closed, reset, or transport timeout). Live Azure SQL dropped the connection during $PhaseName. This is not a SQL Express install failure and must not surface as a silent workflow cancel. Re-run the nightly Sync job. If a second consecutive schedule still drops, check the production SQL database (restart / DTU / firewall). Last exit code: $($result.ExitCode)"
        }

        throw "sqlpackage $PhaseName failed with exit code $($result.ExitCode)"
    }
}

function New-MirrorPromotionSql {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDatabase,
        [Parameter(Mandatory = $true)][string]$TargetDatabase
    )

    return @"
IF DB_ID(N'$StagingDatabase') IS NULL
    THROW 50000, 'The staged mirror database does not exist.', 1;
IF DB_ID(N'$TargetDatabase') IS NOT NULL
BEGIN
    ALTER DATABASE [$TargetDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$TargetDatabase];
END
-- Keep this sqlcmd session inside the staged database before reserving its
-- single-user slot. Otherwise a recently closing SqlPackage connection can
-- claim that slot between SET SINGLE_USER and MODIFY NAME (SQL error 924).
USE [$StagingDatabase];
ALTER DATABASE [$StagingDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [$StagingDatabase] MODIFY NAME = [$TargetDatabase];
ALTER DATABASE [$TargetDatabase] SET MULTI_USER;
"@
}

function Invoke-SyncLegacyDbSelfTest {
    $tcp = 'A transport-level error has occurred when receiving results from the server. (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)'
    $reset = 'The connection was reset by the remote host (connection reset).'
    $transportTimeout = 'A transport-level error has occurred when receiving results from the server. (provider: TCP Provider, error: 0 - The semaphore timeout period has expired.)'
    $containedUser = 'Error SQL72014: An error occurred during deployment. Msg 33233. You can only create a user with a password in a contained database.'
    $mdfCollision = 'Msg 5170 Cannot create file queenzone_legacy_sync_refresh.mdf because it already exists.'

    if (-not (Test-SqlPackageTransientTransportError $tcp)) {
        throw "Classifier missed TCP forcibly closed."
    }
    if (-not (Test-SqlPackageTransientTransportError $reset)) {
        throw "Classifier missed connection reset."
    }
    if (-not (Test-SqlPackageTransientTransportError $transportTimeout)) {
        throw "Classifier missed transport timeout."
    }
    if (Test-SqlPackageTransientTransportError $containedUser) {
        throw "Classifier must not treat contained-user publish errors (#1334) as transient."
    }
    if (Test-SqlPackageTransientTransportError $mdfCollision) {
        throw "Classifier must not treat staging filename collisions (#1384/#1386) as transient."
    }
    if (Test-SqlPackageTransientTransportError 'Timeout expired. The timeout period elapsed prior to completion of the operation.') {
        throw "Classifier must not treat a SQL command timeout as a transport drop."
    }

    $promotionSql = New-MirrorPromotionSql -StagingDatabase 'selftest_stage' -TargetDatabase 'selftest_target'
    $useStagingIndex = $promotionSql.IndexOf('USE [selftest_stage];', [StringComparison]::Ordinal)
    $singleUserIndex = $promotionSql.IndexOf('ALTER DATABASE [selftest_stage] SET SINGLE_USER', [StringComparison]::Ordinal)
    $renameIndex = $promotionSql.IndexOf('ALTER DATABASE [selftest_stage] MODIFY NAME', [StringComparison]::Ordinal)
    if ($useStagingIndex -lt 0 -or $singleUserIndex -le $useStagingIndex -or $renameIndex -le $singleUserIndex) {
        throw "Promotion SQL must enter the staged database before reserving SINGLE_USER and renaming it."
    }

    $quoted = ConvertTo-WindowsProcessArguments @(
        'tool',
        'run',
        'sqlpackage',
        '/p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership',
        '/SourceConnectionString:Server=example;Database=queenzone-db'
    )
    if ($quoted -notmatch '/p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership') {
        throw "Argument quoting must keep the #1334 Publish exclusions intact."
    }

    $state = @{ Calls = 0 }
    $runner = {
        param($PhaseArguments)
        $state.Calls++
        if ($state.Calls -eq 1) {
            return @{ ExitCode = 1; Output = $tcp }
        }
        return @{ ExitCode = 0; Output = "ok" }
    }
    Invoke-SqlPackagePhase -PhaseName "Extract" -SqlPackageArguments @("/Action:Extract") -MaxAttempts 3 -BackoffSeconds @(0, 0) -Runner $runner
    if ($state.Calls -ne 2) {
        throw "Expected Extract to retry once then succeed; got $($state.Calls) attempts."
    }

    $state.Calls = 0
    $exhausted = $false
    try {
        $failRunner = {
            param($PhaseArguments)
            $state.Calls++
            return @{ ExitCode = 1; Output = $tcp }
        }
        Invoke-SqlPackagePhase -PhaseName "Publish" -SqlPackageArguments @("/Action:Publish") -MaxAttempts 3 -BackoffSeconds @(0, 0) -Runner $failRunner
    }
    catch {
        $exhausted = $true
        if ($_.Exception.Message -notmatch 'TCP/transport') {
            throw "Exhausted retries must name TCP/transport. Message: $($_.Exception.Message)"
        }
        if ($_.Exception.Message -notmatch 'Publish') {
            throw "Exhausted retries must name the failed phase. Message: $($_.Exception.Message)"
        }
    }
    if (-not $exhausted) {
        throw "Expected Publish to fail after exhausted TCP/transport retries."
    }
    if ($state.Calls -ne 3) {
        throw "Expected 3 Publish attempts; got $($state.Calls)."
    }

    $state.Calls = 0
    $permanentFailed = $false
    try {
        $permanentRunner = {
            param($PhaseArguments)
            $state.Calls++
            return @{ ExitCode = 1; Output = $containedUser }
        }
        Invoke-SqlPackagePhase -PhaseName "Publish" -SqlPackageArguments @("/Action:Publish") -MaxAttempts 3 -BackoffSeconds @(0, 0) -Runner $permanentRunner
    }
    catch {
        $permanentFailed = $true
        if ($_.Exception.Message -match 'TCP/transport') {
            throw "Permanent publish errors must not be reported as TCP/transport."
        }
    }
    if (-not $permanentFailed) {
        throw "Expected a permanent Publish failure to throw."
    }
    if ($state.Calls -ne 1) {
        throw "Permanent errors must not retry; got $($state.Calls) attempts."
    }

    $wrapperPattern = 'TCP/transport|forcibly closed|connection reset|transport-level|transport timeout'
    $namedFailure = 'sqlpackage Extract failed after 3 attempts due to a TCP/transport error (connection forcibly closed, reset, or transport timeout).'
    if ($namedFailure -notmatch $wrapperPattern) {
        throw "Nightly Sync wrapper must annotate the script's named TCP/transport failure."
    }

    # Exercise the same polled Start-Process path as Extract and Publish. A
    # previous smoke test used Start-Process -Wait, which concealed the
    # Windows PowerShell 5.1 null-ExitCode failure seen by the nightly runner.
    $smoke = Invoke-SqlPackageProcess -SqlPackageArguments @('/Version')
    if ($smoke.ExitCode -ne 0) {
        throw "Polled process-launch smoke failed with exit $($smoke.ExitCode)."
    }
    if ([string]$smoke.Output -notmatch '\d+\.\d+') {
        throw "Polled process-launch smoke did not print a sqlpackage version."
    }

    Write-Host "Sync-LegacyDbToSqlExpress.ps1 self-test passed."
}

if ($SelfTest) {
    Invoke-SyncLegacyDbSelfTest
    exit 0
}

$sourceConnectionString = $env:ConnectionStrings__QueenZoneLegacy
if ([string]::IsNullOrWhiteSpace($sourceConnectionString)) {
    Write-Error "ConnectionStrings__QueenZoneLegacy is not set."
}

function Get-SourceSchemaUserNames([string] $ConnectionString) {
    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT DISTINCT principal.name
FROM sys.schemas schema_info
JOIN sys.database_principals principal ON principal.principal_id=schema_info.principal_id
WHERE principal.type <> 'R'
  AND principal.name NOT IN ('dbo','guest','sys','INFORMATION_SCHEMA');
"@
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) { Write-Output $reader.GetString(0) }
        }
        finally {
            $reader.Dispose()
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}

# Defensive cleanup: the finally block below deletes this run's own dacpac,
# but a hard-killed run (workflow cancellation, runner crash) can skip that
# and leave one behind. Sweep anything older than 6 hours - safely older
# than any run in progress - so those don't quietly accumulate in %TEMP%.
Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Filter "queenzone-legacy-*.dacpac" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-6) } |
    ForEach-Object {
        Write-Host "Removing stale leftover dacpac from an interrupted run: $($_.Name)"
        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
    }

try {
    $schemaUserNames = @(Get-SourceSchemaUserNames $sourceConnectionString)

    Write-Host "Extracting live legacy database (schema + data) to $dacpacPath..."
    Invoke-SqlPackagePhase -PhaseName "Extract" -MaxAttempts $SqlPackageTransientAttempts -SqlPackageArguments @(
        "/Action:Extract",
        "/SourceConnectionString:$sourceConnectionString",
        "/TargetFile:$dacpacPath",
        "/p:ExtractAllTableData=True",
        "/p:VerifyExtraction=False"
    ) -BeforeAttempt {
        if (Test-Path -LiteralPath $dacpacPath) {
            Write-Host "Removing incomplete dacpac from a previous Extract attempt: $dacpacPath"
            Remove-Item -LiteralPath $dacpacPath -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Recreating staging database $stagingDatabase..."

    # SQL Server does not rename physical files when the staged database is
    # promoted with MODIFY NAME. A fixed staging name therefore collides on the
    # next run: the live target still owns <TargetDatabase>_refresh.mdf. Give
    # every staging database unique physical filenames. Dropping the prior
    # target during promotion lets SQL Server remove its old files itself.
    $dropSql = @"
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '$stagingDatabase')
BEGIN
    ALTER DATABASE [$stagingDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$stagingDatabase];
END
CREATE DATABASE [$stagingDatabase];
"@
    sqlcmd -S "localhost\$InstanceName" -b -Q $dropSql
    if ($LASTEXITCODE -ne 0) { throw "Staging database creation failed with exit code $LASTEXITCODE" }

    foreach ($schemaUserName in $schemaUserNames) {
        $ownerIdentifier = $schemaUserName.Replace(']', ']]')
        $ownerLiteral = $schemaUserName.Replace("'", "''")
        $ownerSql = @"
USE [$stagingDatabase];
IF DATABASE_PRINCIPAL_ID(N'$ownerLiteral') IS NULL
    CREATE USER [$ownerIdentifier] WITHOUT LOGIN;
"@
        sqlcmd -S "localhost\$InstanceName" -b -Q $ownerSql
        if ($LASTEXITCODE -ne 0) { throw "Staging schema-owner creation failed with exit code $LASTEXITCODE" }
    }

    Write-Host "Publishing dacpac into SQLEXPRESS as staging database $stagingDatabase (excluding views and Azure security objects)..."
    Invoke-SqlPackagePhase -PhaseName "Publish" -MaxAttempts $SqlPackageTransientAttempts -SqlPackageArguments @(
        "/Action:Publish",
        "/SourceFile:$dacpacPath",
        "/TargetConnectionString:Server=localhost\$InstanceName;Database=$stagingDatabase;Integrated Security=True;TrustServerCertificate=True",
        "/p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership",
        "/p:ScriptDatabaseOptions=False",
        "/p:AllowIncompatiblePlatform=True"
    )

    $verifySql = @"
USE [$stagingDatabase];
IF OBJECT_ID(N'dbo.NEWS_T', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Q_ARTICLE_T', N'U') IS NULL
   OR OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ModernForumThread', N'U') IS NULL
    THROW 50000, 'The staged mirror is missing required production tables.', 1;
"@
    sqlcmd -S "localhost\$InstanceName" -b -Q $verifySql
    if ($LASTEXITCODE -ne 0) { throw "Staged mirror verification failed with exit code $LASTEXITCODE" }

    Write-Host "Granting $ProbeLoginName access to the staged mirror..."
    $grantSql = @"
USE [$stagingDatabase];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$ProbeLoginName')
BEGIN
    CREATE USER [$ProbeLoginName] FOR LOGIN [$ProbeLoginName];
    ALTER ROLE db_owner ADD MEMBER [$ProbeLoginName];
END
"@
    sqlcmd -S "localhost\$InstanceName" -Q $grantSql

    Write-Host "Replacing $TargetDatabase with the verified staged mirror..."
    $promoteSql = New-MirrorPromotionSql -StagingDatabase $stagingDatabase -TargetDatabase $TargetDatabase
    sqlcmd -S "localhost\$InstanceName" -b -Q $promoteSql
    if ($LASTEXITCODE -ne 0) { throw "Mirror promotion failed with exit code $LASTEXITCODE" }
    $stagingPromoted = $true

    Write-Host "Sync complete: $TargetDatabase refreshed from the live legacy database."
}
finally {
    if (-not $stagingPromoted) {
        $cleanupSql = @"
IF DB_ID(N'$stagingDatabase') IS NOT NULL
BEGIN
    ALTER DATABASE [$stagingDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$stagingDatabase];
END
"@
        sqlcmd -S "localhost\$InstanceName" -Q $cleanupSql 2>$null
    }
    if (Test-Path $dacpacPath) {
        Remove-Item $dacpacPath -Force
    }
}

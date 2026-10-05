$ErrorActionPreference = "Stop"

# Load function definitions only: the database-sync entry point must never run here.
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot "Sync-LegacyDbToSqlExpress.ps1"), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors.Message -join "`n") }
foreach ($definition in $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

# Run the real self-test groups, with only the external process launch replaced.
function Invoke-SqlPackageProcess {
    param([string[]]$SqlPackageArguments)
    if ($SqlPackageArguments.Count -ne 1 -or $SqlPackageArguments[0] -ne '/Version') {
        throw "Offline self-test attempted a database operation."
    }
    return @{ ExitCode = 0; Output = 'SqlPackage 170.3.93' }
}
Invoke-SyncLegacyDbSelfTest

# Observe retry timing without sleeping. The last configured delay must be reused.
$delays = [System.Collections.Generic.List[int]]::new()
function Start-Sleep {
    param([int]$Seconds)
    $delays.Add($Seconds)
}
Wait-SqlPackageRetry -PhaseName Extract -Attempt 1 -MaxAttempts 5 -BackoffSeconds @(5, 15)
Wait-SqlPackageRetry -PhaseName Extract -Attempt 2 -MaxAttempts 5 -BackoffSeconds @(5, 15)
Wait-SqlPackageRetry -PhaseName Extract -Attempt 4 -MaxAttempts 5 -BackoffSeconds @(5, 15)
Wait-SqlPackageRetry -PhaseName Extract -Attempt 1 -MaxAttempts 2 -BackoffSeconds @(0)
Wait-SqlPackageRetry -PhaseName Extract -Attempt 1 -MaxAttempts 2 -BackoffSeconds @()
if (($delays -join ',') -ne '5,15,15') { throw "Retry delays changed: $($delays -join ',')." }

# Hooks must run before every attempt, including retries, with arguments preserved.
$state = @{ Events = [System.Collections.Generic.List[string]]::new(); Calls = 0 }
$before = { $state.Events.Add('before') }
$runner = {
    param($PhaseArguments)
    if (($PhaseArguments -join ',') -ne '/Action:Extract,/fixture') { throw "Phase arguments changed." }
    $state.Events.Add('run')
    $state.Calls++
    if ($state.Calls -eq 1) { return @{ ExitCode = 1; Output = 'connection reset' } }
    return @{ ExitCode = 0; Output = 'ok' }
}
Invoke-SqlPackagePhase -PhaseName Extract -SqlPackageArguments @('/Action:Extract', '/fixture') -MaxAttempts 2 -BackoffSeconds @(0) -BeforeAttempt $before -Runner $runner
if (($state.Events -join ',') -ne 'before,run,before,run') { throw "Attempt hook order changed." }
Write-Output "Offline database-sync classification, copy helpers, retries, backoff and attempt hooks passed."

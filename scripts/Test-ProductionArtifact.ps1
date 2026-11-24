[CmdletBinding()]
param([Parameter(Mandatory)][string] $PublishDirectory)

$ErrorActionPreference = 'Stop'
$artifact = (Resolve-Path -LiteralPath $PublishDirectory).Path
$forbidden = Get-ChildItem -LiteralPath $artifact -Recurse -File |
    Where-Object { $_.Name -match '^QueenZone\.(Testing|TestHost)(\.|$)' }
if ($forbidden) { throw 'Production artifact contains local/test host files.' }

$deps = Get-Content -LiteralPath (Join-Path $artifact 'QueenZone.Web.deps.json') -Raw | ConvertFrom-Json
if ($deps.libraries.PSObject.Properties.Name -match '^QueenZone\.(Testing|TestHost)/') {
    throw 'Production dependency manifest references local/test host assemblies.'
}

# A wrong environment must fail closed even when all ordinary test settings are valid.
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Get-Command dotnet -CommandType Application).Source
$start.ArgumentList.Add((Join-Path $artifact 'QueenZone.Web.dll'))
$start.WorkingDirectory = $artifact
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Testing'
$start.Environment['DOTNET_ENVIRONMENT'] = 'Testing'
$start.Environment['ASPNETCORE_URLS'] = 'http://127.0.0.1:0'
$process = [System.Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Production artifact did not reject test authentication within 30 seconds.'
    }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -eq 0 -or $output -notmatch 'Test authentication is not included in QueenZone.Web') {
        throw 'Production artifact did not fail at the expected test-authentication boundary.'
    }
    Write-Host 'Production artifact has no test support and rejects the Testing environment.'
}
finally { $process.Dispose() }

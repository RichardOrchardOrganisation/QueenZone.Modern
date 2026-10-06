# Export only code identifiers, outcomes, durations and fixed failure classes.
# Raw TRX messages/stdout/stacks can contain mirror content or credentials and
# must remain local. This script never serializes those fields or input paths.
param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
$reports = @()
if (Test-Path -LiteralPath $ResultsDirectory) {
    foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter "*.trx" -File) {
        $reader = $null
        try {
            $settings = New-Object System.Xml.XmlReaderSettings
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $reader = [System.Xml.XmlReader]::Create($file.FullName, $settings)
            $document = New-Object System.Xml.XmlDocument
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        catch {
            throw "Unable to parse test report safely. No raw report will be uploaded."
        }
        finally {
            if ($null -ne $reader) { $reader.Dispose() }
        }

        $methods = @{}
        foreach ($definition in $document.SelectNodes("//*[local-name()='UnitTest']")) {
            $method = $definition.SelectSingleNode("*[local-name()='TestMethod']")
            if ($null -eq $method) { continue }
            $className = $method.GetAttribute("className").Split(',')[0]
            $methodName = $method.GetAttribute("name")
            # Use source method identity, never parameterized display names.
            if ($className -cmatch '^QueenZone\.[A-Za-z0-9_.]+$' -and
                $methodName -cmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
                $methods[$definition.GetAttribute("id")] = "$className.$methodName"
            }
        }

        foreach ($result in $document.SelectNodes("//*[local-name()='UnitTestResult']")) {
            $identity = $methods[$result.GetAttribute("testId")]
            if ([string]::IsNullOrWhiteSpace($identity)) { $identity = "unknown-test" }
            $outcome = $result.GetAttribute("outcome")
            if ($outcome -cnotmatch '^(Passed|Failed|NotExecuted|Timeout|Error|Inconclusive)$') { $outcome = "Unknown" }
            $duration = [TimeSpan]::Zero
            $null = [TimeSpan]::TryParse($result.GetAttribute("duration"), [ref]$duration)
            $failureClass = $null
            if ($outcome -ne "Passed" -and $outcome -ne "NotExecuted") {
                $failureClass = "TEST_FAILURE"
                $errorInfo = $result.SelectSingleNode(".//*[local-name()='ErrorInfo']")
                if ($null -ne $errorInfo) {
                    $message = $errorInfo.InnerText
                    if ($message -match 'Execution Timeout Expired|SqlException.*\(-2\)') { $failureClass = "SQL_TIMEOUT" }
                    elseif ($message -match 'deadlocked|SqlException.*\(1205\)') { $failureClass = "SQL_DEADLOCK" }
                    elseif ($message -match 'Invalid column name|Invalid object name') { $failureClass = "SQL_SCHEMA" }
                    elseif ($message -match 'strict mode violation') { $failureClass = "STRICT_LOCATOR" }
                }
            }
            $reports += [pscustomobject]@{
                test = $identity
                outcome = $outcome
                durationSeconds = [Math]::Max(0.0, [double]$duration.TotalSeconds)
                failureClass = $failureClass
            }
        }
    }
}

if ($reports.Count -gt 0) {
    $directory = Split-Path -Parent $OutputPath
    $null = New-Item -ItemType Directory -Path $directory -Force
    ConvertTo-Json -Depth 4 -InputObject @($reports) | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Output "Exported $($reports.Count) safe test outcomes."
}
else {
    Write-Output "No completed test results to export."
}

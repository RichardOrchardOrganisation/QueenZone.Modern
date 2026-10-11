<#
Whole-script test (#2211): runs Bootstrap-OpenTofuState.ps1 -WhatIf against a
fake `az` (and `gh`) placed first on PATH. The fakes log every call and fail on
any write verb. The test asserts that there are 0 writes and that the run succeeds.
Run: pwsh -File infra/bootstrap/tests/Test-BootstrapWhatIfNoWrites.ps1
Requires bash (Linux/macOS runners). No Azure or GitHub access.
#>
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$bootstrap = (Resolve-Path (Join-Path $PSScriptRoot "../Bootstrap-OpenTofuState.ps1")).Path
$subscription = "610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d"
$applyPrincipal = "e5e5ea3b-2a6e-4b62-abb8-947e5e66378c"
$failures = [System.Collections.Generic.List[string]]::new()

$fakeAz = @'
#!/usr/bin/env bash
# Fake az: reads only. Any write verb is logged as WRITE and fails.
echo "az $*" >> "$FAKE_LOG"
args=" $* "
case "$args" in
  *" create "*|*" delete "*|*" update "*|*" set "*|*" add "*|*" remove "*|*" reset "*|*" assign "*)
    echo "WRITE az $*" >> "$FAKE_LOG"; echo "fake az: write refused: $*" >&2; exit 97 ;;
esac
case "$args" in
  *" account show "*) echo '{"id":"'"$FAKE_SUB"'","tenantId":"00000000-0000-0000-0000-0000000000aa"}' ;;
  *" ad signed-in-user show "*) echo "00000000-0000-0000-0000-0000000000bb" ;;
  *" ad app list "*)
    if [ "$FAKE_SCENARIO" = "app-missing" ]; then echo '[]';
    else echo '[{"id":"00000000-0000-0000-0000-0000000000cc","appId":"7b466caa-bd0e-4bee-b77f-c9b715fbd36e","displayName":"QueenZone OpenTofu Apply"}]'; fi ;;
  *" ad sp show "*)
    if [ "$FAKE_SCENARIO" = "sp-missing" ]; then echo "ERROR: Resource not found" >&2; exit 3;
    else echo '{"id":"'"$FAKE_APPLY"'","appId":"7b466caa-bd0e-4bee-b77f-c9b715fbd36e"}'; fi ;;
  *" federated-credential list "*)
    echo '[{"name":"github-org-opentofu-apply","issuer":"https://token.actions.githubusercontent.com","subject":"repo:RichardOrchardOrganisation@333232587/QueenZone.Modern@1265145026:environment:opentofu-apply","audiences":["api://AzureADTokenExchange"]}]' ;;
  *" role assignment list "*)
    scope=$(printf '%s\n' "$@" | awk 'p{print;exit} $0=="--scope"{p=1}')
    echo '[{"id":"'"$scope"'/providers/Microsoft.Authorization/roleAssignments/loose","scope":"'"$scope"'","condition":"((!(ActionMatches{'"'"'Microsoft.Authorization/roleAssignments/write'"'"'})))"}]' ;;
  *" role definition list "*) echo '[]' ;;
  *) echo '[]' ;;
esac
'@

$fakeGh = @'
#!/usr/bin/env bash
echo "gh $*" >> "$FAKE_LOG"
case " $* " in
  *" api "*"-X"*|*" api "*"--method"*|*" secret set "*|*" variable set "*) echo "WRITE gh $*" >> "$FAKE_LOG"; exit 97 ;;
esac
echo '{}'
'@

function Invoke-Scenario {
    param([string]$Scenario)

    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "qz-fake-az-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $dir "az") -Value ($fakeAz -replace "`r`n", "`n") -NoNewline
        Set-Content -LiteralPath (Join-Path $dir "gh") -Value ($fakeGh -replace "`r`n", "`n") -NoNewline
        & chmod +x (Join-Path $dir "az") (Join-Path $dir "gh")
        $log = Join-Path $dir "calls.log"
        New-Item -ItemType File -Path $log | Out-Null

        $env:FAKE_LOG = $log
        $env:FAKE_SUB = $subscription
        $env:FAKE_APPLY = $applyPrincipal
        $env:FAKE_SCENARIO = $Scenario
        $oldPath = $env:PATH
        $env:PATH = "$dir$([System.IO.Path]::PathSeparator)$oldPath"
        try {
            $output = & pwsh -NoProfile -File $bootstrap -WhatIf 2>&1 | Out-String
            $exit = $LASTEXITCODE
        }
        finally {
            $env:PATH = $oldPath
        }
        $calls = @(Get-Content -LiteralPath $log)
        return [pscustomobject]@{
            Exit   = $exit
            Output = $output
            Calls  = $calls
            Writes = @($calls | Where-Object { $_ -like "WRITE *" })
        }
    }
    finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-Case {
    param([string]$Name, [bool]$Condition, $Result)
    if (-not $Condition) {
        $failures.Add("$Name`n--- exit $($Result.Exit)`n$($Result.Output)`n--- calls`n$($Result.Calls -join "`n")")
    }
}

$full = Invoke-Scenario "full"
Assert-Case "full -WhatIf makes 0 writes and succeeds" ($full.Writes.Count -eq 0 -and $full.Exit -eq 0) $full
Assert-Case "full -WhatIf reaches the RBAC step and previews delete/create" (
    $full.Output -match [regex]::Escape("OpenTofu Apply principal object ID: $applyPrincipal") -and
    $full.Output -match "roleAssignments/loose condition:" -and
    $full.Output -match "What if: .*Delete RBAC Administrator assignment" -and
    $full.Output -match "What if: .*Create RBAC Administrator constrained") $full
Assert-Case "full -WhatIf lists both resource groups" (
    $full.Output -match "resourceGroups/Queenzone-RG:" -and $full.Output -match "resourceGroups/Queenzone-Dev-RG:") $full

$spMissing = Invoke-Scenario "sp-missing"
Assert-Case "missing service principal: -WhatIf does not throw and makes 0 writes" ($spMissing.Writes.Count -eq 0 -and $spMissing.Exit -eq 0) $spMissing
Assert-Case "missing service principal is reported" (
    $spMissing.Output -match "has no service principal\. It would be created" -and
    $spMissing.Output -match "cannot be previewed") $spMissing

$appMissing = Invoke-Scenario "app-missing"
Assert-Case "missing application: -WhatIf makes 0 writes and succeeds" ($appMissing.Writes.Count -eq 0 -and $appMissing.Exit -eq 0 -and $appMissing.Output -match "What if: .*Create Entra application") $appMissing

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error "FAIL: $_" -ErrorAction Continue }
    exit 1
}
Write-Output "Test-BootstrapWhatIfNoWrites: all cases passed (0 writes)."

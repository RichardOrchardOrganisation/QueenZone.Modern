#Requires -Version 5.1
<#
.SYNOPSIS
  Post-apply checks for an OpenTofu production apply (issue #625).

.DESCRIPTION
  Runs the existing general route suite (Smoke-LiveSite.ps1), then adds the
  infra-specific checks #625 asks for that the general suite does not cover:

    - Direct Azure origin GET /health must return 403 (Cloudflare-only
      ingress; see infra/modules/azure-web and docs/architecture/azure-hosting-plan.md).
    - GET /health/ready must be reachable (status is reported, not hard-gated,
      since readiness can legitimately degrade independently of an infra apply).
    - cdn2.queenzone.org/songfiles/* must return 404 — the documented,
      safety-critical contract that fan-performance audio never leaks through
      the public Worker proxy (AGENTS.md "Media Serving",
      docs/architecture/azure-hosting-plan.md).
    - cdn2.queenzone.org/attachments/* must return 404 — legacy forum
      attachments are streamed by the member-gated app proxy (#1656).
    - cdn.queenzone.org must still be proxied by Cloudflare (CF-Ray header
      present) — checked generically rather than against a specific photo
      blob, which could be deleted later and cause a false failure.

  The Application Insights freshness check is best-effort and never fails the
  script: it is evidence for a human reviewing the apply, not a release gate.

.EXAMPLE
  ./scripts/Test-OpenTofuPostApplySmoke.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://www.queenzone.org",
    [string]$DirectOriginUrl = "https://queenzone-prod.azurewebsites.net",
    [string]$ApplicationInsightsName = "queenzone-prod-ai",
    [string]$ResourceGroup = "Queenzone-RG"
)

$ErrorActionPreference = "Stop"

$failed = 0

# On PS 7+ (pwsh, what the GitHub Actions runner uses), a non-2xx response's
# exception carries $_.Exception.Response as a System.Net.Http.HttpResponseMessage,
# whose .Headers is an HttpResponseHeaders instance -- it does NOT support
# ["Header-Name"] indexer syntax the way the success-path Invoke-WebRequest
# headers (or PS 5.1's WebException/WebHeaderCollection) do. Left as-is, a
# header lookup on the error path silently returns nothing even when the
# header is genuinely present (confirmed the hard way: cdn.queenzone.org's
# CF-Ray check failed post-apply smoke on a real 400 response that a manual
# curl proved DID carry CF-Ray). Normalize to a plain hashtable so downstream
# code can always use ["Header-Name"] regardless of PS edition or code path.
function ConvertTo-HeaderTable {
    param($Headers)

    $table = @{}
    if ($null -eq $Headers) {
        return $table
    }

    if ($Headers -is [System.Net.Http.Headers.HttpHeaders]) {
        foreach ($entry in $Headers) {
            $table[$entry.Key] = ($entry.Value -join ", ")
        }
    }
    else {
        foreach ($name in $Headers.Keys) {
            $table[$name] = $Headers[$name]
        }
    }

    return $table
}

# Invoke-WebRequest throws on a non-2xx response on both PS 5.1 and PS 7+;
# in both cases the thrown exception's Response carries the real status code
# and headers, so a non-2xx response is not itself a probe failure here —
# several of these checks *expect* 403/404.
function Invoke-StatusProbe {
    param([string]$Uri)

    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 30
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Headers    = ConvertTo-HeaderTable -Headers $response.Headers
            Content    = $response.Content
            Error      = $null
        }
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($null -ne $webResponse -and $webResponse.PSObject.Properties.Name -contains "StatusCode") {
            return [pscustomobject]@{
                StatusCode = [int]$webResponse.StatusCode
                Headers    = ConvertTo-HeaderTable -Headers $webResponse.Headers
                Content    = $null
                Error      = $null
            }
        }
        return [pscustomobject]@{
            StatusCode = $null
            Headers    = $null
            Content    = $null
            Error      = $_.Exception.Message
        }
    }
}

Write-Host "== General route suite (Smoke-LiveSite.ps1) =="
& (Join-Path $PSScriptRoot "Smoke-LiveSite.ps1") -BaseUrl $BaseUrl

Write-Host ""
Write-Host "== Direct Azure origin must be blocked =="
$directHealthUrl = "$($DirectOriginUrl.TrimEnd('/'))/health"
$probe = Invoke-StatusProbe -Uri $directHealthUrl
if ($probe.Error) {
    Write-Host "FAIL  $directHealthUrl -> $($probe.Error)"
    $failed++
}
elseif ($probe.StatusCode -eq 403) {
    Write-Host "OK    $directHealthUrl -> HTTP 403 (blocked, as expected)"
}
else {
    Write-Host "FAIL  $directHealthUrl -> HTTP $($probe.StatusCode) (expected 403)"
    $failed++
}

Write-Host ""
Write-Host "== /health/ready reachability =="
$readyUrl = "$($BaseUrl.TrimEnd('/'))/health/ready"
$probe = Invoke-StatusProbe -Uri $readyUrl
if ($probe.Error) {
    Write-Host "FAIL  $readyUrl -> $($probe.Error)"
    $failed++
}
else {
    Write-Host "INFO  $readyUrl -> HTTP $($probe.StatusCode)"
    if ($probe.Content) {
        Write-Host $probe.Content
    }
}

Write-Host ""
Write-Host "== cdn2 songfiles must stay blocked (404) =="
$songfilesUrl = "https://cdn2.queenzone.org/songfiles/probe-object-does-not-need-to-exist"
$probe = Invoke-StatusProbe -Uri $songfilesUrl
if ($probe.Error) {
    Write-Host "FAIL  $songfilesUrl -> $($probe.Error)"
    $failed++
}
elseif ($probe.StatusCode -eq 404) {
    Write-Host "OK    $songfilesUrl -> HTTP 404 (blocked, as expected)"
}
else {
    Write-Host "FAIL  $songfilesUrl -> HTTP $($probe.StatusCode) (expected 404)"
    $failed++
}

Write-Host ""
Write-Host "== cdn2 attachments must stay blocked (404) =="
$attachmentsUrl = "https://cdn2.queenzone.org/attachments/probe-object-does-not-need-to-exist"
$probe = Invoke-StatusProbe -Uri $attachmentsUrl
if ($probe.Error) {
    Write-Host "FAIL  $attachmentsUrl -> $($probe.Error)"
    $failed++
}
elseif ($probe.StatusCode -eq 404) {
    Write-Host "OK    $attachmentsUrl -> HTTP 404 (blocked, as expected)"
}
else {
    Write-Host "FAIL  $attachmentsUrl -> HTTP $($probe.StatusCode) (expected 404)"
    $failed++
}

Write-Host ""
Write-Host "== cdn must still be proxied by Cloudflare =="
$cdnUrl = "https://cdn.queenzone.org/"
$probe = Invoke-StatusProbe -Uri $cdnUrl
if ($probe.Error) {
    Write-Host "FAIL  $cdnUrl -> $($probe.Error)"
    $failed++
}
elseif ($probe.Headers -and $probe.Headers["CF-Ray"]) {
    Write-Host "OK    $cdnUrl -> HTTP $($probe.StatusCode) with CF-Ray present"
}
else {
    Write-Host "FAIL  $cdnUrl -> HTTP $($probe.StatusCode) but no CF-Ray header (not proxied by Cloudflare?)"
    $failed++
}

Write-Host ""
Write-Host "== Application Insights freshness (best-effort, non-blocking) =="
try {
    $az = Get-Command az -ErrorAction Stop
    $query = "requests | where timestamp > ago(15m) | count"
    $result = & $az.Source monitor app-insights query `
        --app $ApplicationInsightsName `
        --resource-group $ResourceGroup `
        --analytics-query $query `
        --output json 2>$null
    if ($LASTEXITCODE -eq 0 -and $result) {
        Write-Host "INFO  Application Insights query succeeded (recent request count):"
        Write-Host $result
    }
    else {
        Write-Host "INFO  Application Insights query did not return a result (non-blocking)."
    }
    # This check is explicitly non-blocking, but the az CLI call above can
    # leave $LASTEXITCODE non-zero. GitHub Actions' pwsh step wrapper checks
    # $LASTEXITCODE at the very end of the script regardless of the script's
    # own control flow, so a stale non-zero value here fails the whole step
    # even after "All OpenTofu post-apply checks passed" -- confirmed the
    # hard way on run 33727676793: every check reported OK/INFO, yet the
    # step still failed with exit code 1.
    $global:LASTEXITCODE = 0
}
catch {
    Write-Host "INFO  Skipping Application Insights check: $($_.Exception.Message) (non-blocking)."
    $global:LASTEXITCODE = 0
}

if ($failed -gt 0) {
    throw "OpenTofu post-apply smoke failed: $failed check(s) against $BaseUrl / $DirectOriginUrl."
}

Write-Host ""
Write-Host "All OpenTofu post-apply checks passed."

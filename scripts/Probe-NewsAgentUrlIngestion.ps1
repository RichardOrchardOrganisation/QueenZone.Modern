#Requires -Version 5.1
<#
.SYNOPSIS
    Opt-in SQL Express mirror probe for admin URL ingestion (issue #489).

.DESCRIPTION
    Verifies the real SQL-backed run-request queue used by /admin/news-discovery
    "Submit article URL", and optionally runs a full fetch + triage on the local
    machine (same path as Process-NewsAgentRunRequests.ps1).

    Default mode (schema/lifecycle only):
      - Requires ConnectionStrings__QueenZoneLegacy
      - Requires RUN_NEWS_AGENT_URL_INGESTION_PROBE=true
      - Confirms Kind/ArticleUrl/GenerateDraft columns exist
      - Queues, claims, and completes URL ingestion requests without outbound HTTP

    Full mode (-Full):
      - Also sets RUN_NEWS_AGENT_URL_INGESTION_FULL_PROBE=true
      - Fetches a public URL (default https://example.com/?qz-url-ingestion-probe=...)
      - Runs triage via the worker DI stack (OpenRouter when OPENROUTER_API_KEY is set)
      - Creates and then deletes a discovery candidate; never publishes to public /news

    The script refuses any target except the local queenzone_legacy_sync SQL Express
    mirror. Probe rows are deleted before the test returns.

.EXAMPLE
    # Schema + queue only (safe first step after migration deploy)
    $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
    $env:RUN_NEWS_AGENT_URL_INGESTION_PROBE = "true"
    .\scripts\Probe-NewsAgentUrlIngestion.ps1

.EXAMPLE
    # Full fetch + triage on the real stack
    $env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
    $env:RUN_NEWS_AGENT_URL_INGESTION_PROBE = "true"
    $env:OPENROUTER_API_KEY = "<optional but recommended for AI triage>"
    .\scripts\Probe-NewsAgentUrlIngestion.ps1 -Full

.EXAMPLE
    # Full probe against a specific public article (triage only)
    .\scripts\Probe-NewsAgentUrlIngestion.ps1 -Full -ArticleUrl "https://www.queenonline.com/news/some-story"

.EXAMPLE
    # Full probe including draft generation (still never auto-publishes)
    .\scripts\Probe-NewsAgentUrlIngestion.ps1 -Full -GenerateDraft
#>
[CmdletBinding()]
param(
    [switch]$Full,
    [string]$ArticleUrl = "",
    [switch]$GenerateDraft,
    [string]$RunnerId = "",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Require-Env([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        Write-Error "$Name is not set."
    }
    return $value
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download'
}

$null = Require-Env 'ConnectionStrings__QueenZoneLegacy'

if ($env:RUN_NEWS_AGENT_URL_INGESTION_PROBE -ne 'true') {
    Write-Error "Set RUN_NEWS_AGENT_URL_INGESTION_PROBE=true to run live URL ingestion probes against the configured database."
}

& "$PSScriptRoot/Assert-SqlExpressMirrorConnection.ps1" `
    -ConnectionString $env:ConnectionStrings__QueenZoneLegacy

Write-Host "QueenZone News Agent URL ingestion probe"
Write-Host "Configuration: $Configuration"
Write-Host "Full pipeline: $Full"
Write-Host "Generate draft: $GenerateDraft"
Write-Host "Connection string length: $($env:ConnectionStrings__QueenZoneLegacy.Length) (value not printed)"

Write-Step "Schema / queue / claim lifecycle (SQL Express mirror)"
$schemaArgs = @(
    'test',
    'tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj',
    '--configuration', $Configuration,
    '--logger', 'trx;LogFilePrefix=url-ingestion',
    '--results-directory', "$PSScriptRoot/../test-results/legacy-probes",
    '--filter', 'FullyQualifiedName~EfNewsAgentUrlIngestionLiveProbeTests'
)
& dotnet @schemaArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Schema/lifecycle probe failed (exit $LASTEXITCODE). Apply EF migrations if Kind/ArticleUrl columns are missing."
}

if (-not $Full) {
    Write-Host ""
    Write-Host "Schema/lifecycle probe passed." -ForegroundColor Green
    Write-Host "Re-run with -Full to fetch and triage a public URL on the disposable mirror."
    exit 0
}

Write-Step "Full pipeline: queue + fetch + triage (local runner stack)"
$env:RUN_NEWS_AGENT_URL_INGESTION_FULL_PROBE = 'true'

if (-not [string]::IsNullOrWhiteSpace($ArticleUrl)) {
    $env:NEWS_AGENT_URL_INGESTION_PROBE_URL = $ArticleUrl.Trim()
    Write-Host "Article URL: $($env:NEWS_AGENT_URL_INGESTION_PROBE_URL)"
}
else {
    Remove-Item Env:NEWS_AGENT_URL_INGESTION_PROBE_URL -ErrorAction SilentlyContinue
    Write-Host "Article URL: default https://example.com/?qz-url-ingestion-probe=<unique>"
}

if ($GenerateDraft) {
    $env:NEWS_AGENT_URL_INGESTION_PROBE_GENERATE_DRAFT = 'true'
}
else {
    $env:NEWS_AGENT_URL_INGESTION_PROBE_GENERATE_DRAFT = 'false'
}

if (-not [string]::IsNullOrWhiteSpace($RunnerId)) {
    $env:NEWS_AGENT_URL_INGESTION_PROBE_RUNNER_ID = $RunnerId.Trim()
}
else {
    $env:NEWS_AGENT_URL_INGESTION_PROBE_RUNNER_ID = "url-ingestion-probe-$env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($env:OPENROUTER_API_KEY) -and [string]::IsNullOrWhiteSpace($env:OpenRouter__ApiKey)) {
    Write-Host "WARNING: OPENROUTER_API_KEY is not set. Triage will use deterministic checks only (still validates fetch + SQL)." -ForegroundColor Yellow
}

$fullArgs = @(
    'test',
    'tests/QueenZone.NewsAgent.Tests/QueenZone.NewsAgent.Tests.csproj',
    '--configuration', $Configuration,
    '--logger', 'trx;LogFilePrefix=url-ingestion',
    '--results-directory', "$PSScriptRoot/../test-results/legacy-probes",
    '--filter', 'FullyQualifiedName~NewsAgentUrlIngestionLiveProbeTests'
)
& dotnet @fullArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Full URL ingestion probe failed (exit $LASTEXITCODE). Check worker OpenRouter settings, network, and runner logs."
}

Write-Host ""
Write-Host "Full URL ingestion probe passed." -ForegroundColor Green
Write-Host "Probe artifacts were removed; public /news was not changed."
exit 0

[CmdletBinding()]
param(
    [switch]$UseRemoteBackend,
    [ValidateSet("production", "dev")]
    [string[]]$EnvironmentName = @("production", "dev")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$infraPath = Join-Path $repositoryRoot "infra"
$expectedVersion = (Get-Content -LiteralPath (Join-Path $repositoryRoot ".opentofu-version") -Raw).Trim()
$tofu = Get-Command tofu -ErrorAction Stop
$actualVersion = (& $tofu.Source version -json | ConvertFrom-Json).terraform_version
if ($actualVersion -ne $expectedVersion) {
    throw "OpenTofu $expectedVersion is required; found $actualVersion."
}

& (Join-Path $PSScriptRoot "Test-AzureMigrationTargetCapacity.ps1") -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "Azure migration target capacity self-test failed."
}

& (Join-Path $PSScriptRoot "Test-OpenTofuPostApplySmoke.ps1") -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "OpenTofu post-apply smoke self-test failed."
}

& (Join-Path $PSScriptRoot "Test-OpenTofuContainerPublicAccessDrift.ps1") -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "OpenTofu container publicAccess drift self-test failed."
}

& $tofu.Source fmt -check -recursive $infraPath
if ($LASTEXITCODE -ne 0) {
    throw "OpenTofu formatting failed."
}

& (Join-Path $PSScriptRoot "Test-OpenTofuSafety.ps1") -InfraPath $infraPath
if ($LASTEXITCODE -ne 0) {
    throw "OpenTofu safety checks failed."
}

foreach ($rootName in $EnvironmentName) {
    $rootPath = Join-Path $infraPath "environments/$rootName"
    $backendConfig = Join-Path $infraPath "backend/$rootName.backend.hcl"
    $initArguments = @("-chdir=$rootPath", "init", "-input=false", "-reconfigure")
    if ($UseRemoteBackend) {
        $initArguments += "-backend-config=$backendConfig"
    }
    else {
        $initArguments += "-backend=false"
    }

    & $tofu.Source @initArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OpenTofu $rootName initialisation failed."
    }

    & $tofu.Source "-chdir=$rootPath" validate
    if ($LASTEXITCODE -ne 0) {
        throw "OpenTofu $rootName validation failed."
    }

}

$webModulePath = Join-Path $infraPath "modules/azure-web"
& $tofu.Source "-chdir=$webModulePath" init -backend=false -input=false
if ($LASTEXITCODE -ne 0) { throw "Azure web module test initialisation failed." }
& $tofu.Source "-chdir=$webModulePath" test
if ($LASTEXITCODE -ne 0) { throw "Azure web module contract tests failed." }

$dataModulePath = Join-Path $infraPath "modules/azure-data"
& $tofu.Source "-chdir=$dataModulePath" init -backend=false -input=false
if ($LASTEXITCODE -ne 0) { throw "Azure data module test initialisation failed." }
& $tofu.Source "-chdir=$dataModulePath" test
if ($LASTEXITCODE -ne 0) { throw "Azure data module contract tests failed." }

$mobileBuildsModulePath = Join-Path $infraPath "modules/azure-mobile-builds"
& $tofu.Source "-chdir=$mobileBuildsModulePath" init -backend=false -input=false
if ($LASTEXITCODE -ne 0) { throw "Azure mobile-builds module test initialisation failed." }
& $tofu.Source "-chdir=$mobileBuildsModulePath" test
if ($LASTEXITCODE -ne 0) { throw "Azure mobile-builds module contract tests failed." }

Write-Output "OpenTofu format, safety, initialisation, and root/module validation checks passed."

$ErrorActionPreference = 'Stop'

$pester = Get-Module -ListAvailable -Name Pester |
    Sort-Object Version -Descending |
    Select-Object -First 1
if (-not $pester -or $pester.Version -lt [version]'5.0.0') {
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    Install-Module -Name Pester -MinimumVersion 5.0.0 -MaximumVersion 5.99.99 -Force -SkipPublisherCheck -Scope CurrentUser
}

Import-Module Pester -MinimumVersion 5.0.0
$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path $PSScriptRoot 'glory11/NewsAgentVersion.Tests.ps1'
$configuration.Run.Exit = $true
$configuration.Output.Verbosity = 'Detailed'
Invoke-Pester -Configuration $configuration

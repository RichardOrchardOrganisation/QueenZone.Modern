#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

BeforeAll {
    . (Join-Path $PSScriptRoot 'NewsAgentVersion.ps1')
    $script:RunNewsAgent = Join-Path $PSScriptRoot 'Run-NewsAgent.ps1'
}

Describe 'QueenZone prod tag compare/select' {
    It 'accepts vYYYY.MM.DD.N' {
        Test-QueenZoneProdTag -Name 'v2026.10.22.1' | Should -BeTrue
    }

    It 'rejects unreleased, semver, and incomplete tags' {
        Test-QueenZoneProdTag -Name 'unreleased' | Should -BeFalse
        Test-QueenZoneProdTag -Name 'v1.2.3' | Should -BeFalse
        Test-QueenZoneProdTag -Name 'v2026.10.22' | Should -BeFalse
        Test-QueenZoneProdTag -Name '' | Should -BeFalse
    }

    It 'orders tags by year, month, day, then N' {
        Compare-QueenZoneProdTag -Left 'v2026.10.22.1' -Right 'v2026.10.22.2' | Should -BeLessThan 0
        Compare-QueenZoneProdTag -Left 'v2026.10.22.2' -Right 'v2026.10.15.1' | Should -BeGreaterThan 0
        Compare-QueenZoneProdTag -Left 'v2026.10.22.1' -Right 'v2026.10.22.1' | Should -Be 0
    }

    It 'rejects compare of a non-prod tag' {
        { Compare-QueenZoneProdTag -Left 'v2026.10.22.1' -Right 'unreleased' } | Should -Throw '*prod tags*'
    }

    It 'selects the latest non-draft, non-prerelease prod release regardless of list order' {
        $releases = @(
            [pscustomobject]@{ tag_name = 'unreleased'; draft = $true; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v1.2.3'; draft = $false; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v2026.10.22.1'; draft = $false; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v2026.10.22.2'; draft = $false; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v2026.10.15.1'; draft = $false; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v2026.11.01.1'; draft = $true; prerelease = $false }
            [pscustomobject]@{ tag_name = 'v2026.11.02.1'; draft = $false; prerelease = $true }
        )

        (Select-LatestQueenZoneProdRelease -Releases $releases).tag_name | Should -Be 'v2026.10.22.2'
    }

    It 'returns nothing when no prod releases exist' {
        $releases = @([pscustomobject]@{ tag_name = 'v1.0.0'; draft = $false })
        Select-LatestQueenZoneProdRelease -Releases $releases | Should -BeNullOrEmpty
    }

    It 'needs an update when installed differs or is missing' {
        Test-NewsAgentNeedsUpdate -InstalledVersion $null -LatestVersion 'v2026.10.22.1' | Should -BeTrue
        Test-NewsAgentNeedsUpdate -InstalledVersion 'v2026.10.15.1' -LatestVersion 'v2026.10.22.1' | Should -BeTrue
        Test-NewsAgentNeedsUpdate -InstalledVersion 'v2026.10.22.1' -LatestVersion 'v2026.10.22.1' | Should -BeFalse
    }

    It 'reads the installed version from current/version.txt' {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $root 'current') | Out-Null
        try {
            Set-Content -LiteralPath (Join-Path (Join-Path $root 'current') 'version.txt') -Value "v2026.10.22.1`n"
            Get-InstalledNewsAgentVersion -InstallRoot $root | Should -Be 'v2026.10.22.1'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }

    It 'falls back to current.txt when the current pointer is missing' {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        try {
            Set-Content -LiteralPath (Join-Path $root 'current.txt') -Value 'v2026.10.15.1'
            Get-InstalledNewsAgentVersion -InstallRoot $root | Should -Be 'v2026.10.15.1'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }

    It 'names the zip newsagent-vYYYY.MM.DD.N.zip' {
        Get-NewsAgentAssetName -Tag 'v2026.10.22.1' | Should -Be 'newsagent-v2026.10.22.1.zip'
    }

    It 'requires the exact newsagent zip name' {
        $directory = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory | Out-Null
        try {
            $assetName = Get-NewsAgentAssetName -Tag 'v2026.10.22.1'
            Set-Content -LiteralPath (Join-Path $directory 'other.zip') -Value 'nope'
            { Resolve-NewsAgentDownloadedZip -Directory $directory -AssetName $assetName } | Should -Throw '*newsagent-v2026.10.22.1.zip*'
            Set-Content -LiteralPath (Join-Path $directory $assetName) -Value 'ok'
            Resolve-NewsAgentDownloadedZip -Directory $directory -AssetName $assetName | Should -Be (Join-Path $directory $assetName)
        }
        finally {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }

    It 'returns only the child exit code when the child writes stdout' {
        $pwsh = (Get-Command pwsh).Source
        $code = Invoke-NewsAgentNativeCommand -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'Write-Output leaked-line; exit 3')
        $code | Should -BeOfType [int]
        $code | Should -Be 3
    }

    It 'resolves the repository from an explicit value or GITHUB_REPOSITORY' {
        Resolve-QueenZoneRepository -Repository 'owner/sample-repo' | Should -Be 'owner/sample-repo'
        $previous = $env:GITHUB_REPOSITORY
        try {
            $env:GITHUB_REPOSITORY = 'owner/from-env'
            Resolve-QueenZoneRepository | Should -Be 'owner/from-env'
        }
        finally {
            if ($null -eq $previous) {
                Remove-Item Env:GITHUB_REPOSITORY
            }
            else {
                $env:GITHUB_REPOSITORY = $previous
            }
        }
    }
}

Describe 'Run-NewsAgent exit-code passthrough' {
    It 'exits 3 when the worker exits 3' {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        $current = Join-Path $root 'current'
        New-Item -ItemType Directory -Path $current | Out-Null
        try {
            Set-Content -LiteralPath (Join-Path $current 'version.txt') -Value 'v2026.10.22.1'
            $worker = Join-Path $current 'QueenZone.NewsAgent.Worker.exe'
            Set-Content -LiteralPath $worker -Value "#!/bin/sh`necho worker-stdout`nexit 3`n" -Encoding ascii
            if (Get-Command chmod -ErrorAction SilentlyContinue) {
                & chmod +x $worker
            }

            & pwsh -NoProfile -File $script:RunNewsAgent -InstallRoot $root -SkipUpdate | Out-Host
            $LASTEXITCODE | Should -Be 3
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }
}

param(
    [Parameter()]
    [string]$Reports,

    [double]$GlobalLineThreshold = 51,

    [double]$ChangedLineThreshold = 70,

    [string]$BaseRef = $env:GITHUB_BASE_REF,

    [string]$HeadRef = "HEAD",

    [switch]$RequireBaseRef,

    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

if (-not $SelfTest -and [string]::IsNullOrWhiteSpace($Reports)) {
    throw "Reports is required unless -SelfTest is specified."
}

function Get-RepoRelativePath {
    param([string]$Path)

    $root = (Get-Location).Path
    if (-not $root.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $root += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [Uri]::new($root)
    $pathUri = [Uri]::new($Path)
    $relativeUri = $rootUri.MakeRelativeUri($pathUri)

    return [Uri]::UnescapeDataString($relativeUri.ToString())
}

function Convert-ToRepoPath {
    param(
        [string]$Path,
        [string[]]$Sources
    )

    $normalizedPath = $Path.Replace('\', [System.IO.Path]::DirectorySeparatorChar).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $candidatePaths = @()

    if ([System.IO.Path]::IsPathRooted($normalizedPath)) {
        $candidatePaths += $normalizedPath
    }
    else {
        foreach ($source in $Sources) {
            $candidatePaths += [System.IO.Path]::GetFullPath((Join-Path $source $normalizedPath))
        }
    }

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath) {
            return (Get-RepoRelativePath -Path $candidatePath)
        }
    }

    return ($Path -replace '\\', '/')
}

function Get-ChangedLines {
    param(
        [string]$BaseRef,
        [string]$HeadRef
    )

    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        if ($RequireBaseRef) { throw "Changed-line coverage requires a base ref." }
        Write-Host "No base ref supplied; skipping changed-line coverage gate."
        return @{}
    }

    $resolvedBaseRef = $BaseRef
    if ($BaseRef -notmatch '^origin/') {
        $remoteRef = "origin/$BaseRef"
        git rev-parse --verify --quiet $remoteRef *> $null
        if ($LASTEXITCODE -eq 0) {
            $resolvedBaseRef = $remoteRef
        }
    }

    git rev-parse --verify --quiet $resolvedBaseRef *> $null
    if ($LASTEXITCODE -ne 0) {
        if ($RequireBaseRef) { throw "Base ref '$BaseRef' is not available locally." }
        Write-Host "Base ref '$BaseRef' is not available locally; skipping changed-line coverage gate."
        return @{}
    }

    if ($RequireBaseRef) {
        git merge-base --is-ancestor $resolvedBaseRef $HeadRef
        if ($LASTEXITCODE -ne 0) {
            throw "Base ref '$resolvedBaseRef' is not an ancestor of '$HeadRef'."
        }
    }

    $diffLines = git diff --unified=0 --no-color "$resolvedBaseRef...$HeadRef" -- '*.cs'
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to calculate changed lines against '$resolvedBaseRef'."
    }

    $changedLines = @{}
    $currentFile = $null

    foreach ($line in $diffLines) {
        if ($line -match '^\+\+\+ b/(.+)$') {
            $currentFile = $Matches[1]
            if (-not $changedLines.ContainsKey($currentFile)) {
                $changedLines[$currentFile] = [System.Collections.Generic.HashSet[int]]::new()
            }
            continue
        }

        if ($null -eq $currentFile) {
            continue
        }

        if ($line -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            $startLine = [int]$Matches[1]
            $lineCount = if ($Matches[2]) { [int]$Matches[2] } else { 1 }

            for ($offset = 0; $offset -lt $lineCount; $offset++) {
                [void]$changedLines[$currentFile].Add($startLine + $offset)
            }
        }
    }

    return $changedLines
}

# Coverlet writes UTF-8 coverage.cobertura.xml under a GUID folder. The TRX
# logger (same TestResults dir) also copies that file into the VSTest
# `_runner_*/In/*` attachment inbox as UTF-16, which contains NUL bytes and
# cannot be loaded as XML. ReportGenerator skips those copies; this gate
# must too, and must never feed .trx or other non-cobertura XML to [xml].
function Read-CoberturaDocument {
    param([string]$Path)

    $fileName = [System.IO.Path]::GetFileName($Path)
    if (-not $fileName.Equals("coverage.cobertura.xml", [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Skipping non-cobertura file: $Path"
        return $null
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -eq 0) {
        Write-Host "Skipping empty coverage file: $Path"
        return $null
    }

    foreach ($byte in $bytes) {
        if ($byte -eq 0) {
            Write-Host "Skipping coverage file with NUL bytes (likely a TRX attachment copy): $Path"
            return $null
        }
    }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) {
        $text = $text.Substring(1)
    }

    try {
        $document = [xml]$text
    }
    catch {
        Write-Host "Skipping unreadable coverage file '${Path}': $($_.Exception.Message)"
        return $null
    }

    if ($null -eq $document.coverage) {
        Write-Host "Skipping XML that is not a Cobertura coverage document: $Path"
        return $null
    }

    return $document
}

function Get-CoberturaDocuments {
    param([string]$ReportsPath)

    $reportFiles = @(Get-ChildItem -Path $ReportsPath -Recurse -Filter "coverage.cobertura.xml" -File)
    $documents = New-Object System.Collections.Generic.List[object]

    foreach ($reportFile in $reportFiles) {
        $document = Read-CoberturaDocument -Path $reportFile.FullName
        if ($null -ne $document) {
            $documents.Add([pscustomobject]@{
                    File = $reportFile
                    Xml  = $document
                }) | Out-Null
        }
    }

    return $documents
}

function New-SampleCoberturaXml {
    param(
        [string]$SourceRoot,
        [string]$FileName = "src/QueenZone.Web/CoverageGateSample.cs"
    )

    return @"
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="1" branch-rate="1" version="1.9" timestamp="1" lines-covered="2" lines-valid="2" branches-covered="0" branches-valid="0">
  <sources>
    <source>$SourceRoot</source>
  </sources>
  <packages>
    <package name="QueenZone.Web" line-rate="1" branch-rate="1" complexity="1">
      <classes>
        <class name="QueenZone.Web.CoverageGateSample" filename="$FileName" line-rate="1" branch-rate="1" complexity="1">
          <lines>
            <line number="10" hits="1" branch="false" />
            <line number="11" hits="1" branch="false" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"@
}

function Invoke-CoverageGateSelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("qz-coverage-gate-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    try {
        $validDir = Join-Path $tempRoot (New-Guid).ToString("D")
        $trxInboxDir = Join-Path $tempRoot "_runnervmgx7h7_2026-08-30_13_18_42/In/runnervmgx7h7"
        $junkDir = Join-Path $tempRoot "not-cobertura"
        New-Item -ItemType Directory -Path $validDir, $trxInboxDir, $junkDir | Out-Null

        $validXml = New-SampleCoberturaXml -SourceRoot $tempRoot
        $validPath = Join-Path $validDir "coverage.cobertura.xml"
        [System.IO.File]::WriteAllText($validPath, $validXml, [System.Text.UTF8Encoding]::new($false))

        $nulCopyPath = Join-Path $trxInboxDir "coverage.cobertura.xml"
        [System.IO.File]::WriteAllText($nulCopyPath, $validXml, [System.Text.Encoding]::Unicode)

        $trxPath = Join-Path $tempRoot "QueenZone.Web.Tests-shard-0.trx"
        [System.IO.File]::WriteAllText($trxPath, "<TestRun></TestRun>", [System.Text.Encoding]::Unicode)

        $wrongRootPath = Join-Path $junkDir "coverage.cobertura.xml"
        [System.IO.File]::WriteAllText($wrongRootPath, "<TestRun></TestRun>", [System.Text.UTF8Encoding]::new($false))

        if ($null -ne (Read-CoberturaDocument -Path $trxPath)) {
            throw "Self-test failed: TRX files must be ignored."
        }

        if ($null -ne (Read-CoberturaDocument -Path $nulCopyPath)) {
            throw "Self-test failed: cobertura copies that contain NUL bytes must be ignored."
        }

        if ($null -ne (Read-CoberturaDocument -Path $wrongRootPath)) {
            throw "Self-test failed: non-cobertura XML named coverage.cobertura.xml must be ignored."
        }

        $validDocument = Read-CoberturaDocument -Path $validPath
        if ($null -eq $validDocument -or $null -eq $validDocument.coverage) {
            throw "Self-test failed: valid UTF-8 cobertura was not loaded."
        }

        $loaded = @(Get-CoberturaDocuments -ReportsPath $tempRoot)
        if ($loaded.Count -ne 1) {
            throw "Self-test failed: expected 1 valid cobertura document, found $($loaded.Count)."
        }

        if ($loaded[0].File.FullName -ne $validPath) {
            throw "Self-test failed: loaded unexpected report '$($loaded[0].File.FullName)'."
        }

        $emptyRoot = Join-Path $tempRoot "empty-only"
        New-Item -ItemType Directory -Path $emptyRoot | Out-Null
        $emptyOnly = @(Get-CoberturaDocuments -ReportsPath $emptyRoot)
        if ($emptyOnly.Count -ne 0) {
            throw "Self-test failed: empty reports directory should yield no documents."
        }

        $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
        if ($null -eq $pwsh) {
            $pwsh = Get-Command powershell
        }

        $gateOutput = & $pwsh.Source -NoProfile -File $PSCommandPath -Reports $tempRoot -GlobalLineThreshold 0 -ChangedLineThreshold 0 -BaseRef "" 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Self-test failed: coverage gate should accept a mixed TestResults dir that still has one valid cobertura. Output:`n$($gateOutput | Out-String)"
        }

        $gateText = @($gateOutput) -join [Environment]::NewLine
        if ($gateText -notmatch 'union of 1 reports') {
            throw "Self-test failed: expected the gate to union exactly one valid report. Output:`n$gateText"
        }

        $requiredBaseOutput = & $pwsh.Source -NoProfile -File $PSCommandPath -Reports $tempRoot -GlobalLineThreshold 0 -ChangedLineThreshold 0 -BaseRef "" -RequireBaseRef 2>&1
        if ($LASTEXITCODE -eq 0 -or (@($requiredBaseOutput) -join [Environment]::NewLine) -notmatch 'Changed-line coverage requires a base ref') {
            throw "Self-test failed: a required base ref must fail closed when missing."
        }

        $emptyOutput = & $pwsh.Source -NoProfile -File $PSCommandPath -Reports $emptyRoot -GlobalLineThreshold 0 -ChangedLineThreshold 0 -BaseRef "" 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw "Self-test failed: expected the gate to throw when no valid cobertura exists."
        }

        $emptyText = @($emptyOutput) -join [Environment]::NewLine
        if ($emptyText -notmatch 'No valid Cobertura coverage reports') {
            throw "Self-test failed: empty reports dir produced unexpected error. Output:`n$emptyText"
        }

        Write-Host "Test-CoverageGate.ps1 self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest) {
    Invoke-CoverageGateSelfTest
    exit 0
}

$loadedReports = @(Get-CoberturaDocuments -ReportsPath $Reports)
if ($loadedReports.Count -eq 0) {
    throw "No valid Cobertura coverage reports found under '$Reports'."
}

$reportFiles = @($loadedReports | ForEach-Object { $_.File })

# Merge line hits across reports by file path. Do NOT sum each report's lines-valid /
# lines-covered: coverlet emits overlapping assemblies (e.g. QueenZone.Data from both
# Web.Tests and NewsAgent.Tests), and summing double-counts those lines and understates
# global coverage when a sparse report includes a large shared surface.
$coveredLinesByFile = @{}

foreach ($loadedReport in $loadedReports) {
    $coverage = $loadedReport.Xml

    $sources = @($coverage.coverage.sources.source | ForEach-Object {
        if ($_ -is [string]) {
            $_
        }
        elseif ($_.InnerText) {
            $_.InnerText
        }
        else {
            $_.'#text'
        }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($sources.Count -eq 0) {
        $sources = @((Get-Location).Path)
    }

    foreach ($class in $coverage.coverage.packages.package.classes.class) {
        $repoPath = Convert-ToRepoPath -Path $class.filename -Sources $sources

        if (-not $coveredLinesByFile.ContainsKey($repoPath)) {
            $coveredLinesByFile[$repoPath] = @{}
        }

        foreach ($line in $class.lines.line) {
            $lineNumber = [int]$line.number
            $hits = [int]$line.hits

            if (-not $coveredLinesByFile[$repoPath].ContainsKey($lineNumber)) {
                $coveredLinesByFile[$repoPath][$lineNumber] = 0
            }

            $coveredLinesByFile[$repoPath][$lineNumber] += $hits
        }
    }
}

$totalLinesValid = 0
$totalLinesCovered = 0
foreach ($fileHits in $coveredLinesByFile.Values) {
    foreach ($hits in $fileHits.Values) {
        $totalLinesValid++
        if ($hits -gt 0) {
            $totalLinesCovered++
        }
    }
}

if ($totalLinesValid -eq 0) {
    throw "Coverage report contains no valid lines."
}

$globalLineCoverage = [math]::Round(($totalLinesCovered / $totalLinesValid) * 100, 2)
Write-Host "Global line coverage: $globalLineCoverage% ($totalLinesCovered/$totalLinesValid) [union of $($reportFiles.Count) reports]"

if ($globalLineCoverage -lt $GlobalLineThreshold) {
    throw "Global line coverage $globalLineCoverage% is below the required $GlobalLineThreshold%."
}

$changedLines = Get-ChangedLines -BaseRef $BaseRef -HeadRef $HeadRef
if ($changedLines.Count -eq 0) {
    Write-Host "No changed C# lines found for patch coverage."
    exit 0
}

$changedCoverableLines = 0
$changedCoveredLines = 0
$uncoveredChangedLines = @()

foreach ($file in $changedLines.Keys) {
    if (-not $coveredLinesByFile.ContainsKey($file)) {
        continue
    }

    foreach ($lineNumber in $changedLines[$file]) {
        if (-not $coveredLinesByFile[$file].ContainsKey($lineNumber)) {
            continue
        }

        $changedCoverableLines++

        if ($coveredLinesByFile[$file][$lineNumber] -gt 0) {
            $changedCoveredLines++
        }
        else {
            $uncoveredChangedLines += "${file}:${lineNumber}"
        }
    }
}

if ($changedCoverableLines -eq 0) {
    Write-Host "Changed C# lines do not overlap coverable lines in the Cobertura report."
    exit 0
}

$changedLineCoverage = [math]::Round(($changedCoveredLines / $changedCoverableLines) * 100, 2)
Write-Host "Changed-line coverage: $changedLineCoverage% ($changedCoveredLines/$changedCoverableLines)"

if ($changedLineCoverage -lt $ChangedLineThreshold) {
    $sample = $uncoveredChangedLines | Select-Object -First 20
    Write-Host "Uncovered changed lines:"
    $sample | ForEach-Object { Write-Host "  $_" }

    if ($uncoveredChangedLines.Count -gt $sample.Count) {
        Write-Host "  ...and $($uncoveredChangedLines.Count - $sample.Count) more."
    }

    throw "Changed-line coverage $changedLineCoverage% is below the required $ChangedLineThreshold%."
}

<#
.SYNOPSIS
  Rank methods by CRAP (Change Risk Anti-Patterns) score from Coverlet Cobertura reports.

.DESCRIPTION
  CRAP(m) = comp(m)^2 * (1 - cov(m))^3 + comp(m)

  comp is the cyclomatic complexity Coverlet records on each <method>, and cov is the
  method's line coverage (0..1). Line hits are unioned across every report, so a method
  exercised by different Web.Tests shards (or by Web.Tests and NewsAgent.Tests) is scored
  on its combined coverage, the same way Test-CoverageGate.ps1 merges global coverage.

  Async and iterator state machines (`Type/<Method>d__N` + MoveNext) are reported under
  their source method name. Lambdas keep a `<Method>::lambda` name so they rank on their own.

  Writes crap-report.csv (every method, highest score first) and crap-summary.md (counts
  plus the top -Top methods) to -OutputDirectory.

  Ratchet (-Baseline): methods are keyed as file|type|method with compiler-generated type
  segments (<>c__DisplayClass…, <M>d__N) dropped; overloads and lambdas keep their highest
  score. A method above -Threshold that is not in the baseline is new debt; a baselined
  method whose score rose by more than 0.1 got worse. Both are listed in the summary and,
  with -Enforce, fail the run. Every -Baseline run also writes a proposed baseline next to
  the report that only lowers or drops entries, so committing it can never hide new debt.
  -WriteBaseline applies that proposal to the -Baseline file (or creates it when missing).

  -FromCsv rebuilds from an earlier crap-report.csv, e.g. one downloaded from the CI
  coverage-report artifact, because a local run without the SQL Server shard does not
  match CI coverage.

.EXAMPLE
  pwsh -File ./scripts/Get-CrapReport.ps1 -Reports ./TestResults -OutputDirectory ./coverage-report/crap

.EXAMPLE
  pwsh -File ./scripts/Get-CrapReport.ps1 -Reports ./TestResults -Baseline ./config/crap-baseline.dotnet.json -Enforce

.EXAMPLE
  pwsh -File ./scripts/Get-CrapReport.ps1 -FromCsv ./coverage-report/crap/crap-report.csv -Baseline ./config/crap-baseline.dotnet.json -WriteBaseline

.EXAMPLE
  pwsh -File ./scripts/Get-CrapReport.ps1 -SelfTest
#>
param(
    [Parameter()]
    [string]$Reports,

    [string]$FromCsv,

    [string]$OutputDirectory = "./coverage-report/crap",

    [double]$Threshold = 30,

    [int]$Top = 20,

    [string]$Baseline,

    [switch]$Enforce,

    [switch]$WriteBaseline,

    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
# Progress lines use Write-Information (not Write-Host) so functions that return rows
# keep a clean pipeline; Continue makes them visible in CI logs.
$InformationPreference = "Continue"

if (-not $SelfTest -and [string]::IsNullOrWhiteSpace($Reports) -and [string]::IsNullOrWhiteSpace($FromCsv)) {
    throw "Reports or FromCsv is required unless -SelfTest is specified."
}

if (($Enforce -or $WriteBaseline) -and [string]::IsNullOrWhiteSpace($Baseline)) {
    throw "-Enforce and -WriteBaseline require -Baseline."
}

# Scores are rounded to 0.1, so a rise within this is rounding, not a regression.
$RatchetTolerance = 0.1

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

# Same loading rules as Test-CoverageGate.ps1: skip the UTF-16 TRX attachment copies
# (NUL bytes), empty files, and anything that is not a Cobertura document.
function Read-CoberturaDocument {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -eq 0 -or [Array]::IndexOf($bytes, [byte]0) -ge 0) {
        return $null
    }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) {
        $text = $text.Substring(1)
    }

    try {
        $document = [xml]$text
    }
    catch {
        Write-Information "Skipping unreadable coverage file '${Path}': $($_.Exception.Message)"
        return $null
    }

    if ($null -eq $document.coverage) {
        return $null
    }

    return $document
}

function Get-CoberturaSources {
    param([xml]$Document)

    $sources = @($Document.coverage.sources.source | ForEach-Object {
            if ($_ -is [string]) { $_ } elseif ($_.InnerText) { $_.InnerText } else { $_.'#text' }
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($sources.Count -eq 0) {
        $sources = @((Get-Location).Path)
    }

    return $sources
}

function Get-MethodIdentity {
    param(
        [string]$ClassName,
        [string]$MethodName,
        [string]$Signature
    )

    # Async lambda state machine: Type/<<Parent>b__N_M>d.MoveNext()
    if ($ClassName -match '^(?<type>.+?)/<<(?<parent>[^>]+)>b__[\d_]+>d(__\d+)?$') {
        return [pscustomobject]@{ Class = $Matches.type; Method = "$($Matches.parent)::lambda"; Key = "$ClassName|$MethodName" }
    }

    # Async / iterator state machine: Type/<Method>d__N.MoveNext()
    if ($ClassName -match '^(?<type>.+?)/<(?<method>[^>]+)>d__\d+$') {
        return [pscustomobject]@{ Class = $Matches.type; Method = $Matches.method; Key = "$ClassName|$MethodName" }
    }

    # Lambda: Type/<>c or Type/<>c__DisplayClassN_M with method <Parent>b__N_M
    if ($ClassName -match '^(?<type>.+?)/<>c') {
        $type = $Matches.type
        if ($MethodName -match '^<(?<parent>[^>]+)>b__') {
            return [pscustomobject]@{ Class = $type; Method = "$($Matches.parent)::lambda"; Key = "$ClassName|$MethodName$Signature" }
        }
    }

    return [pscustomobject]@{ Class = $ClassName; Method = $MethodName; Key = "$ClassName|$MethodName$Signature" }
}

function Get-CrapScore {
    param(
        [double]$Complexity,
        [double]$Coverage
    )

    return [math]::Pow($Complexity, 2) * [math]::Pow(1 - $Coverage, 3) + $Complexity
}

function Get-MethodEntry {
    param(
        [hashtable]$Methods,
        [string]$RepoPath,
        [object]$Identity
    )

    $key = "$RepoPath|$($Identity.Key)"
    if (-not $Methods.ContainsKey($key)) {
        $Methods[$key] = [pscustomobject]@{
            File       = $RepoPath
            Class      = $Identity.Class
            Method     = $Identity.Method
            Complexity = 0
            Lines      = [System.Collections.Generic.HashSet[int]]::new()
            Covered    = [System.Collections.Generic.HashSet[int]]::new()
        }
    }

    return $Methods[$key]
}

# Union line hits into the entry so methods split across shards score on combined coverage.
function Add-MethodCoverage {
    param(
        [object]$Entry,
        [System.Xml.XmlElement]$Method
    )

    $complexity = [int]$Method.complexity
    if ($complexity -gt $Entry.Complexity) { $Entry.Complexity = $complexity }

    foreach ($line in @($Method.lines.line | Where-Object { $null -ne $_ })) {
        $number = [int]$line.number
        $Entry.Lines.Add($number) | Out-Null
        if ([int64]$line.hits -gt 0) { $Entry.Covered.Add($number) | Out-Null }
    }
}

function Add-DocumentMethods {
    param(
        [hashtable]$Methods,
        [xml]$Document
    )

    $sources = Get-CoberturaSources -Document $Document
    foreach ($class in @($Document.coverage.packages.package.classes.class | Where-Object { $null -ne $_ })) {
        $repoPath = Convert-ToRepoPath -Path $class.filename -Sources $sources
        foreach ($method in @($class.methods.method | Where-Object { $null -ne $_ })) {
            $identity = Get-MethodIdentity -ClassName $class.name -MethodName $method.name -Signature $method.signature
            $entry = Get-MethodEntry -Methods $Methods -RepoPath $repoPath -Identity $identity
            Add-MethodCoverage -Entry $entry -Method $method
        }
    }
}

function ConvertTo-CrapRow {
    param([object]$Entry)

    $coverage = $Entry.Covered.Count / $Entry.Lines.Count
    return [pscustomobject]@{
        Crap         = [math]::Round((Get-CrapScore -Complexity $Entry.Complexity -Coverage $coverage), 1)
        Complexity   = $Entry.Complexity
        LineCoverage = [math]::Round($coverage * 100, 1)
        Lines        = $Entry.Lines.Count
        Method       = $Entry.Method
        Class        = $Entry.Class
        File         = $Entry.File
        Line         = ($Entry.Lines | Measure-Object -Minimum).Minimum
    }
}

function Get-CrapRows {
    param([string]$ReportsPath)

    $methods = @{}
    $reportCount = 0

    foreach ($reportFile in @(Get-ChildItem -Path $ReportsPath -Recurse -Filter "coverage.cobertura.xml" -File)) {
        $document = Read-CoberturaDocument -Path $reportFile.FullName
        if ($null -ne $document) {
            $reportCount++
            Add-DocumentMethods -Methods $methods -Document $document
        }
    }

    if ($reportCount -eq 0) {
        throw "No valid Cobertura coverage reports found under '$ReportsPath'."
    }

    Write-Information "Loaded $reportCount Cobertura report(s); $($methods.Count) method(s)."

    return @($methods.Values |
        Where-Object { $_.Lines.Count -gt 0 } |
        ForEach-Object { ConvertTo-CrapRow -Entry $_ } |
        Sort-Object -Property @{ Expression = "Crap"; Descending = $true }, @{ Expression = "Complexity"; Descending = $true }, File, Line)
}

function Write-CrapReport {
    param(
        [object[]]$Rows,
        [string]$OutputDirectory,
        [double]$Threshold,
        [int]$Top
    )

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $csvPath = Join-Path $OutputDirectory "crap-report.csv"
    $Rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding utf8

    $over = @($Rows | Where-Object { $_.Crap -gt $Threshold })
    $uncovered = @($over | Where-Object { $_.LineCoverage -eq 0 })

    $md = [System.Text.StringBuilder]::new()
    [void]$md.AppendLine("## CRAP score (Change Risk Anti-Patterns)")
    [void]$md.AppendLine()
    [void]$md.AppendLine("CRAP = complexity^2 x (1 - line coverage)^3 + complexity. Above $Threshold is high change risk.")
    [void]$md.AppendLine()
    [void]$md.AppendLine("- Methods scored: $($Rows.Count)")
    [void]$md.AppendLine("- Methods above ${Threshold}: $($over.Count) ($($uncovered.Count) with no line coverage)")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| CRAP | Complexity | Line cov | Method | Location |")
    [void]$md.AppendLine("| ---: | ---: | ---: | --- | --- |")
    foreach ($row in @($Rows | Select-Object -First $Top)) {
        $shortClass = ($row.Class -split '\.')[-1]
        [void]$md.AppendLine("| $($row.Crap) | $($row.Complexity) | $($row.LineCoverage)% | ``$shortClass.$($row.Method)`` | $($row.File):$($row.Line) |")
    }

    $mdPath = Join-Path $OutputDirectory "crap-summary.md"
    [System.IO.File]::WriteAllText($mdPath, $md.ToString(), [System.Text.UTF8Encoding]::new($false))

    Write-Information "Methods above CRAP ${Threshold}: $($over.Count) of $($Rows.Count) ($($uncovered.Count) with no line coverage)."
    Write-Information "Wrote $csvPath"
    Write-Information "Wrote $mdPath"
}

function Get-BaselineKey {
    param([object]$Row)

    # Drop compiler-generated nesting (<>c__DisplayClass43_0, <M>d__5): the ordinals shift
    # when unrelated members are added, which would churn the baseline.
    $type = (@($Row.Class -split '/') | Where-Object { -not $_.StartsWith('<') }) -join '/'
    return "$($Row.File)|$type|$($Row.Method)"
}

function Get-MethodScores {
    param([object[]]$Rows)

    $scores = @{}
    foreach ($row in $Rows) {
        $key = Get-BaselineKey -Row $row
        $crap = [double]$row.Crap
        if (-not $scores.ContainsKey($key) -or $crap -gt $scores[$key]) {
            $scores[$key] = $crap
        }
    }

    return $scores
}

function Read-CrapBaseline {
    param([string]$Path)

    $baseline = @{}
    $json = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    foreach ($property in $json.hotspots.PSObject.Properties) {
        $baseline[$property.Name] = [double]$property.Value
    }

    return $baseline
}

function Compare-CrapBaseline {
    param(
        [hashtable]$Scores,
        [hashtable]$Baseline,
        [double]$Threshold
    )

    $result = [pscustomobject]@{
        New      = [System.Collections.Generic.List[object]]::new()
        Worse    = [System.Collections.Generic.List[object]]::new()
        Improved = [System.Collections.Generic.List[object]]::new()
    }

    foreach ($key in ($Scores.Keys | Sort-Object)) {
        $score = $Scores[$key]
        if (-not $Baseline.ContainsKey($key)) {
            if ($score -gt $Threshold) { $result.New.Add([pscustomobject]@{ Key = $key; Score = $score }) }
        }
        elseif ($score -gt $Baseline[$key] + $RatchetTolerance) {
            $result.Worse.Add([pscustomobject]@{ Key = $key; Baseline = $Baseline[$key]; Score = $score })
        }
    }

    foreach ($key in ($Baseline.Keys | Sort-Object)) {
        $score = if ($Scores.ContainsKey($key)) { $Scores[$key] } else { $null }
        if ($null -eq $score -or $score -lt $Baseline[$key] - $RatchetTolerance) {
            $result.Improved.Add([pscustomobject]@{ Key = $key; Baseline = $Baseline[$key]; Score = $score })
        }
    }

    return $result
}

# Ratchet only: keep baselined keys still above the threshold at min(baseline, current) and
# never add a key. With no baseline yet, every method above the threshold is recorded.
function Get-ProposedBaseline {
    param(
        [hashtable]$Scores,
        [hashtable]$Baseline,
        [double]$Threshold
    )

    $proposed = [ordered]@{}
    $keys = if ($null -eq $Baseline) { $Scores.Keys } else { $Baseline.Keys }
    foreach ($key in ($keys | Sort-Object)) {
        if (-not $Scores.ContainsKey($key) -or $Scores[$key] -le $Threshold) { continue }
        $proposed[$key] = if ($null -eq $Baseline) { $Scores[$key] } else { [math]::Min($Baseline[$key], $Scores[$key]) }
    }

    return $proposed
}

function Write-CrapBaseline {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Hotspots,
        [double]$Threshold,
        [string]$Path
    )

    $document = [ordered]@{
        description = "CRAP ratchet baseline. Methods above the threshold that already existed. Lower or remove entries; never raise by hand. See AGENTS.md (Change risk)."
        threshold   = if ($Threshold -eq [math]::Floor($Threshold)) { [int]$Threshold } else { $Threshold }
        hotspots    = $Hotspots
    }
    $json = ($document | ConvertTo-Json -Depth 4) + "`n"
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Format-RatchetMarkdown {
    param(
        [object]$Comparison,
        [string]$BaselinePath,
        [double]$Threshold
    )

    $md = [System.Text.StringBuilder]::new()
    [void]$md.AppendLine()
    [void]$md.AppendLine("### CRAP ratchet (``$BaselinePath``)")
    [void]$md.AppendLine()
    if ($Comparison.New.Count -eq 0 -and $Comparison.Worse.Count -eq 0) {
        [void]$md.AppendLine("No new or worsened hotspots above $Threshold.")
    }
    foreach ($item in $Comparison.New) {
        [void]$md.AppendLine("- **New hotspot:** ``$($item.Key)`` scores $($item.Score). Add tests or split it until it is at most $Threshold.")
    }
    foreach ($item in $Comparison.Worse) {
        [void]$md.AppendLine("- **Worse:** ``$($item.Key)`` rose from $($item.Baseline) to $($item.Score).")
    }
    if ($Comparison.Improved.Count -gt 0) {
        [void]$md.AppendLine()
        [void]$md.AppendLine("$($Comparison.Improved.Count) baselined hotspot(s) improved or were removed. Commit ``crap-baseline.proposed.json`` from this report as ``$BaselinePath`` to lock in the gain.")
    }

    return $md.ToString()
}

function Invoke-CrapRatchet {
    param(
        [object[]]$Rows,
        [string]$BaselinePath,
        [string]$OutputDirectory,
        [double]$Threshold
    )

    $scores = Get-MethodScores -Rows $Rows
    $hasBaseline = Test-Path -LiteralPath $BaselinePath
    if (-not $hasBaseline -and -not $WriteBaseline) {
        throw "CRAP baseline '$BaselinePath' does not exist. Create it with -WriteBaseline."
    }

    $existing = if ($hasBaseline) { Read-CrapBaseline -Path $BaselinePath } else { $null }
    $proposed = Get-ProposedBaseline -Scores $scores -Baseline $existing -Threshold $Threshold
    Write-CrapBaseline -Hotspots $proposed -Threshold $Threshold -Path (Join-Path $OutputDirectory "crap-baseline.proposed.json")

    if ($WriteBaseline) {
        Write-CrapBaseline -Hotspots $proposed -Threshold $Threshold -Path $BaselinePath
        Write-Information "Wrote $BaselinePath ($($proposed.Count) hotspot(s))."
    }

    # A first -WriteBaseline run compares against what it just recorded, not an empty list.
    $compareTo = if ($hasBaseline) { $existing } else { @{} + $proposed }
    $comparison = Compare-CrapBaseline -Scores $scores -Baseline $compareTo -Threshold $Threshold
    $markdown = Format-RatchetMarkdown -Comparison $comparison -BaselinePath $BaselinePath -Threshold $Threshold
    Add-Content -LiteralPath (Join-Path $OutputDirectory "crap-summary.md") -Value $markdown -Encoding utf8NoBOM
    Write-Information $markdown

    return $comparison
}

function Import-CrapCsv {
    param([string]$Path)

    return @(Import-Csv -LiteralPath $Path | ForEach-Object {
            [pscustomobject]@{
                Crap         = [double]$_.Crap
                Complexity   = [int]$_.Complexity
                LineCoverage = [double]$_.LineCoverage
                Lines        = [int]$_.Lines
                Method       = $_.Method
                Class        = $_.Class
                File         = $_.File
                Line         = [int]$_.Line
            }
        })
}

function New-SelfTestRow {
    param([double]$Crap, [string]$Method, [string]$Class = "QueenZone.Web.Sample", [string]$File = "src/Sample.cs")

    return [pscustomobject]@{ Crap = $Crap; Complexity = 1; LineCoverage = 0; Lines = 1; Method = $Method; Class = $Class; File = $File; Line = 1 }
}

function Invoke-RatchetSelfTest {
    param([string]$TempRoot)

    $key = Get-BaselineKey -Row (New-SelfTestRow -Crap 1 -Method "Run::lambda" -Class "QueenZone.Web.Sample/<>c__DisplayClass43_0")
    if ($key -ne "src/Sample.cs|QueenZone.Web.Sample|Run::lambda") {
        throw "Self-test failed: baseline keys must drop compiler-generated type segments (got '$key')."
    }

    $baseline = @{
        "src/Sample.cs|QueenZone.Web.Sample|Stable"   = 100.0
        "src/Sample.cs|QueenZone.Web.Sample|Worse"    = 40.0
        "src/Sample.cs|QueenZone.Web.Sample|Better"   = 90.0
        "src/Sample.cs|QueenZone.Web.Sample|Fixed"    = 50.0
        "src/Sample.cs|QueenZone.Web.Sample|Deleted"  = 60.0
    }
    $rows = @(
        (New-SelfTestRow -Crap 100.05 -Method "Stable"),
        (New-SelfTestRow -Crap 45 -Method "Worse"),
        (New-SelfTestRow -Crap 70 -Method "Better"),
        (New-SelfTestRow -Crap 12 -Method "Fixed"),
        (New-SelfTestRow -Crap 31 -Method "Brand"),
        (New-SelfTestRow -Crap 20 -Method "Small"),
        (New-SelfTestRow -Crap 35 -Method "Overload"),
        (New-SelfTestRow -Crap 33 -Method "Overload")
    )
    $scores = Get-MethodScores -Rows $rows
    if ($scores["src/Sample.cs|QueenZone.Web.Sample|Overload"] -ne 35) {
        throw "Self-test failed: overloads sharing a key must keep the highest score."
    }

    $comparison = Compare-CrapBaseline -Scores $scores -Baseline $baseline -Threshold 30
    $newKeys = @($comparison.New | ForEach-Object { $_.Key.Split('|')[-1] })
    $worseKeys = @($comparison.Worse | ForEach-Object { $_.Key.Split('|')[-1] })
    $improvedKeys = @($comparison.Improved | ForEach-Object { $_.Key.Split('|')[-1] })
    if (($newKeys -join ',') -ne 'Brand,Overload' -or ($worseKeys -join ',') -ne 'Worse' -or ($improvedKeys -join ',') -ne 'Better,Deleted,Fixed') {
        throw "Self-test failed: ratchet comparison (new=$($newKeys -join ','); worse=$($worseKeys -join ','); improved=$($improvedKeys -join ','))."
    }

    $proposed = Get-ProposedBaseline -Scores $scores -Baseline $baseline -Threshold 30
    $proposedText = ($proposed.GetEnumerator() | ForEach-Object { "$($_.Key.Split('|')[-1])=$($_.Value)" }) -join ','
    if ($proposedText -ne 'Better=70,Stable=100,Worse=40') {
        throw "Self-test failed: the proposed baseline must only lower or drop entries and never add new ones (got '$proposedText')."
    }

    $bootstrap = Get-ProposedBaseline -Scores $scores -Baseline $null -Threshold 30
    if ($bootstrap.Count -ne 5) {
        throw "Self-test failed: a first baseline must record every method above the threshold."
    }

    $baselinePath = Join-Path $TempRoot "baseline.json"
    Write-CrapBaseline -Hotspots $proposed -Threshold 30 -Path $baselinePath
    $roundTrip = Read-CrapBaseline -Path $baselinePath
    if ($roundTrip.Count -ne 3 -or $roundTrip["src/Sample.cs|QueenZone.Web.Sample|Better"] -ne 70) {
        throw "Self-test failed: baseline JSON did not round-trip."
    }
}

function Invoke-CrapReportSelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("crap-report-selftest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    try {
        # Two shards each cover half of Complex.Run; merged it is 4/4 lines.
        $shard = {
            param([string]$RunHits)
            $hits = $RunHits -split ','
            return @"
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0" branch-rate="0" version="1.9" timestamp="1">
  <sources><source>$tempRoot</source></sources>
  <packages>
    <package name="QueenZone.Web">
      <classes>
        <class name="QueenZone.Web.Complex" filename="src/Complex.cs">
          <methods>
            <method name="Run" signature="(System.Int32)" complexity="10">
              <lines>
                <line number="10" hits="$($hits[0])" /><line number="11" hits="$($hits[1])" />
                <line number="12" hits="$($hits[2])" /><line number="13" hits="$($hits[3])" />
              </lines>
            </method>
            <method name="Untested" signature="()" complexity="6">
              <lines><line number="20" hits="0" /><line number="21" hits="0" /></lines>
            </method>
          </methods>
        </class>
        <class name="QueenZone.Web.Complex/&lt;LoadAsync&gt;d__4" filename="src/Complex.cs">
          <methods>
            <method name="MoveNext" signature="()" complexity="4">
              <lines><line number="30" hits="1" /><line number="31" hits="0" /></lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"@
        }

        foreach ($pair in @(@("a", "1,1,0,0"), @("b", "0,0,1,1"))) {
            $dir = Join-Path $tempRoot $pair[0]
            New-Item -ItemType Directory -Path $dir | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $dir "coverage.cobertura.xml"), (& $shard $pair[1]), [System.Text.UTF8Encoding]::new($false))
        }

        # TRX attachment copies are UTF-16 and must be ignored, not double-counted.
        $trxDir = Join-Path $tempRoot "_runner/In/runner"
        New-Item -ItemType Directory -Path $trxDir | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $trxDir "coverage.cobertura.xml"), (& $shard "0,0,0,0"), [System.Text.Encoding]::Unicode)

        $rows = @(Get-CrapRows -ReportsPath $tempRoot)

        $untested = $rows | Where-Object { $_.Method -eq "Untested" }
        if ($null -eq $untested -or $untested.Crap -ne 42 -or $rows[0].Method -ne "Untested") {
            throw "Self-test failed: an uncovered complexity-6 method should score 42 and rank first."
        }

        $run = $rows | Where-Object { $_.Method -eq "Run" }
        if ($null -eq $run -or $run.LineCoverage -ne 100 -or $run.Crap -ne 10) {
            throw "Self-test failed: line hits must be unioned across shards (expected 100% coverage, CRAP 10)."
        }

        $async = $rows | Where-Object { $_.Method -eq "LoadAsync" }
        if ($null -eq $async -or $async.Class -ne "QueenZone.Web.Complex" -or $async.Crap -ne 6) {
            throw "Self-test failed: async state machines should report under the source method (expected CRAP 6)."
        }

        $lambda = Get-MethodIdentity -ClassName "QueenZone.Data.Repo/<>c__DisplayClass5_0" -MethodName "<GetRecentAsync>b__0" -Signature "()"
        $asyncLambda = Get-MethodIdentity -ClassName "Pages_Admin_Index/<<ExecuteAsync>b__15_1>d" -MethodName "MoveNext" -Signature "()"
        if ($lambda.Class -ne "QueenZone.Data.Repo" -or $lambda.Method -ne "GetRecentAsync::lambda" -or
            $asyncLambda.Class -ne "Pages_Admin_Index" -or $asyncLambda.Method -ne "ExecuteAsync::lambda") {
            throw "Self-test failed: lambdas and async lambdas should report under their declaring type and parent method."
        }

        if ((Get-CrapScore -Complexity 5 -Coverage 0) -ne 30 -or (Get-CrapScore -Complexity 30 -Coverage 1) -ne 30) {
            throw "Self-test failed: CRAP formula boundary check."
        }

        $outDir = Join-Path $tempRoot "out"
        Write-CrapReport -Rows $rows -OutputDirectory $outDir -Threshold 30 -Top 5
        $csv = @(Import-Csv (Join-Path $outDir "crap-report.csv"))
        $summary = Get-Content -Raw (Join-Path $outDir "crap-summary.md")
        if ($csv.Count -ne 3 -or $summary -notmatch 'Methods above 30: 1 \(1 with no line coverage\)') {
            throw "Self-test failed: report files did not match the scored rows."
        }

        Invoke-RatchetSelfTest -TempRoot $tempRoot

        Write-Information "Get-CrapReport.ps1 self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest) {
    Invoke-CrapReportSelfTest
    exit 0
}

$rows = if ($FromCsv) { Import-CrapCsv -Path $FromCsv } else { Get-CrapRows -ReportsPath $Reports }
Write-CrapReport -Rows $rows -OutputDirectory $OutputDirectory -Threshold $Threshold -Top $Top

if ($Baseline) {
    $comparison = Invoke-CrapRatchet -Rows $rows -BaselinePath $Baseline -OutputDirectory $OutputDirectory -Threshold $Threshold
    $violations = $comparison.New.Count + $comparison.Worse.Count
    if ($Enforce -and $violations -gt 0) {
        throw "CRAP ratchet failed: $($comparison.New.Count) new and $($comparison.Worse.Count) worsened hotspot(s) above $Threshold. See crap-summary.md."
    }
}

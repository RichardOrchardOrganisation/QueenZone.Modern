$ErrorActionPreference = "Stop"

# Load named pure helpers only. Never execute the probe or Lighthouse entry points.
foreach ($item in @(
    @{ Path = "Get-DevAttachmentEvidence.ps1"; Name = "Get-ProbeRedirectLocation" },
    @{ Path = "Measure-FrontendPerformance.ps1"; Name = "Move-LighthouseReports" }
)) {
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot $item.Path), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($parseErrors.Message -join "`n") }
    $helperName = $item.Name
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $helperName
    }, $true)
    if ($null -eq $definition) { throw "Missing helper $helperName." }
    . ([scriptblock]::Create($definition.Extent.Text))
}

$response = [System.Net.Http.HttpResponseMessage]::new()
try {
    $response.Headers.Location = [Uri]::new("https://example.invalid/redirect")
    $location = Get-ProbeRedirectLocation -WebResponse $response
    if ($location -ne "https://example.invalid/redirect") { throw "Typed headers lost the redirect." }
    $response.Headers.Remove("Location") | Out-Null
    if ($null -ne (Get-ProbeRedirectLocation -WebResponse $response)) { throw "Absent redirect must remain absent." }
}
finally { $response.Dispose() }

$legacy = [pscustomobject]@{ Headers = @{ Location = "/legacy" } }
if ((Get-ProbeRedirectLocation -WebResponse $legacy) -ne "/legacy") { throw "Legacy headers lost the redirect." }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("qz-lighthouse-helper-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    foreach ($suffix in @(".report.json", ".json", "")) {
        $basePath = Join-Path $tempRoot ([guid]::NewGuid().ToString("N"))
        $jsonPath = "$basePath.final.json"
        $htmlPath = "$basePath.final.html"
        [System.IO.File]::WriteAllText("$basePath$suffix", '{"fixture":true}')
        Move-LighthouseReports -BasePath $basePath -JsonPath $jsonPath -HtmlPath $htmlPath -ExitCode 0
        if ((Get-Content -Raw $jsonPath) -ne '{"fixture":true}') { throw "Report content changed for '$suffix'." }
    }
    $basePath = Join-Path $tempRoot "both"
    [System.IO.File]::WriteAllText("$basePath.report.json", '{"fixture":true}')
    [System.IO.File]::WriteAllText("$basePath.report.html", '<html>fixture</html>')
    Move-LighthouseReports -BasePath $basePath -JsonPath "$basePath.final.json" -HtmlPath "$basePath.final.html" -ExitCode 1
    if ((Get-Content -Raw "$basePath.final.html") -ne '<html>fixture</html>') { throw "HTML report content changed." }
    $rejected = $false
    try { Move-LighthouseReports -BasePath "$tempRoot/missing" -JsonPath "$tempRoot/missing.json" -HtmlPath "$tempRoot/missing.html" -ExitCode 7 }
    catch {
        $rejected = $true
        if ($_.Exception.Message -notmatch '\(exit 7\)') { throw }
    }
    if (-not $rejected) { throw "Missing report must fail closed." }
}
finally { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
Write-Output "Redirect and Lighthouse report helper tests passed (8 cases)."

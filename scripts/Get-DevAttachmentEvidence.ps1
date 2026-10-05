#Requires -Version 5.1
<#
.SYNOPSIS
  Dev evidence for #1833: stored absolute attachment URLs and
  app-path download checks.

.DESCRIPTION
  Counts ModernForumPost body/signature rows that contain absolute attachment
  blob URLs, then probes a sample of queenzonedev attachment links through the
  member-gated app paths (never anonymous blob URLs).

  Refuses any connection string whose Initial Catalog is not queenzone-dev-db.
  The direct SQL connection uses ApplicationIntent=ReadOnly and runs SELECT only.
  Signing in and downloading through the app may update auth grants and download counts.

.EXAMPLE
  ./scripts/Get-DevAttachmentEvidence.ps1 -SelfTest

.EXAMPLE
  $env:ConnectionStrings__QueenZoneLegacy = "<queenzone-dev-db>"
  ./scripts/Get-DevAttachmentEvidence.ps1 -BaseUrl https://dev.queenzone.org
#>
[CmdletBinding(DefaultParameterSetName = "Check")]
param(
    [Parameter(ParameterSetName = "Check")]
    [string]$ConnectionString = $env:ConnectionStrings__QueenZoneLegacy,

    [Parameter(ParameterSetName = "Check")]
    [string]$BaseUrl = "https://dev.queenzone.org",

    [Parameter(ParameterSetName = "Check")]
    [string]$MemberEmail = "member@dev.queenzone.invalid",

    [Parameter(ParameterSetName = "Check")]
    [string]$MemberPassword = $env:DEV_SNAPSHOT_MEMBER_PASSWORD,

    [Parameter(ParameterSetName = "Check")]
    [ValidateRange(1, 10)]
    [int]$SampleSize = 5,

    [Parameter(ParameterSetName = "Check")]
    [switch]$RequireCompleteEvidence,

    [Parameter(Mandatory = $true, ParameterSetName = "SelfTest")]
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ExpectedDevDatabase = "queenzone-dev-db"
$DevStorageAttachmentNeedle = "queenzonedev.blob.core.windows.net/attachments/"
$BlobCoreAttachmentNeedle = "blob.core.windows.net/attachments/"
$QueenZoneOrgAttachmentNeedle = "queenzone.org/attachments/"

function Test-IsAllowedDevSqlDatabase {
    param([string]$InitialCatalog)
    return [string]::Equals($InitialCatalog, $ExpectedDevDatabase, [StringComparison]::OrdinalIgnoreCase)
}

function Get-AbsoluteAttachmentUrlCountsFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [pscustomobject]@{
            DevStorageAccount = 0
            BlobCoreWindowsNet = 0
            QueenZoneOrg = 0
        }
    }

    $lower = $Text.ToLowerInvariant()
    return [pscustomobject]@{
        DevStorageAccount  = [int]($lower.Contains($DevStorageAttachmentNeedle))
        BlobCoreWindowsNet = [int]($lower.Contains($BlobCoreAttachmentNeedle))
        QueenZoneOrg       = [int]($lower.Contains($QueenZoneOrgAttachmentNeedle))
    }
}

function ConvertTo-ReadOnlyDevSqlConnectionString {
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    Add-Type -AssemblyName System.Data
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    if (-not (Test-IsAllowedDevSqlDatabase -InitialCatalog $builder.InitialCatalog)) {
        throw "Refusing SQL catalog '$($builder.InitialCatalog)'. This check is queenzone-dev-db only and must target $ExpectedDevDatabase."
    }

    $builder["ApplicationIntent"] = "ReadOnly"
    $builder["Application Name"] = "QueenZone.DevAttachmentEvidence.ReadOnly"
    return [pscustomobject]@{
        ConnectionString = $builder.ConnectionString
        InitialCatalog   = $builder.InitialCatalog
        DataSource       = $builder.DataSource
    }
}

function Get-SqlConnectionType {
    foreach ($name in @("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient", "System.Data.SqlClient.SqlConnection, System.Data")) {
        $type = [type]::GetType($name, $false)
        if ($null -ne $type) {
            return $type
        }
    }

    try {
        Add-Type -AssemblyName System.Data
    }
    catch {
    }

    $type = [type]::GetType("System.Data.SqlClient.SqlConnection, System.Data", $false)
    if ($null -ne $type) {
        return $type
    }

    throw "Neither Microsoft.Data.SqlClient nor System.Data.SqlClient is available."
}

function Invoke-ReadOnlyScalar {
    param(
        [Parameter(Mandatory = $true)][string]$ConnectionString,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $connectionType = Get-SqlConnectionType
    $connection = [Activator]::CreateInstance($connectionType, @($ConnectionString))
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 60
        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-ReadOnlyRows {
    param(
        [Parameter(Mandatory = $true)][string]$ConnectionString,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $connectionType = Get-SqlConnectionType
    $connection = [Activator]::CreateInstance($connectionType, @($ConnectionString))
    $rows = [System.Collections.Generic.List[object]]::new()
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 60
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $row[$reader.GetName($i)] = $reader.GetValue($i)
                }
                $rows.Add([pscustomobject]$row)
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }

    return @($rows)
}

function Get-ProbeRedirectLocation {
    param($WebResponse)

    $location = $null
    if ($webResponse.Headers -is [System.Net.Http.Headers.HttpHeaders]) {
        $values = $null
        if ($webResponse.Headers.TryGetValues("Location", [ref]$values)) {
            $location = [string](@($values)[0])
        }
    }
    elseif ($webResponse.Headers -and $webResponse.Headers["Location"]) {
        $location = [string]$webResponse.Headers["Location"]
    }
    return $location
}

function Invoke-HttpProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session,
        [hashtable]$Headers,
        [int]$TimeoutSec = 30
    )

    try {
        $params = @{
            Uri             = $Uri
            Method          = "GET"
            UseBasicParsing = $true
            TimeoutSec      = $TimeoutSec
            MaximumRedirection = 0
        }
        if ($null -ne $Session) {
            $params.WebSession = $Session
        }
        if ($Headers) {
            $params.Headers = $Headers
        }
        $response = Invoke-WebRequest @params
        return [pscustomobject]@{
            Uri        = $Uri
            StatusCode = [int]$response.StatusCode
            Location   = [string]$response.Headers["Location"]
            Disposition = [string]$response.Headers["Content-Disposition"]
            Error      = $null
        }
    }
    catch {
        $webResponse = $null
        if ($_.Exception.PSObject.Properties.Name -contains "Response") {
            $webResponse = $_.Exception.Response
        }
        if ($null -ne $webResponse -and $webResponse.PSObject.Properties.Name -contains "StatusCode") {
            $location = Get-ProbeRedirectLocation -WebResponse $webResponse
            return [pscustomobject]@{
                Uri        = $Uri
                StatusCode = [int]$webResponse.StatusCode
                Location   = $location
                Disposition = $null
                Error      = $null
            }
        }

        $message = $_.Exception.Message
        if ($message -match "\((\d{3})\)") {
            return [pscustomobject]@{
                Uri        = $Uri
                StatusCode = [int]$Matches[1]
                Location   = $null
                Disposition = $null
                Error      = $null
            }
        }

        return [pscustomobject]@{
            Uri        = $Uri
            StatusCode = $null
            Location   = $null
            Disposition = $null
            Error      = $message
        }
    }
}

function Assert-Probe {
    param(
        [Parameter(Mandatory = $true)]$Probe,
        [Parameter(Mandatory = $true)][int]$ExpectedStatus,
        [switch]$Download
    )

    if ($Probe.Error -or $Probe.StatusCode -ne $ExpectedStatus) {
        throw "Attachment probe failed: $($Probe.Uri) returned $($Probe.StatusCode) ($($Probe.Error)); expected $ExpectedStatus."
    }
    if ($Download -and $Probe.Disposition -notmatch '^attachment(?:;|$)') {
        throw "Attachment probe failed: $($Probe.Uri) did not return Content-Disposition: attachment."
    }
}

function Get-AntiforgeryToken {
    param([Parameter(Mandatory = $true)][string]$Html)

    $match = [regex]::Match($Html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) {
        $match = [regex]::Match($Html, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"')
    }
    if (-not $match.Success) {
        throw "Login page did not include an antiforgery token."
    }
    return $match.Groups[1].Value
}

if ($SelfTest) {
    $failures = [System.Collections.Generic.List[string]]::new()

    if (-not (Test-IsAllowedDevSqlDatabase -InitialCatalog $ExpectedDevDatabase)) {
        $failures.Add("Expected queenzone-dev-db to be allowed.")
    }
    foreach ($forbidden in @("queenzone-db", "queenzone_legacy_sync", "queenzone-prod", "")) {
        if (Test-IsAllowedDevSqlDatabase -InitialCatalog $forbidden) {
            $failures.Add("Catalog '$forbidden' must be rejected.")
        }
    }

    $none = Get-AbsoluteAttachmentUrlCountsFromText -Text "<p>see /forum/attachment/legacy/12</p>"
    if ($none.DevStorageAccount -ne 0 -or $none.BlobCoreWindowsNet -ne 0 -or $none.QueenZoneOrg -ne 0) {
        $failures.Add("Expected app-relative attachment paths not to count as absolute blob URLs.")
    }

    $devHit = Get-AbsoluteAttachmentUrlCountsFromText -Text "https://queenzonedev.blob.core.windows.net/attachments/scan.pdf"
    if ($devHit.DevStorageAccount -ne 1 -or $devHit.BlobCoreWindowsNet -ne 1) {
        $failures.Add("Expected a queenzonedev attachments URL to count for queenzonedev storage and blob.core.windows.net.")
    }

    $cdnHit = Get-AbsoluteAttachmentUrlCountsFromText -Text "https://cdn2.queenzone.org/attachments/scan.pdf"
    if ($cdnHit.QueenZoneOrg -ne 1 -or $cdnHit.DevStorageAccount -ne 0) {
        $failures.Add("Expected a queenzone.org/attachments URL to count only in the org bucket.")
    }

    try {
        $null = ConvertTo-ReadOnlyDevSqlConnectionString -ConnectionString "Server=example;Database=queenzone-db;User ID=x;Password=y"
        $failures.Add("Expected a queenzone-db connection string to be refused.")
    }
    catch {
        if ($_.Exception.Message -notmatch "queenzone-dev-db") {
            $failures.Add("Expected the catalog refusal to name queenzone-dev-db.")
        }
    }

    $allowed = ConvertTo-ReadOnlyDevSqlConnectionString -ConnectionString "Server=example;Database=queenzone-dev-db;User ID=x;Password=y"
    if ($allowed.InitialCatalog -ne $ExpectedDevDatabase -or $allowed.ConnectionString -notmatch "Application\s*Intent") {
        $failures.Add("Expected the queenzone-dev-db catalog to be accepted and marked read-only.")
    }

    try {
        Assert-Probe -Probe ([pscustomobject]@{ Uri = "test"; StatusCode = 404; Error = $null; Disposition = $null }) -ExpectedStatus 200
        $failures.Add("Expected an attachment 404 to fail the evidence check.")
    }
    catch {
    }
    try {
        Assert-Probe -Probe ([pscustomobject]@{ Uri = "test"; StatusCode = 200; Error = $null; Disposition = $null }) -ExpectedStatus 200 -Download
        $failures.Add("Expected a missing download disposition to fail the evidence check.")
    }
    catch {
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Output "Get-DevAttachmentEvidence self-test passed."
    exit 0
}

Write-Host "=== #1833 queenzonedev attachment evidence ==="
Write-Host "BaseUrl: $BaseUrl"

$countResult = $null
$samples = @()

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    if ($RequireCompleteEvidence) {
        throw "A queenzone-dev-db connection is required for complete attachment evidence."
    }
    Write-Host "SQL: skipped (ConnectionStrings__QueenZoneLegacy / -ConnectionString is not set)."
}
else {
    $safe = ConvertTo-ReadOnlyDevSqlConnectionString -ConnectionString $ConnectionString
    Write-Host "SQL catalog: $($safe.InitialCatalog) on $($safe.DataSource) (ApplicationIntent=ReadOnly)"

    $countSql = @"
SELECT
    COUNT_BIG(*) AS PostCount,
    COALESCE(SUM(CASE WHEN BodyHtml LIKE N'%queenzonedev.blob.core.windows.net/attachments/%'
              OR SignatureHtml LIKE N'%queenzonedev.blob.core.windows.net/attachments/%'
             THEN 1 ELSE 0 END), 0) AS DevStorageAccountCount,
    COALESCE(SUM(CASE WHEN BodyHtml LIKE N'%blob.core.windows.net/attachments/%'
              OR SignatureHtml LIKE N'%blob.core.windows.net/attachments/%'
             THEN 1 ELSE 0 END), 0) AS BlobCoreWindowsNetCount,
    COALESCE(SUM(CASE WHEN BodyHtml LIKE N'%queenzone.org/attachments/%'
              OR SignatureHtml LIKE N'%queenzone.org/attachments/%'
             THEN 1 ELSE 0 END), 0) AS QueenZoneOrgCount
FROM ModernForumPost;
"@
    $countRows = @(Invoke-ReadOnlyRows -ConnectionString $safe.ConnectionString -Sql $countSql)
    $countResult = $countRows[0]
    Write-Host ("COUNT queenzonedev storage attachments URLs: {0}" -f $countResult.DevStorageAccountCount)
    Write-Host ("COUNT blob.core.windows.net/attachments/: {0}" -f $countResult.BlobCoreWindowsNetCount)
    Write-Host ("COUNT queenzone.org/attachments/: {0}" -f $countResult.QueenZoneOrgCount)
    Write-Host ("ModernForumPost rows scanned: {0}" -f $countResult.PostCount)
    if ([long]$countResult.DevStorageAccountCount -ne 0 -or
        [long]$countResult.BlobCoreWindowsNetCount -ne 0 -or
        [long]$countResult.QueenZoneOrgCount -ne 0) {
        throw "Stored absolute attachment URLs exist in dev post bodies or signatures; review those rows before changing the container ACL."
    }

    $legacySql = @"
SELECT TOP ($SampleSize)
    LegacyPostId,
    Attachment
FROM ModernForumPost
WHERE Attachment IS NOT NULL
  AND LTRIM(RTRIM(Attachment)) <> N''
  AND IsHidden = 0
ORDER BY LegacyPostId;
"@
    $legacy = @(Invoke-ReadOnlyRows -ConnectionString $safe.ConnectionString -Sql $legacySql)
    foreach ($row in $legacy) {
        $samples += [pscustomobject]@{
            Kind = "legacy"
            Path = "/forum/attachment/legacy/$($row.LegacyPostId)"
            ApiPath = "/api/v1/forum/attachments/legacy/$($row.LegacyPostId)"
        }
    }

    $modernSql = @"
SELECT TOP ($SampleSize)
    a.LegacyPostId,
    a.Id,
    a.BlobPath,
    a.MimeType
FROM ForumPostAttachments AS a
INNER JOIN ModernForumPost AS p ON p.Id = a.PostId
WHERE p.IsHidden = 0
ORDER BY a.UploadedAt DESC;
"@
    $modern = @(Invoke-ReadOnlyRows -ConnectionString $safe.ConnectionString -Sql $modernSql)
    foreach ($row in $modern) {
            $samples += [pscustomobject]@{
                Kind = "modern"
                Path = "/forum/attachment/$($row.LegacyPostId)/$($row.Id)"
                ApiPath = "/api/v1/forum/attachments/$($row.LegacyPostId)/$($row.Id)"
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$row.BlobPath) -and [string]$row.MimeType -like "image/*") {
                $samples += [pscustomobject]@{
                    Kind = "image"
                    Path = "/ugc/forum/$($row.BlobPath.TrimStart('/'))"
                    ApiPath = $null
                }
            }
    }
}

$root = $BaseUrl.TrimEnd("/")
Write-Host ""
Write-Host "=== Signed-out app-path probes ==="

if ($samples.Count -eq 0) {
    if ($RequireCompleteEvidence) {
        throw "No real dev attachment rows were found to probe."
    }
    $samples = @(
        [pscustomobject]@{ Kind = "legacy"; Path = "/forum/attachment/legacy/1002"; ApiPath = "/api/v1/forum/attachments/legacy/1002" }
    )
    Write-Host "INFO  No queenzone-dev-db sample rows; probing the documented sample legacy path only."
}

foreach ($sample in $samples) {
    $probe = Invoke-HttpProbe -Uri ($root + $sample.Path)
    $location = if ($probe.Location) { " Location=$($probe.Location)" } else { "" }
    $errorText = if ($probe.Error) { " error=$($probe.Error)" } else { "" }
    Write-Host ("SIGNED_OUT {0} {1} -> {2}{3}{4}" -f $sample.Kind, $sample.Path, $probe.StatusCode, $location, $errorText)
    if ($sample.Kind -eq "image") {
        Assert-Probe -Probe $probe -ExpectedStatus 200
    }
    else {
        Assert-Probe -Probe $probe -ExpectedStatus 302
        if ($probe.Location -notmatch "/account/login") {
            throw "Signed-out attachment route did not redirect to /account/login: $($sample.Path)"
        }
    }

    if ($sample.ApiPath) {
        $api = Invoke-HttpProbe -Uri ($root + $sample.ApiPath)
        $apiError = if ($api.Error) { " error=$($api.Error)" } else { "" }
        Write-Host ("SIGNED_OUT api {0} -> {1}{2}" -f $sample.ApiPath, $api.StatusCode, $apiError)
        Assert-Probe -Probe $api -ExpectedStatus 401
    }
}

if ([string]::IsNullOrWhiteSpace($MemberPassword)) {
    Write-Host ""
    Write-Host "SIGNED_IN: skipped (DEV_SNAPSHOT_MEMBER_PASSWORD / -MemberPassword is not set)."
    if ($RequireCompleteEvidence) {
        throw "A member password is required to verify sampled attachment downloads."
    }
    exit 0
}

Write-Host ""
Write-Host "=== Signed-in app-path probes (cookie) ==="
$session = $null
$login = Invoke-WebRequest -Uri "$root/account/login" -UseBasicParsing -SessionVariable session -TimeoutSec 30
$token = Get-AntiforgeryToken -Html $login.Content
$body = @{
    "__RequestVerificationToken" = $token
    "Input.Email"                = $MemberEmail
    "Input.Password"             = $MemberPassword
}
try {
    $signIn = Invoke-WebRequest -Uri "$root/account/login" -Method POST -Body $body -UseBasicParsing -WebSession $session -MaximumRedirection 0 -TimeoutSec 30
    $signInStatus = [int]$signIn.StatusCode
}
catch {
    $webResponse = $null
    if ($_.Exception.PSObject.Properties.Name -contains "Response") {
        $webResponse = $_.Exception.Response
    }
    if ($null -ne $webResponse -and $webResponse.PSObject.Properties.Name -contains "StatusCode") {
        $signInStatus = [int]$webResponse.StatusCode
    }
    else {
        throw "Member password sign-in failed: $($_.Exception.Message)"
    }
}
Write-Host ("SIGN_IN /account/login -> {0}" -f $signInStatus)
if ($signInStatus -ne 302) {
    throw "Synthetic member sign-in did not redirect after success (HTTP $signInStatus)."
}

foreach ($sample in $samples) {
    $probe = Invoke-HttpProbe -Uri ($root + $sample.Path) -Session $session
    $disposition = if ($probe.Disposition -match '^attachment(?:;|$)') { " Content-Disposition=attachment" } else { "" }
    $errorText = if ($probe.Error) { " error=$($probe.Error)" } else { "" }
    Write-Host ("SIGNED_IN {0} {1} -> {2}{3}{4}" -f $sample.Kind, $sample.Path, $probe.StatusCode, $disposition, $errorText)
    Assert-Probe -Probe $probe -ExpectedStatus 200 -Download:($sample.Kind -ne "image")
}

Write-Host ""
Write-Host "=== Mobile bearer app-path probes ==="
$tokenResponse = Invoke-RestMethod -Uri "$root/api/v1/auth/token" -Method POST `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{ grant_type = "password"; client_id = "queenzone-mobile"; username = $MemberEmail; password = $MemberPassword } `
    -TimeoutSec 30
if ([string]::IsNullOrWhiteSpace([string]$tokenResponse.access_token)) {
    throw "Mobile password grant did not return an access token."
}
$bearerHeaders = @{ Authorization = "Bearer $($tokenResponse.access_token)" }
foreach ($sample in $samples) {
    if (-not $sample.ApiPath) {
        continue
    }
    $probe = Invoke-HttpProbe -Uri ($root + $sample.ApiPath) -Headers $bearerHeaders
    Write-Host ("BEARER {0} -> {1}" -f $sample.ApiPath, $probe.StatusCode)
    Assert-Probe -Probe $probe -ExpectedStatus 200 -Download
}

Write-Host ""
Write-Host "Get-DevAttachmentEvidence passed. The evidence SQL connection issued SELECT only."

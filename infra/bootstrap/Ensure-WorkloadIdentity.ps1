. (Join-Path $PSScriptRoot "Assert-GitHubFederatedCredential.ps1")

function Ensure-WorkloadIdentity {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$FederatedCredentialName,
        [Parameter(Mandatory)][string]$EnvironmentName
    )

    $applications = @(Invoke-AzJson @("ad", "app", "list", "--filter", "displayName eq '$DisplayName'"))
    if ($applications.Count -gt 1) {
        throw "More than one Entra application is named '$DisplayName'. Resolve the duplicate before continuing."
    }

    if ($applications.Count -eq 0) {
        if (-not $PSCmdlet.ShouldProcess($DisplayName, "Create Entra application")) {
            return $null
        }

        $application = Invoke-AzJson @("ad", "app", "create", "--display-name", $DisplayName)
    }
    else {
        $application = $applications[0]
    }

    $servicePrincipal = Try-AzJson @("ad", "sp", "show", "--id", $application.appId)
    if ($null -eq $servicePrincipal) {
        if ($PSCmdlet.ShouldProcess($DisplayName, "Create service principal")) {
            $servicePrincipal = Invoke-AzJson @("ad", "sp", "create", "--id", $application.appId)
        }
    }

    if ($null -eq $servicePrincipal) {
        throw "The service principal for '$DisplayName' was not created."
    }

    $subject = "repo:$OidcRepositorySegment`:environment:$EnvironmentName"
    $credential = @{
        name        = $FederatedCredentialName
        issuer      = "https://token.actions.githubusercontent.com"
        subject     = $subject
        audiences   = @("api://AzureADTokenExchange")
        description = "GitHub environment $EnvironmentName for $GitHubRepository"
    }

    $existingCredentials = @(Invoke-AzJson @("ad", "app", "federated-credential", "list", "--id", $application.id))
    $existingCredential = $existingCredentials | Where-Object { $_.name -eq $FederatedCredentialName } | Select-Object -First 1
    Assert-GitHubFederatedCredential -ExistingCredential $existingCredential -Subject $subject `
        -Issuer $credential.issuer -CredentialName $FederatedCredentialName

    if ($null -eq $existingCredential -and $existingCredentials.Count -ge 20) {
        throw "'$DisplayName' has reached the 20 federated-credential limit."
    }

    if ($null -eq $existingCredential -and $PSCmdlet.ShouldProcess($DisplayName, "Create GitHub OIDC federated credential")) {
        $credentialFile = Join-Path ([System.IO.Path]::GetTempPath()) "$FederatedCredentialName.json"
        try {
            $credential | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $credentialFile -Encoding utf8NoBOM
            $null = Invoke-AzJson @(
                "ad", "app", "federated-credential", "create",
                "--id", $application.id,
                "--parameters", $credentialFile
            )
        }
        finally {
            Remove-Item -LiteralPath $credentialFile -Force -ErrorAction SilentlyContinue
        }
    }

    return [pscustomobject]@{
        ClientId          = $application.appId
        PrincipalObjectId = $servicePrincipal.id
    }
}


function Assert-GitHubFederatedCredential {
    param($ExistingCredential, [string]$Subject, [string]$Issuer, [string]$CredentialName)
    if ($null -ne $ExistingCredential -and
        ($ExistingCredential.subject -ne $Subject -or $ExistingCredential.issuer -ne $Issuer -or
         @($ExistingCredential.audiences).Count -ne 1 -or $ExistingCredential.audiences[0] -ne "api://AzureADTokenExchange")) {
        throw "Federated credential '$CredentialName' exists with a different issuer or subject. Review it before changing trust."
    }
}

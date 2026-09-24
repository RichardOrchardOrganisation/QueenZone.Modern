function Resolve-GitHubOidcRepositorySegment {
    param(
        [Parameter(Mandatory)][string]$GitHubRepository,
        [string]$OidcOwnerId = "",
        [string]$OidcRepositoryId = ""
    )

    if ([bool]$OidcOwnerId -ne [bool]$OidcRepositoryId) {
        throw "Supply both OidcOwnerId and OidcRepositoryId, or neither."
    }
    if ($GitHubRepository -notmatch '^[^/]+/[^/]+$') {
        throw "GitHubRepository must be an owner/repository name."
    }
    if ($OidcOwnerId) {
        if ($OidcOwnerId -notmatch '^[0-9]+$' -or $OidcRepositoryId -notmatch '^[0-9]+$') {
            throw "Immutable OIDC subject requires numeric owner and repository IDs."
        }
        $parts = $GitHubRepository.Split('/')
        return "$($parts[0])@$OidcOwnerId/$($parts[1])@$OidcRepositoryId"
    }
    if ($GitHubRepository -ne "richardorchard/QueenZone.Modern") {
        throw "A different GitHub owner requires the immutable OIDC owner and repository IDs."
    }
    return $GitHubRepository
}

using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed class MobileAuthService(
    MobileAuthAuthorizationSessionStore sessions,
    IMobileAuthGrantRepository grants,
    MobileAuthTokenIssuer tokens,
    MemberAccountService memberAccountService,
    MobileAuthAccountRateLimiter accountRateLimiter,
    IOptions<MobileAuthOptions> options,
    TimeProvider timeProvider,
    ILogger<MobileAuthService> logger)
{
    public MobileAuthStartResult StartAuthorization(
        string? responseType,
        string? clientId,
        string? redirectUri,
        string? codeChallenge,
        string? codeChallengeMethod,
        string? state,
        string? provider)
    {
        var mobile = options.Value;
        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
        {
            return MobileAuthStartResult.Failed("invalid_request", "response_type must be code.");
        }

        if (!string.Equals(clientId, mobile.ClientId, StringComparison.Ordinal))
        {
            return MobileAuthStartResult.Failed("invalid_client", "Unknown client_id.");
        }

        if (!IsRegisteredRedirectUri(mobile, redirectUri))
        {
            return MobileAuthStartResult.Failed(
                "invalid_request",
                "redirect_uri is not registered.",
                redirectSafe: false);
        }

        if (string.IsNullOrWhiteSpace(state) || state.Length > 512)
        {
            return MobileAuthStartResult.Failed("invalid_request", "state is required.", redirectUri, state);
        }

        if (!string.Equals(codeChallengeMethod, MobileAuthPkce.MethodS256, StringComparison.Ordinal)
            || !MobileAuthPkce.IsValidCodeChallenge(codeChallenge))
        {
            return MobileAuthStartResult.Failed(
                "invalid_request",
                "PKCE S256 code_challenge is required.",
                redirectUri,
                state);
        }

        if (!tokens.CanIssueTokens)
        {
            return MobileAuthStartResult.Failed(
                "temporarily_unavailable",
                "Mobile auth is not configured.",
                redirectUri,
                state);
        }

        var normalizedProvider = MemberAuthenticationSchemes.NormalizeExternalProvider(provider);
        if (normalizedProvider is null)
        {
            return MobileAuthStartResult.Failed("invalid_request", "Unknown provider.", redirectUri, state);
        }

        var session = sessions.Create(
            mobile.ClientId,
            redirectUri!,
            codeChallenge!,
            state,
            normalizedProvider,
            TimeSpan.FromMinutes(mobile.AuthorizationCodeLifetimeMinutes));

        return MobileAuthStartResult.Started(session);
    }

    public async Task<MobileAuthCallbackResult> CompleteExternalLoginAsync(
        string? requestId,
        string provider,
        string providerKey,
        string email,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Missing authorization request.");
        }

        var session = sessions.Take(requestId);
        if (session is null)
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Authorization request expired.");
        }

        if (!string.Equals(session.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            return MobileAuthCallbackResult.Failed(
                "access_denied",
                "Provider mismatch.",
                session.RedirectUri,
                session.State);
        }

        var account = await memberAccountService.FindOrCreateFromExternalLoginAsync(
            session.Provider,
            providerKey,
            email,
            displayName,
            cancellationToken);

        if (account.IsSuspended)
        {
            return MobileAuthCallbackResult.Failed(
                "access_denied",
                "account_suspended",
                session.RedirectUri,
                session.State);
        }

        if (!accountRateLimiter.IsAllowed(account.Id))
        {
            return MobileAuthCallbackResult.Failed(
                "temporarily_unavailable",
                MobileAuthAccountRateLimiter.ClientMessage,
                session.RedirectUri,
                session.State);
        }

        var rawCode = MobileAuthPkce.CreateOpaqueToken();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await grants.StoreAuthorizationCodeAsync(
            new MobileAuthAuthorizationCodeEntity
            {
                Id = Guid.NewGuid(),
                CodeHash = MobileAuthPkce.Sha256Hex(rawCode),
                MemberAccountId = account.Id,
                ClientId = session.ClientId,
                RedirectUri = session.RedirectUri,
                CodeChallenge = session.CodeChallenge,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(options.Value.AuthorizationCodeLifetimeMinutes),
            },
            cancellationToken);

        return MobileAuthCallbackResult.Succeeded(session.RedirectUri, session.State, rawCode);
    }

    public async Task<MobileAuthTokenResult> ExchangeAuthorizationCodeAsync(
        string? grantType,
        string? clientId,
        string? redirectUri,
        string? code,
        string? codeVerifier,
        CancellationToken cancellationToken)
    {
        var mobile = options.Value;
        if (!string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
        {
            return MobileAuthTokenResult.Failed("unsupported_grant_type", "grant_type must be authorization_code.");
        }

        if (!string.Equals(clientId, mobile.ClientId, StringComparison.Ordinal)
            || !IsRegisteredRedirectUri(mobile, redirectUri)
            || string.IsNullOrWhiteSpace(code)
            || !MobileAuthPkce.IsValidCodeVerifier(codeVerifier))
        {
            return MobileAuthTokenResult.Failed("invalid_grant", "The authorization code grant is invalid.");
        }

        if (!tokens.CanIssueTokens)
        {
            return MobileAuthTokenResult.Failed("temporarily_unavailable", "Mobile auth is not configured.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await grants.RedeemAuthorizationCodeAsync(
            MobileAuthPkce.Sha256Hex(code),
            now,
            cancellationToken);

        if (stored is null
            || !string.Equals(stored.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(stored.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !MobileAuthPkce.VerifyS256(codeVerifier!, stored.CodeChallenge))
        {
            return MobileAuthTokenResult.Failed("invalid_grant", "The authorization code grant is invalid.");
        }

        var account = await memberAccountService.FindByIdAsync(stored.MemberAccountId, cancellationToken);
        if (account is null || account.IsSuspended)
        {
            return MobileAuthTokenResult.Failed("invalid_grant", "The authorization code grant is invalid.");
        }

        return await IssueTokenPairAsync(account, now, cancellationToken);
    }

    public async Task<MobileAuthTokenResult> ExchangeRefreshTokenAsync(
        string? clientId,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        var mobile = options.Value;
        if (!string.Equals(clientId, mobile.ClientId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        if (!tokens.CanIssueTokens)
        {
            return MobileAuthTokenResult.Failed("temporarily_unavailable", "Mobile auth is not configured.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = MobileAuthPkce.Sha256Hex(refreshToken);
        var stored = await grants.FindRefreshTokenByHashAsync(tokenHash, cancellationToken);
        if (stored is null)
        {
            // Most often a grant issued by a different environment: TestFlight
            // ships staging and production under one bundle id.
            logger.LogInformation(
                "Mobile auth refresh rejected: no grant matches the presented token for client {ClientId}.",
                clientId);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        if (stored.RevokedAt is not null)
        {
            // Refresh-token reuse: the presented grant was already rotated away.
            // Within a short grace window this is usually a client that never saw
            // its rotation response (killed mid launch, dropped connection, ...),
            // not a stolen token — trace forward to whatever grant replaced it and
            // rotate that instead of nuking every device. Outside the window, or
            // when the chain doesn't lead anywhere live, treat it as theft: revoke
            // everything and make that visible.
            var withinGraceWindow = mobile.RefreshTokenReuseGraceSeconds > 0
                && now - stored.RevokedAt.Value <= TimeSpan.FromSeconds(mobile.RefreshTokenReuseGraceSeconds);

            var active = withinGraceWindow
                ? await FindActiveDescendantAsync(stored, clientId!, now, cancellationToken)
                : null;

            if (active is not null)
            {
                logger.LogInformation(
                    "Mobile auth refresh replayed an already-rotated token within the reuse grace window "
                        + "for member {MemberId}; rotating the current grant instead of revoking all grants.",
                    stored.MemberAccountId);

                if (!await grants.TryRevokeRefreshTokenAsync(active.TokenHash, now, cancellationToken))
                {
                    // Lost this rotation race too; let the caller retry.
                    return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
                }

                var recoveredAccount = await memberAccountService.FindByIdAsync(
                    active.MemberAccountId,
                    cancellationToken);
                if (recoveredAccount is null || recoveredAccount.IsSuspended)
                {
                    await grants.RevokeAllRefreshTokensForMemberAsync(active.MemberAccountId, now, cancellationToken);
                    return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
                }

                return await IssueTokenPairAsync(recoveredAccount, now, cancellationToken, active.TokenHash);
            }

            logger.LogWarning(
                "Mobile auth refresh-token reuse detected for member {MemberId}; revoking all grants. "
                    + "Grant issued {CreatedAt:o}, revoked {RevokedAt:o}.",
                stored.MemberAccountId,
                stored.CreatedAt,
                stored.RevokedAt);
            await grants.RevokeAllRefreshTokensForMemberAsync(stored.MemberAccountId, now, cancellationToken);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        if (stored.ExpiresAt <= now
            || !string.Equals(stored.ClientId, clientId, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Mobile auth refresh rejected for member {MemberId}: grant expired {ExpiresAt:o} or client mismatch.",
                stored.MemberAccountId,
                stored.ExpiresAt);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        if (!accountRateLimiter.IsAllowed(stored.MemberAccountId))
        {
            return MobileAuthTokenResult.RateLimited();
        }

        if (!await grants.TryRevokeRefreshTokenAsync(tokenHash, now, cancellationToken))
        {
            // A concurrent refresh rotated this grant between the read above and
            // here. The loser gets invalid_grant and signs out locally.
            logger.LogWarning(
                "Mobile auth refresh lost the rotation race for member {MemberId}; concurrent refresh in flight.",
                stored.MemberAccountId);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        var account = await memberAccountService.FindByIdAsync(stored.MemberAccountId, cancellationToken);
        if (account is null || account.IsSuspended)
        {
            logger.LogInformation(
                "Mobile auth refresh rejected for member {MemberId}: account missing or suspended; revoking all grants.",
                stored.MemberAccountId);
            await grants.RevokeAllRefreshTokensForMemberAsync(stored.MemberAccountId, now, cancellationToken);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        return await IssueTokenPairAsync(account, now, cancellationToken, tokenHash);
    }

    public async Task<MobileAuthTokenResult> ExchangePasswordGrantAsync(
        string? clientId,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var mobile = options.Value;
        if (!string.Equals(clientId, mobile.ClientId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return MobileAuthTokenResult.Failed("invalid_grant", PasswordGrantInvalidDescription);
        }

        if (!tokens.CanIssueTokens)
        {
            return MobileAuthTokenResult.Failed("temporarily_unavailable", "Mobile auth is not configured.");
        }

        var signIn = await memberAccountService.SignInAsync(username, password, cancellationToken);
        if (!signIn.Succeeded || signIn.Account is null)
        {
            return MobileAuthTokenResult.Failed(
                "invalid_grant",
                string.Equals(signIn.Error, MemberAccountService.SuspendedSignInError, StringComparison.Ordinal)
                    ? MemberAccountService.SuspendedSignInError
                    : PasswordGrantInvalidDescription);
        }

        if (!accountRateLimiter.IsAllowed(signIn.Account.Id))
        {
            return MobileAuthTokenResult.RateLimited();
        }

        return await IssueTokenPairAsync(signIn.Account, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public const string PasswordGrantInvalidDescription = "The password grant is invalid.";

    public async Task RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await grants.TryRevokeRefreshTokenAsync(
            MobileAuthPkce.Sha256Hex(refreshToken),
            now,
            cancellationToken);
    }

    public Task<int> RevokeAllRefreshTokensForMemberAsync(
        Guid memberAccountId,
        CancellationToken cancellationToken) =>
        grants.RevokeAllRefreshTokensForMemberAsync(
            memberAccountId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

    private async Task<MobileAuthTokenResult> IssueTokenPairAsync(
        MemberAccount account,
        DateTime utcNow,
        CancellationToken cancellationToken,
        string? rotatedFromTokenHash = null)
    {
        var mobile = options.Value;
        var accessToken = tokens.IssueAccessToken(account.Id, account.Email, account.DisplayName);
        var refreshToken = MobileAuthPkce.CreateOpaqueToken();
        var refreshTokenHash = MobileAuthPkce.Sha256Hex(refreshToken);
        await grants.StoreRefreshTokenAsync(
            new MobileAuthRefreshTokenEntity
            {
                Id = Guid.NewGuid(),
                TokenHash = refreshTokenHash,
                MemberAccountId = account.Id,
                ClientId = mobile.ClientId,
                CreatedAt = utcNow,
                ExpiresAt = utcNow.AddDays(mobile.RefreshTokenLifetimeDays),
            },
            cancellationToken);

        if (rotatedFromTokenHash is not null)
        {
            await grants.LinkRefreshTokenRotationAsync(rotatedFromTokenHash, refreshTokenHash, cancellationToken);
        }

        return MobileAuthTokenResult.Succeeded(
            accessToken,
            refreshToken,
            tokens.AccessTokenLifetimeSeconds);
    }

    /// <summary>
    /// Walks a chain of <see cref="MobileAuthRefreshTokenEntity.ReplacedByTokenHash"/>
    /// pointers forward from an already-rotated token to find the grant that is
    /// still active — the one a client would have received had its rotation
    /// response not been lost. Bounded to guard against an unexpectedly long or
    /// cyclical chain; a real rotation chain within the grace window is one hop.
    /// </summary>
    private async Task<MobileAuthRefreshTokenEntity?> FindActiveDescendantAsync(
        MobileAuthRefreshTokenEntity token,
        string clientId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var next = token.ReplacedByTokenHash;
        for (var hop = 0; hop < MaxReuseChainHops && next is not null; hop++)
        {
            var candidate = await grants.FindRefreshTokenByHashAsync(next, cancellationToken);
            if (candidate is null || !string.Equals(candidate.ClientId, clientId, StringComparison.Ordinal))
            {
                return null;
            }

            if (candidate.RevokedAt is null)
            {
                return candidate.ExpiresAt > utcNow ? candidate : null;
            }

            next = candidate.ReplacedByTokenHash;
        }

        return null;
    }

    private const int MaxReuseChainHops = 5;

    public static bool IsRegisteredRedirectUri(MobileAuthOptions mobile, string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri)
            || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
            || parsed.Scheme is "javascript" or "data" or "file")
        {
            return false;
        }

        return mobile.RedirectUris.Any(allowed =>
            string.Equals(allowed, redirectUri, StringComparison.Ordinal));
    }
}

public sealed record MobileAuthStartResult(
    bool Success,
    string? Error,
    string? ErrorDescription,
    bool RedirectSafe,
    string? RedirectUri,
    string? State,
    MobileAuthAuthorizationSession? Session)
{
    public static MobileAuthStartResult Started(MobileAuthAuthorizationSession session) =>
        new(true, null, null, true, session.RedirectUri, session.State, session);

    public static MobileAuthStartResult Failed(
        string error,
        string description,
        string? redirectUri = null,
        string? state = null,
        bool redirectSafe = true) =>
        new(false, error, description, redirectSafe && redirectUri is not null, redirectUri, state, null);
}

public sealed record MobileAuthCallbackResult(
    bool Success,
    string? Error,
    string? ErrorDescription,
    string? RedirectUri,
    string? State,
    string? Code)
{
    public static MobileAuthCallbackResult Succeeded(string redirectUri, string state, string code) =>
        new(true, null, null, redirectUri, state, code);

    public static MobileAuthCallbackResult Failed(
        string error,
        string description,
        string? redirectUri = null,
        string? state = null) =>
        new(false, error, description, redirectUri, state, null);
}

public sealed record MobileAuthTokenResult(
    bool Success,
    string? Error,
    string? ErrorDescription,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    int StatusCode)
{
    public static MobileAuthTokenResult Succeeded(string accessToken, string refreshToken, int expiresIn) =>
        new(true, null, null, accessToken, refreshToken, expiresIn, StatusCodes.Status200OK);

    public static MobileAuthTokenResult Failed(string error, string description) =>
        new(false, error, description, null, null, 0, StatusCodes.Status400BadRequest);

    public static MobileAuthTokenResult RateLimited() =>
        new(
            false,
            "temporarily_unavailable",
            MobileAuthAccountRateLimiter.ClientMessage,
            null,
            null,
            0,
            StatusCodes.Status429TooManyRequests);
}

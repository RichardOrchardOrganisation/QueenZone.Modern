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
    MobileAuthReplayRecoveryLimiter replayRecoveryLimiter,
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
        bool emailVerified,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Missing authorization request.");
        }

        var session = sessions.Peek(requestId);
        if (session is null)
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Authorization request expired.");
        }

        if (!string.Equals(session.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            sessions.Take(requestId);
            return MobileAuthCallbackResult.Failed(
                "access_denied",
                "Provider mismatch.",
                session.RedirectUri,
                session.State);
        }

        var resolution = await memberAccountService.FindOrCreateFromExternalLoginAsync(
            session.Provider,
            providerKey,
            email,
            displayName,
            emailVerified,
            cancellationToken);

        switch (resolution.Status)
        {
            case ExternalLoginStatus.LinkConfirmationRequired:
                return MobileAuthCallbackResult.ConfirmationRequired(session.RedirectUri, session.State);
            case ExternalLoginStatus.Suspended:
                sessions.Take(requestId);
                return MobileAuthCallbackResult.Failed(
                    "access_denied",
                    "account_suspended",
                    session.RedirectUri,
                    session.State);
            case ExternalLoginStatus.SignedIn when resolution.Account is not null:
                sessions.Take(requestId);
                return await IssueAuthorizationCodeAsync(session, resolution.Account, cancellationToken);
            default:
                sessions.Take(requestId);
                return MobileAuthCallbackResult.Failed(
                    "access_denied",
                    ExternalLoginMessages.UnverifiedEmail,
                    session.RedirectUri,
                    session.State);
        }
    }

    public async Task<MobileAuthCallbackResult> CompleteConfirmedLoginAsync(
        string? requestId,
        MemberAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(requestId))
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Missing authorization request.");
        }

        var session = sessions.Take(requestId);
        if (session is null)
        {
            return MobileAuthCallbackResult.Failed("invalid_request", "Authorization request expired.");
        }

        if (account.IsSuspended)
        {
            return MobileAuthCallbackResult.Failed(
                "access_denied",
                "account_suspended",
                session.RedirectUri,
                session.State);
        }

        return await IssueAuthorizationCodeAsync(session, account, cancellationToken);
    }

    public MobileAuthCallbackResult CancelPendingAuthorization(string? requestId)
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

        return MobileAuthCallbackResult.Failed(
            "access_denied",
            "Sign-in was cancelled.",
            session.RedirectUri,
            session.State);
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
                mobile.ClientId);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        if (stored.RevokedAt is not null)
        {
            // Refresh-token reuse: the presented grant was already rotated away.
            // Usually a client that never saw its rotation response (killed mid
            // launch, suspended in the background, dropped connection, ...), which
            // is recovered by rotating the grant that replaced it. When the chain
            // has moved on without this token's holder, two parties share it:
            // treat it as theft, revoke everything and make that visible.
            var recovered = await TryRecoverReplayedGrantAsync(stored, now, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
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

        var account = await memberAccountService.FindByIdAsync(stored.MemberAccountId, cancellationToken);
        if (account is not MemberAccount liveAccount || RefreshAccountRejected(liveAccount))
        {
            logger.LogInformation(
                "Mobile auth refresh rejected for member {MemberId}: account missing, suspended, or pending deletion; revoking all grants.",
                stored.MemberAccountId);
            await grants.RevokeAllRefreshTokensForMemberAsync(stored.MemberAccountId, now, cancellationToken);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        var rotated = await TryRotateTokenPairAsync(liveAccount, tokenHash, now, cancellationToken);
        if (rotated is not null)
        {
            return rotated;
        }

        // A concurrent refresh rotated this grant between the read above and here.
        // Recover onto the winner's grant like any other lost rotation response;
        // if that isn't possible yet, the grant is not dead, so say retry rather
        // than invalid_grant (which signs the client out).
        var current = await grants.FindRefreshTokenByHashAsync(tokenHash, cancellationToken);
        if (current?.RevokedAt is not null
            && await TryRecoverReplayedGrantAsync(current, now, cancellationToken) is { } recoveredFromRace)
        {
            return recoveredFromRace;
        }

        logger.LogWarning(
            "Mobile auth refresh lost the rotation race for member {MemberId}; concurrent refresh in flight.",
            stored.MemberAccountId);
        return RotationConflict();
    }

    /// <summary>
    /// Recovers a replay of an already-rotated grant by rotating the grant that
    /// replaced it. Within the reuse grace window the chain is followed to its live
    /// end (a double-rotation race). After it, only a direct successor that has
    /// never been used qualifies: nobody advanced the chain, so the legitimate
    /// client almost certainly never received that successor. Returns null when
    /// neither applies, which callers treat as reuse.
    /// </summary>
    private async Task<MobileAuthTokenResult?> TryRecoverReplayedGrantAsync(
        MobileAuthRefreshTokenEntity replayed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var mobile = options.Value;
        var replayAge = now - replayed.RevokedAt!.Value;
        MobileAuthRefreshTokenEntity? active;
        if (mobile.RefreshTokenReuseGraceSeconds > 0
            && replayAge <= TimeSpan.FromSeconds(mobile.RefreshTokenReuseGraceSeconds))
        {
            active = await FindActiveDescendantAsync(replayed, mobile.ClientId, now, cancellationToken);
            if (active is null)
            {
                return null;
            }

            logger.LogInformation(
                "Mobile auth refresh replayed an already-rotated token within the reuse grace window "
                    + "for member {MemberId}; rotating the current grant instead of revoking all grants.",
                replayed.MemberAccountId);
        }
        else if (mobile.RefreshTokenUnusedSuccessorRecovery)
        {
            active = await FindUnusedSuccessorAsync(replayed, mobile.ClientId, now, cancellationToken);
            if (active is null)
            {
                return null;
            }

            if (!replayRecoveryLimiter.TryConsume(replayed.MemberAccountId))
            {
                logger.LogWarning(
                    "Mobile auth refresh for member {MemberId} is over the daily unused-successor recovery limit; "
                        + "treating the replay as reuse.",
                    replayed.MemberAccountId);
                return null;
            }

            // Information, not Debug: a thief and the real client swapping one chain
            // back and forth shows up as a run of these for one member.
            logger.LogInformation(
                "Mobile auth refresh replayed a token rotated {ReplayAgeSeconds}s ago whose successor was never used "
                    + "for member {MemberId}; rotating the unused successor (issued {SuccessorCreatedAt:o}) "
                    + "instead of revoking all grants.",
                (long)replayAge.TotalSeconds,
                replayed.MemberAccountId,
                active.CreatedAt);
        }
        else
        {
            return null;
        }

        var account = await memberAccountService.FindByIdAsync(active.MemberAccountId, cancellationToken);
        if (account is not MemberAccount recovered || RefreshAccountRejected(recovered))
        {
            await grants.RevokeAllRefreshTokensForMemberAsync(active.MemberAccountId, now, cancellationToken);
            return MobileAuthTokenResult.Failed("invalid_grant", "The refresh token grant is invalid.");
        }

        // Losing this rotation too means another refresh is mid-flight on the same
        // chain; the client keeps its token and retries.
        return await TryRotateTokenPairAsync(recovered, active.TokenHash, now, cancellationToken)
            ?? RotationConflict();
    }

    private static MobileAuthTokenResult RotationConflict() =>
        MobileAuthTokenResult.Failed(
            "temporarily_unavailable",
            "Another refresh of this grant is in progress. Try again.");

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
            return MobileAuthTokenResult.Failed("invalid_grant", PasswordGrantInvalidDescription);
        }

        if (!accountRateLimiter.IsAllowed(signIn.Account.Id))
        {
            return MobileAuthTokenResult.RateLimited();
        }

        return await IssueTokenPairAsync(signIn.Account, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public const string PasswordGrantInvalidDescription = "The password grant is invalid.";

    private static bool RefreshAccountRejected(MemberAccount? account) =>
        account is null || account.IsSuspended || account.DeletionRequestedAt is not null;

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

    private async Task<MobileAuthCallbackResult> IssueAuthorizationCodeAsync(
        MobileAuthAuthorizationSession session,
        MemberAccount account,
        CancellationToken cancellationToken)
    {
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

    private async Task<MobileAuthTokenResult> IssueTokenPairAsync(
        MemberAccount account,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var (refreshToken, grant) = NewRefreshGrant(account.Id, utcNow);
        await grants.StoreRefreshTokenAsync(grant, cancellationToken);
        return TokenPair(account, refreshToken);
    }

    /// <summary>
    /// Rotates <paramref name="rotatedFromTokenHash"/> into a new pair in one
    /// transaction. Returns null when another refresh rotated it first.
    /// </summary>
    private async Task<MobileAuthTokenResult?> TryRotateTokenPairAsync(
        MemberAccount account,
        string rotatedFromTokenHash,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var (refreshToken, grant) = NewRefreshGrant(account.Id, utcNow);
        return await grants.TryRotateRefreshTokenAsync(rotatedFromTokenHash, grant, utcNow, cancellationToken)
            ? TokenPair(account, refreshToken)
            : null;
    }

    private (string RefreshToken, MobileAuthRefreshTokenEntity Grant) NewRefreshGrant(
        Guid memberAccountId,
        DateTime utcNow)
    {
        var mobile = options.Value;
        var refreshToken = MobileAuthPkce.CreateOpaqueToken();
        return (refreshToken, new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = MobileAuthPkce.Sha256Hex(refreshToken),
            MemberAccountId = memberAccountId,
            ClientId = mobile.ClientId,
            CreatedAt = utcNow,
            ExpiresAt = utcNow.AddDays(mobile.RefreshTokenLifetimeDays),
        });
    }

    private MobileAuthTokenResult TokenPair(MemberAccount account, string refreshToken) =>
        MobileAuthTokenResult.Succeeded(
            tokens.IssueAccessToken(account.Id, account.Email, account.DisplayName),
            refreshToken,
            tokens.AccessTokenLifetimeSeconds);

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

    /// <summary>
    /// The grant that directly replaced <paramref name="token"/>, when it is still
    /// live and has never itself been rotated. Deliberately one hop only: a chain
    /// that moved on past the successor means two parties are using it.
    /// </summary>
    private async Task<MobileAuthRefreshTokenEntity?> FindUnusedSuccessorAsync(
        MobileAuthRefreshTokenEntity token,
        string clientId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (token.ReplacedByTokenHash is null)
        {
            return null;
        }

        var successor = await grants.FindRefreshTokenByHashAsync(token.ReplacedByTokenHash, cancellationToken);
        return successor is not null
            && successor.RevokedAt is null
            && successor.ExpiresAt > utcNow
            && successor.MemberAccountId == token.MemberAccountId
            && string.Equals(successor.ClientId, clientId, StringComparison.Ordinal)
                ? successor
                : null;
    }

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
    string? Code,
    bool RequiresConfirmation = false)
{
    public static MobileAuthCallbackResult Succeeded(string redirectUri, string state, string code) =>
        new(true, null, null, redirectUri, state, code);

    public static MobileAuthCallbackResult Failed(
        string error,
        string description,
        string? redirectUri = null,
        string? state = null) =>
        new(false, error, description, redirectUri, state, null);

    public static MobileAuthCallbackResult ConfirmationRequired(string redirectUri, string state) =>
        new(false, null, null, redirectUri, state, null, RequiresConfirmation: true);
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

using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed class MobileAuthService(
    MobileAuthAuthorizationSessionStore sessions,
    MemberAccountService memberAccountService,
    IOptions<MobileAuthOptions> options,
    MobileAuthGrantProcessingService grantProcessing)
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

        if (!grantProcessing.CanIssueTokens)
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

    public Task<MobileAuthTokenResult> ExchangeAuthorizationCodeAsync(
        string? grantType, string? clientId, string? redirectUri, string? code,
        string? codeVerifier, CancellationToken cancellationToken) =>
        grantProcessing.ExchangeAuthorizationCodeAsync(grantType, clientId, redirectUri, code, codeVerifier, cancellationToken);

    public Task<MobileAuthTokenResult> ExchangeRefreshTokenAsync(
        string? clientId, string? refreshToken, CancellationToken cancellationToken) =>
        grantProcessing.ExchangeRefreshTokenAsync(clientId, refreshToken, cancellationToken);

    public Task<MobileAuthTokenResult> ExchangePasswordGrantAsync(
        string? clientId, string? username, string? password, CancellationToken cancellationToken) =>
        grantProcessing.ExchangePasswordGrantAsync(clientId, username, password, cancellationToken);

    public Task RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken) =>
        grantProcessing.RevokeRefreshTokenAsync(refreshToken, cancellationToken);

    public Task<int> RevokeAllRefreshTokensForMemberAsync(Guid memberAccountId, CancellationToken cancellationToken) =>
        grantProcessing.RevokeAllRefreshTokensForMemberAsync(memberAccountId, cancellationToken);

    private Task<MobileAuthCallbackResult> IssueAuthorizationCodeAsync(
        MobileAuthAuthorizationSession session, MemberAccount account, CancellationToken cancellationToken) =>
        grantProcessing.IssueAuthorizationCodeAsync(session, account, cancellationToken);

    public const string PasswordGrantInvalidDescription = "The password grant is invalid.";

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

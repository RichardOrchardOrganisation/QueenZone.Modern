using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

/// <summary>
/// OAuth2 authorization-code + PKCE endpoints for the mobile public client, plus a
/// resource-owner password grant for operator-created accounts (App Review / non-social).
/// QueenZone remains the confidential client toward Google/Microsoft/Discord/GitHub/Apple;
/// the React Native app never sees a provider secret. Password is typed only on the
/// secondary “Other ways to sign in” path.
/// </summary>
public static class MobileAuthEndpoints
{
    public const string AuthorizePath = "/api/v1/auth/authorize";

    public const string CallbackPath = "/api/v1/auth/callback";

    public const string TokenPath = "/api/v1/auth/token";

    public const string RevokePath = "/api/v1/auth/revoke";

    public const string LogoutPath = "/api/v1/auth/logout";

    public const string SessionPath = "/api/v1/auth/session";

    public const string ProvidersPath = "/api/v1/auth/providers";

    public static void MapMobileAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Auth")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.Auth)
            .DisableAntiforgery();

        group.MapGet("/authorize", AuthorizeAsync);
        group.MapGet("/callback", CallbackAsync);
        group.MapPost("/token", TokenAsync);
        group.MapPost("/revoke", RevokeAsync);
        group.MapGet("/providers", GetProviders)
            .WithName("GetMobileAuthProviders")
            .WithSummary("Sign-in providers configured on this host, matching website /account/login.")
            .Produces<MobileAuthProvidersResponse>();
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy);
        group.MapGet("/session", Session)
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy);
    }

    internal static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        MobileAuthService mobileAuth,
        IAuthenticationSchemeProvider schemes,
        [AsParameters] MobileAuthorizationQuery query)
    {
        var started = mobileAuth.StartAuthorization(
            query.ResponseType,
            query.ClientId,
            query.RedirectUri,
            query.CodeChallenge,
            query.CodeChallengeMethod,
            query.State,
            query.Provider);

        if (!started.Success)
        {
            return started.RedirectSafe
                ? RedirectToApp(httpContext, started.RedirectUri!, started.State, error: started.Error, description: started.ErrorDescription)
                : ErrorJson(started.Error!, started.ErrorDescription!, StatusCodes.Status400BadRequest);
        }

        var session = started.Session!;
        var registered = await schemes.GetSchemeAsync(session.Provider);
        if (registered is null)
        {
            return RedirectToApp(
                httpContext,
                session.RedirectUri,
                session.State,
                error: "temporarily_unavailable",
                description: "That sign-in provider is not configured.");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = $"{CallbackPath}?rid={Uri.EscapeDataString(session.RequestId)}",
        };
        if (!string.Equals(session.Provider, MemberAuthenticationSchemes.Apple, StringComparison.OrdinalIgnoreCase))
        {
            properties.SetParameter("prompt", "select_account");
        }

        return Results.Challenge(properties, [session.Provider]);
    }

    private static string ResolveExternalDisplayName(string? displayName, string emailValue, string provider)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.IsNullOrWhiteSpace(emailValue) ? provider : emailValue;
        }
        return displayName;
    }

    private static string? ProtectAppleRefreshToken(
        string? provider,
        AuthenticationProperties? properties,
        AppleAccountTokenService appleTokens)
    {
        var appleRefreshToken = string.Equals(provider, MemberAuthenticationSchemes.Apple, StringComparison.OrdinalIgnoreCase)
            ? properties?.GetTokenValue("refresh_token")
            : null;
        return string.IsNullOrWhiteSpace(appleRefreshToken)
            ? null
            : appleTokens.Protect(appleRefreshToken);

    }

    internal static async Task<IResult> CallbackAsync(
        HttpContext httpContext,
        MobileAuthService mobileAuth,
        AppleAccountTokenService appleTokens,
        QueenZone.Data.IMemberAccountRepository memberAccounts,
        IAuthenticationSchemeProvider schemes,
        string? rid,
        CancellationToken cancellationToken)
    {
        if (await schemes.GetSchemeAsync(MemberAuthenticationSchemes.ExternalCookie) is null)
        {
            return ErrorJson("access_denied", "External sign-in was cancelled.", StatusCodes.Status400BadRequest);
        }

        var external = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.ExternalCookie);
        if (!external.Succeeded || external.Principal is null)
        {
            return ErrorJson("access_denied", "External sign-in was cancelled.", StatusCodes.Status400BadRequest);
        }

        var provider = external.Principal.Identities.FirstOrDefault()?.AuthenticationType;
        var providerKey = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = external.Principal.FindFirstValue(ClaimTypes.Email);
        var displayName = external.Principal.FindFirstValue(ClaimTypes.Name);
        var protectedAppleToken = ProtectAppleRefreshToken(provider, external.Properties, appleTokens);

        if (!HasRequiredExternalIdentity(provider, providerKey))
        {
            await httpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);
            return ErrorJson("server_error", "The identity provider did not return the required profile.", StatusCodes.Status400BadRequest);
        }

        var profile = ReadExternalProfile(email, displayName, provider, external.Principal);
        var emailValue = profile.Email;
        displayName = profile.DisplayName;
        var emailVerified = profile.EmailVerified;

        var completed = await mobileAuth.CompleteExternalLoginAsync(
            rid,
            provider,
            providerKey,
            emailValue,
            displayName,
            emailVerified,
            cancellationToken);

        if (completed.RequiresConfirmation)
        {
            await ExternalLoginLinkCookie.SignInAsync(
                httpContext,
                new PendingExternalLink(
                    MemberAuthenticationSchemes.NormalizeExternalProvider(provider) ?? provider,
                    providerKey,
                    emailValue,
                    displayName,
                    ReturnUrl: "/",
                    MobileRequestId: rid,
                    ProtectedAppleRefreshToken: protectedAppleToken));
            await httpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);
            return Results.Redirect(ExternalLoginLinkCookie.PagePath);
        }

        await httpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);

        if (!completed.Success || completed.RedirectUri is null)
        {
            return FailedCallbackResult(httpContext, completed);
        }

        await SaveProtectedAppleTokenAsync(
            protectedAppleToken, provider, providerKey, memberAccounts, appleTokens, cancellationToken);

        return RedirectToApp(
            httpContext,
            completed.RedirectUri,
            completed.State,
            code: completed.Code);
    }

    private static bool HasRequiredExternalIdentity(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? provider,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? providerKey) =>
        !string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(providerKey);

    private sealed record ExternalProfile(string Email, string DisplayName, bool EmailVerified);

    private static ExternalProfile ReadExternalProfile(
        string? email,
        string? displayName,
        string provider,
        ClaimsPrincipal principal)
    {
        var emailValue = email ?? string.Empty;
        displayName = ResolveExternalDisplayName(displayName, emailValue, provider);

        var emailVerified = !string.IsNullOrWhiteSpace(emailValue)
            && ExternalLoginEmail.IsVerified(provider, principal);

        return new ExternalProfile(emailValue, displayName, emailVerified);
    }

    private static IResult FailedCallbackResult(HttpContext httpContext, MobileAuthCallbackResult completed)
    {
        return completed.RedirectUri is null
            ? ErrorJson(completed.Error!, completed.ErrorDescription!, StatusCodes.Status400BadRequest)
            : RedirectToApp(
                httpContext,
                completed.RedirectUri,
                completed.State,
                error: completed.Error,
                description: completed.ErrorDescription);
    }

    private static async Task SaveProtectedAppleTokenAsync(
        string? protectedAppleToken,
        string provider,
        string providerKey,
        QueenZone.Data.IMemberAccountRepository memberAccounts,
        AppleAccountTokenService appleTokens,
        CancellationToken cancellationToken)
    {
        if (protectedAppleToken is not null)
        {
            var account = await memberAccounts.FindByExternalLoginAsync(provider, providerKey, cancellationToken);
            if (account is not null)
            {
                await appleTokens.SaveProtectedAsync(account.Id, providerKey, protectedAppleToken, cancellationToken);
            }
        }

    }

    internal static async Task<IResult> TokenAsync(
        HttpContext httpContext,
        MobileAuthService mobileAuth,
        CancellationToken cancellationToken)
    {
        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return ErrorJson("invalid_request", "Token requests must be application/x-www-form-urlencoded.");
        }
        catch (InvalidDataException)
        {
            return ErrorJson("invalid_request", "Token requests must be application/x-www-form-urlencoded.");
        }

        var grantType = form["grant_type"].ToString();
        var exchanged = grantType switch
        {
            "refresh_token" => await mobileAuth.ExchangeRefreshTokenAsync(
                form["client_id"].ToString(),
                form["refresh_token"].ToString(),
                cancellationToken),
            "password" => await mobileAuth.ExchangePasswordGrantAsync(
                form["client_id"].ToString(),
                form["username"].ToString(),
                form["password"].ToString(),
                cancellationToken),
            _ => await mobileAuth.ExchangeAuthorizationCodeAsync(
                grantType,
                form["client_id"].ToString(),
                form["redirect_uri"].ToString(),
                form["code"].ToString(),
                form["code_verifier"].ToString(),
                cancellationToken),
        };

        if (!exchanged.Success)
        {
            return ErrorJson(exchanged.Error!, exchanged.ErrorDescription!, exchanged.StatusCode);
        }

        return Results.Json(new
        {
            access_token = exchanged.AccessToken,
            refresh_token = exchanged.RefreshToken,
            token_type = "Bearer",
            expires_in = exchanged.ExpiresIn,
        });
    }

    internal static async Task<IResult> RevokeAsync(
        HttpContext httpContext,
        MobileAuthService mobileAuth,
        CancellationToken cancellationToken)
    {
        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Results.Ok();
        }
        catch (InvalidDataException)
        {
            return Results.Ok();
        }

        // RFC 7009: always 200, never echo the presented token.
        await mobileAuth.RevokeRefreshTokenAsync(form["token"].ToString(), cancellationToken);
        return Results.Ok();
    }

    internal static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user,
        MobileAuthService mobileAuth,
        CancellationToken cancellationToken)
    {
        var memberIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(memberIdValue, out var memberId))
        {
            return Results.Unauthorized();
        }

        await mobileAuth.RevokeAllRefreshTokensForMemberAsync(memberId, cancellationToken);
        return Results.NoContent();
    }

    internal static IResult GetProviders(IOptions<MemberAuthenticationOptions> options)
    {
        var auth = options.Value;
        var providers = new List<MobileAuthProviderDto>();
        if (auth.Google?.ClientId is { Length: > 0 })
        {
            providers.Add(new MobileAuthProviderDto(MemberAuthenticationSchemes.Google, "Continue with Google"));
        }

        if (auth.Microsoft?.ClientId is { Length: > 0 })
        {
            providers.Add(new MobileAuthProviderDto(MemberAuthenticationSchemes.Microsoft, "Continue with Microsoft"));
        }

        if (auth.Discord?.ClientId is { Length: > 0 })
        {
            providers.Add(new MobileAuthProviderDto(MemberAuthenticationSchemes.Discord, "Continue with Discord"));
        }

        if (auth.GitHub?.ClientId is { Length: > 0 })
        {
            providers.Add(new MobileAuthProviderDto(MemberAuthenticationSchemes.GitHub, "Continue with GitHub"));
        }

        if (auth.Apple?.IsConfigured == true)
        {
            providers.Add(new MobileAuthProviderDto(MemberAuthenticationSchemes.Apple, "Continue with Apple"));
        }

        return Results.Json(new MobileAuthProvidersResponse(providers));
    }

    internal static IResult Session(ClaimsPrincipal user)
    {
        var memberId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Results.Json(new
        {
            memberId,
            email = user.FindFirstValue(ClaimTypes.Email),
            displayName = user.FindFirstValue(ClaimTypes.Name),
        });
    }

    internal static string BuildAppRedirect(
        string redirectUri,
        string? state,
        string? code = null,
        string? error = null,
        string? description = null)
    {
        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var location = redirectUri + separator;
        if (!string.IsNullOrEmpty(error))
        {
            location += "error=" + Uri.EscapeDataString(error);
            if (!string.IsNullOrEmpty(description))
            {
                location += "&error_description=" + Uri.EscapeDataString(description);
            }
        }
        else
        {
            location += "code=" + Uri.EscapeDataString(code ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(state))
        {
            location += "&state=" + Uri.EscapeDataString(state);
        }

        return location;
    }

    private static IResult RedirectToApp(
        HttpContext httpContext,
        string redirectUri,
        string? state,
        string? code = null,
        string? error = null,
        string? description = null)
    {
        // Response.Redirect accepts custom app schemes (queenzone://); Results.Redirect does not.
        httpContext.Response.Redirect(BuildAppRedirect(redirectUri, state, code, error, description));
        return Results.Empty;
    }

    private static IResult ErrorJson(string error, string description, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(new { error, error_description = description }, statusCode: statusCode);
}

public sealed record MobileAuthProviderDto(string Id, string Label);

public sealed record MobileAuthProvidersResponse(IReadOnlyList<MobileAuthProviderDto> Providers);

internal sealed record MobileAuthorizationQuery(
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "response_type")] string? ResponseType,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "client_id")] string? ClientId,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "redirect_uri")] string? RedirectUri,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "code_challenge")] string? CodeChallenge,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "code_challenge_method")] string? CodeChallengeMethod,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "state")] string? State,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "provider")] string? Provider);

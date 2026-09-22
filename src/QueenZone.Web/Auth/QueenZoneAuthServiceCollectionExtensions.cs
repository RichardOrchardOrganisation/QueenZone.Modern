using System.Security.Claims;
using AspNet.Security.OAuth.Apple;
using AspNet.Security.OAuth.Discord;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using QueenZone.Data;

namespace QueenZone.Web;

public static class QueenZoneAuthServiceCollectionExtensions
{
    public static IServiceCollection AddQueenZoneAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var azureAdSection = configuration.GetSection("AzureAd");
        var clientId = azureAdSection["ClientId"];
        // Fail closed outside Development/Testing when Entra is missing or still a placeholder.
        AzureAdClientId.EnsureConfiguredForEnvironment(environment, clientId);
        var useAzureAd = !QueenZoneEnvironments.UsesTestAuth(environment) && AzureAdClientId.IsConfigured(clientId);

        if (QueenZoneEnvironments.UsesTestAuth(environment))
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddPolicyScheme(AdminAuthenticationSchemes.CompositeScheme, null, options =>
                    ConfigureAdminAuthenticationScheme(options, useAzureAd: false))
                .AddPolicyScheme(AdminAuthenticationSchemes.AuthoringCompositeScheme, null, options =>
                    ConfigureAuthoringAuthenticationScheme(options, useAzureAd: false))
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null)
                .AddScheme<AuthenticationSchemeOptions, TestMemberAuthHandler>(TestMemberAuthHandler.SchemeName, null)
                // A real (not test-shortcut) cookie scheme: native register/sign-in pages call
                // SignInAsync, which requires a handler that actually implements sign-in, and a
                // local cookie has no external dependency that would make it unsuitable for tests.
                .AddCookie(MemberAuthenticationSchemes.MembersCookie, options =>
                {
                    options.LoginPath = "/account/login";
                    options.LogoutPath = "/account/logout";
                    options.Events = MemberCookieEvents;
                });
            ConfigureMobileBearer(configuration, environment, services.AddAuthentication());
            return services;
        }

        // Development-only fallback: empty/placeholder ClientId enables X-Test-User-Email admin auth.
        // Staging and Production must never reach this branch (guarded above).
        if (!useAzureAd)
        {
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddPolicyScheme(AdminAuthenticationSchemes.CompositeScheme, null, options =>
                    ConfigureAdminAuthenticationScheme(options, useAzureAd: false))
                .AddPolicyScheme(AdminAuthenticationSchemes.AuthoringCompositeScheme, null, options =>
                    ConfigureAuthoringAuthenticationScheme(options, useAzureAd: false))
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        }
        else
        {
            // The app-wide default authenticate scheme is the cheap local cookie scheme that
            // AddMicrosoftIdentityWebApp registers (the same one the admin composite scheme
            // authenticates against below) — not the remote OpenIdConnect scheme, which exists
            // only to drive the challenge/redirect-to-Entra flow and process the /signin-oidc
            // callback. OpenIdConnect stays the default *challenge* scheme so admin sign-in is
            // unaffected. Probe paths (/health, /health/ready, /warmup) are excluded from this
            // middleware entirely in Program.cs so nothing in the authenticated pipeline can
            // block Azure's cold-start probe (#666, #677).
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddMicrosoftIdentityWebApp(configuration);

            services.AddAuthentication()
                .AddPolicyScheme(AdminAuthenticationSchemes.CompositeScheme, null, options =>
                    ConfigureAdminAuthenticationScheme(options, useAzureAd: true))
                .AddPolicyScheme(AdminAuthenticationSchemes.AuthoringCompositeScheme, null, options =>
                    ConfigureAuthoringAuthenticationScheme(options, useAzureAd: true));
        }

        // A second AddAuthentication() call doesn't reset the default scheme set above; it just
        // returns a plain AuthenticationBuilder bound to the same AuthenticationOptions so the
        // member schemes can be chained on without fighting Microsoft.Identity.Web's own builder type.
        ConfigureMemberAuthentication(configuration, environment, services.AddAuthentication());
        return services;
    }

    public static IServiceCollection AddQueenZoneAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminAuthenticationSchemes.Policy, policy =>
                policy.AddAuthenticationSchemes(AdminAuthenticationSchemes.CompositeScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => AdminAllowlist.IsAllowed(
                        context.User,
                        configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>())));

            options.AddPolicy(MemberAuthenticationSchemes.MemberPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(MemberAuthenticationSchemes.MembersCookie);
                if (QueenZoneEnvironments.UsesTestAuth(environment))
                {
                    policy.AddAuthenticationSchemes(TestMemberAuthHandler.SchemeName);
                }

                policy.RequireAuthenticatedUser();
            });

            // Shared authoring (rich text image upload) accepts either a member cookie or
            // Entra/test admin auth. This uses a separate composite scheme because AdminAccess
            // must never accept a member cookie. Members
            // impersonated purely via X-Test-Member-Id (no MembersCookie) still need to reach
            // this endpoint from Submit/Article and Submit/Photo's rich text editors, so add the
            // TestMember scheme the same way MemberPolicy above does.
            options.AddPolicy("Authoring", policy =>
            {
                policy.AddAuthenticationSchemes(AdminAuthenticationSchemes.AuthoringCompositeScheme);
                if (QueenZoneEnvironments.UsesTestAuth(environment))
                {
                    policy.AddAuthenticationSchemes(TestMemberAuthHandler.SchemeName);
                }

                policy.RequireAuthenticatedUser();
            });

            options.AddPolicy(MemberAuthenticationSchemes.MobileMemberPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(MemberAuthenticationSchemes.MembersBearer);
                policy.RequireAuthenticatedUser();
            });
        });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminApiAuthorizationResultHandler>();

        return services;
    }

    private static void ConfigureAdminAuthenticationScheme(PolicySchemeOptions options, bool useAzureAd)
    {
        // A public member session is not proof of admin authentication. Always authenticate
        // AdminAccess through the dedicated Entra cookie (or the local/test admin handler).
        options.ForwardDefault = SelectAdminAuthenticateScheme(useAzureAd);

        // Challenges must start Entra OIDC (not member social login at /account/login).
        options.ForwardChallenge = SelectAdminChallengeScheme(useAzureAd);
    }

    private static void ConfigureAuthoringAuthenticationScheme(PolicySchemeOptions options, bool useAzureAd)
    {
        options.ForwardDefaultSelector = context => SelectAuthoringAuthenticateScheme(
            useAzureAd,
            context.Request.Cookies.ContainsKey(AdminAuthenticationSchemes.MemberCookieName));

        options.ForwardChallenge = SelectAdminChallengeScheme(useAzureAd);
    }

    /// <summary>
    /// Scheme used to authenticate an already-signed-in admin principal.
    /// </summary>
    internal static string SelectAdminAuthenticateScheme(bool useAzureAd) =>
        useAzureAd
            ? CookieAuthenticationDefaults.AuthenticationScheme
            : TestAuthHandler.SchemeName;

    /// <summary>
    /// Scheme used when the Admin policy challenges an unauthenticated request.
    /// With Entra enabled this must be OIDC, not the member cookie login path.
    /// </summary>
    internal static string SelectAdminChallengeScheme(bool useAzureAd) =>
        useAzureAd
            ? OpenIdConnectDefaults.AuthenticationScheme
            : TestAuthHandler.SchemeName;

    internal static string SelectAuthoringAuthenticateScheme(bool useAzureAd, bool hasMemberCookie) =>
        hasMemberCookie
            ? MemberAuthenticationSchemes.MembersCookie
            : SelectAdminAuthenticateScheme(useAzureAd);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void ConfigureMemberAuthentication(
        IConfiguration configuration,
        IHostEnvironment environment,
        AuthenticationBuilder authenticationBuilder)
    {
        authenticationBuilder.AddCookie(MemberAuthenticationSchemes.MembersCookie, options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            // Lax (not Strict): the OAuth callback is a top-level GET redirected back from the
            // external provider's domain, and Strict would drop the cookie on that navigation.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events = MemberCookieEvents;
        });

        authenticationBuilder.AddCookie(MemberAuthenticationSchemes.ExternalCookie, options =>
        {
            options.Cookie.Name = ".QueenZone.MembersExternal";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        var memberAuth = configuration
            .GetSection(MemberAuthenticationOptions.SectionName)
            .Get<MemberAuthenticationOptions>();

        if (!string.IsNullOrWhiteSpace(memberAuth?.Google?.ClientId))
        {
            authenticationBuilder.AddGoogle(MemberAuthenticationSchemes.Google, options =>
            {
                options.ClientId = memberAuth.Google.ClientId!;
                options.ClientSecret = memberAuth.Google.ClientSecret!;
                options.SignInScheme = MemberAuthenticationSchemes.ExternalCookie;
            });
        }

        if (!string.IsNullOrWhiteSpace(memberAuth?.Microsoft?.ClientId))
        {
            authenticationBuilder.AddMicrosoftAccount(MemberAuthenticationSchemes.Microsoft, options =>
            {
                options.ClientId = memberAuth.Microsoft.ClientId!;
                options.ClientSecret = memberAuth.Microsoft.ClientSecret!;
                options.SignInScheme = MemberAuthenticationSchemes.ExternalCookie;
            });
        }

        if (!string.IsNullOrWhiteSpace(memberAuth?.Discord?.ClientId))
        {
            authenticationBuilder.AddDiscord(MemberAuthenticationSchemes.Discord, options =>
            {
                options.ClientId = memberAuth.Discord.ClientId!;
                options.ClientSecret = memberAuth.Discord.ClientSecret!;
                options.SignInScheme = MemberAuthenticationSchemes.ExternalCookie;
                options.Scope.Add("email");
            });
        }

        if (!string.IsNullOrWhiteSpace(memberAuth?.GitHub?.ClientId))
        {
            authenticationBuilder.AddGitHub(MemberAuthenticationSchemes.GitHub, options =>
            {
                options.ClientId = memberAuth.GitHub.ClientId!;
                options.ClientSecret = memberAuth.GitHub.ClientSecret!;
                options.SignInScheme = MemberAuthenticationSchemes.ExternalCookie;
                options.Scope.Add("user:email");
            });
        }

        if (memberAuth?.Apple?.IsConfigured == true)
        {
            var apple = memberAuth.Apple;
            var privateKey = AppleAuthenticationSupport.NormalizePrivateKey(apple.PrivateKey!);
            authenticationBuilder.AddApple(MemberAuthenticationSchemes.Apple, options =>
            {
                options.ClientId = apple.ClientId!;
                options.TeamId = apple.TeamId!;
                options.KeyId = apple.KeyId!;
                options.SignInScheme = MemberAuthenticationSchemes.ExternalCookie;
                options.GenerateClientSecret = true;
                options.PrivateKey = (_, _) =>
                    Task.FromResult<ReadOnlyMemory<char>>(privateKey.AsMemory());
                options.Events.OnCreatingTicket = async context =>
                {
                    if (!context.Request.HasFormContentType)
                    {
                        return;
                    }

                    var form = await context.Request.ReadFormAsync(context.HttpContext.RequestAborted);
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        AppleAuthenticationSupport.AddNameClaim(
                            identity,
                            form["user"].FirstOrDefault());
                    }
                };
            });
        }

        ConfigureMobileBearer(configuration, environment, authenticationBuilder);
    }

    private static void ConfigureMobileBearer(
        IConfiguration configuration,
        IHostEnvironment environment,
        AuthenticationBuilder authenticationBuilder)
    {
        authenticationBuilder.AddJwtBearer(MemberAuthenticationSchemes.MembersBearer, options =>
            MobileAuthTokenIssuer.ConfigureJwtBearer(options, configuration, environment));
    }

    /// <summary>
    /// Re-checks the account on every request that carries the members cookie. Suspension,
    /// a missing account, or a credential issued before a deletion request ends the session
    /// immediately rather than waiting for the 30-day cookie to expire. A sign-in after the
    /// deletion request is left in place so the member can cancel during the cooling-off period.
    /// Shared between the test-auth and Entra branches.
    /// </summary>
    private static CookieAuthenticationEvents MemberCookieEvents { get; } = new()
    {
        OnValidatePrincipal = async context =>
        {
            var memberIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (memberIdClaim is null || !Guid.TryParse(memberIdClaim, out var memberId))
            {
                return;
            }

            var repository = context.HttpContext.RequestServices.GetRequiredService<IMemberAccountRepository>();
            var account = await repository.FindByIdAsync(memberId, context.HttpContext.RequestAborted);
            var issuedAt = MemberSessionGate.ResolveIssuedAt(context.Principal, context.Properties.IssuedUtc);
            if (MemberSessionGate.Reject(account, issuedAt))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(MemberAuthenticationSchemes.MembersCookie);
            }
        },
    };

}

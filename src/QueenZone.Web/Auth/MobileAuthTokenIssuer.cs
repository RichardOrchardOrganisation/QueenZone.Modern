using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class MobileAuthTokenIssuer(
    IOptions<MobileAuthOptions> options,
    IOptions<SiteOptions> site,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    public string Issuer => site.Value.PublicBaseUrl.TrimEnd('/');

    public string Audience => options.Value.ClientId;

    public int AccessTokenLifetimeSeconds =>
        Math.Max(1, options.Value.AccessTokenLifetimeMinutes) * 60;

    public bool CanIssueTokens =>
        !string.IsNullOrEmpty(options.Value.ResolveSigningKey(QueenZoneEnvironments.IsProductionLike(environment)));

    public string IssueAccessToken(Guid memberId, string email, string displayName)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var issuedAt = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        var signingKey = options.Value.ResolveSigningKey(QueenZoneEnvironments.IsProductionLike(environment));
        if (string.IsNullOrEmpty(signingKey))
        {
            throw new InvalidOperationException(
                "MobileAuth:SigningKey is not configured. Set MobileAuth__SigningKey on the host before issuing mobile tokens.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, memberId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, memberId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(
                MemberSessionGate.IssuedAtUnixMillisecondsClaim,
                issuedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(options.Value.AccessTokenLifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static void ConfigureJwtBearer(
        JwtBearerOptions jwt,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var mobile = configuration.GetSection(MobileAuthOptions.SectionName).Get<MobileAuthOptions>()
            ?? new MobileAuthOptions();
        var site = configuration.GetSection(SiteOptions.SectionName).Get<SiteOptions>()
            ?? new SiteOptions();

        jwt.MapInboundClaims = true;
        jwt.TokenValidationParameters = CreateValidationParameters(
            issuer: site.PublicBaseUrl.TrimEnd('/'),
            audience: string.IsNullOrWhiteSpace(mobile.ClientId)
                ? MobileAuthOptions.DefaultClientId
                : mobile.ClientId,
            signingKey: ResolveJwtValidationSigningKey(mobile, environment));
        jwt.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                // JSON APIs should not redirect to /account/login.
                context.HandleResponse();
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized")
                    .ExecuteAsync(context.HttpContext);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Forbidden")
                    .ExecuteAsync(context.HttpContext);
            },
            OnTokenValidated = async context =>
            {
                var memberIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(memberIdValue, out var memberId))
                {
                    context.Fail("The access token is not valid.");
                    return;
                }

                var repository = context.HttpContext.RequestServices.GetRequiredService<IMemberAccountRepository>();
                var account = await repository.FindByIdAsync(memberId, context.HttpContext.RequestAborted);
                if (MemberSessionGate.Reject(account, MemberSessionGate.ReadIssuedAt(context.Principal)))
                {
                    context.Fail("The access token is not valid.");
                }
            },
        };
    }

    public static TokenValidationParameters CreateValidationParameters(
        string issuer,
        string audience,
        string signingKey) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };

    internal static string ResolveJwtValidationSigningKey(MobileAuthOptions mobile, IHostEnvironment environment)
    {
        var configured = mobile.ResolveSigningKey(QueenZoneEnvironments.IsProductionLike(environment));
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        if (!QueenZoneEnvironments.IsProductionLike(environment))
        {
            return MobileAuthOptions.DevelopmentSigningKey;
        }

        // Per-process unusable key: a missing App Service setting must not take the public
        // site down, and the committed development key must not validate production JWTs.
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class AdminPrincipalRequirement : IAuthorizationRequirement;

public sealed class AdminPrincipalRequirementHandler(
    IOptionsMonitor<AdminOptions> options,
    ILogger<AdminPrincipalRequirementHandler> logger) : AuthorizationHandler<AdminPrincipalRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminPrincipalRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var settings = options.CurrentValue;
        if (!settings.EnforcePrincipalPin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var tenantReason = ReadIdentifier(context.User, "tid",
            "http://schemas.microsoft.com/identity/claims/tenantid", out var tenant);
        var oidReason = ReadIdentifier(context.User, "oid",
            "http://schemas.microsoft.com/identity/claims/objectidentifier", out var oid);
        var reason = tenantReason ?? oidReason;
        if (reason is null && !(settings.AllowedPrincipals ?? []).Any(entry =>
                entry?.Split(':', StringSplitOptions.TrimEntries) is [var allowedTenant, var allowedOid] &&
                Guid.TryParse(allowedTenant, out var tenantId) && Guid.TryParse(allowedOid, out var objectId) &&
                tenant == tenantId && oid == objectId))
        {
            reason = "NotPinned";
        }

        if (reason is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            var rawOid = context.User.FindFirst("oid")?.Value ??
                context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? "";
            var oidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawOid)))[..8];
            logger.LogWarning("Admin sign-in principal pin failed: {ReasonCode}, oid hash {ObjectIdHash}", reason, oidHash);
            context.Fail();
        }

        return Task.CompletedTask;
    }

    private static string? ReadIdentifier(ClaimsPrincipal user, string shortType, string longType, out Guid value)
    {
        value = default;
        var claims = user.FindAll(shortType).Concat(user.FindAll(longType)).ToArray();
        if (claims.Length == 0)
        {
            return "MissingClaims";
        }

        for (var i = 0; i < claims.Length; i++)
        {
            if (!Guid.TryParse(claims[i].Value.Trim(), out var parsed))
            {
                return "InvalidClaim";
            }

            if (i > 0 && value != parsed)
            {
                return "ClaimConflict";
            }

            value = parsed;
        }

        return null;
    }
}

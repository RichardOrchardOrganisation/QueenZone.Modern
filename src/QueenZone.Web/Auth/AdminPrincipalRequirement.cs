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

        var reason = GetDenyReason(context.User, settings.AllowedPrincipals ?? []);
        if (reason is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogWarning("Admin sign-in principal pin failed: {ReasonCode}, oid hash {ObjectIdHash}",
                reason, HashObjectId(context.User));
            context.Fail();
        }

        return Task.CompletedTask;
    }

    private const string TenantClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    private static string? GetDenyReason(ClaimsPrincipal user, string[] allowedPrincipals)
    {
        var tenantReason = ReadIdentifier(user, "tid", TenantClaimType, out var tenant);
        var oidReason = ReadIdentifier(user, "oid", ObjectIdClaimType, out var oid);
        var reason = tenantReason ?? oidReason;
        if (reason is not null)
        {
            return reason;
        }

        return allowedPrincipals.Any(entry => IsPinnedEntry(entry, tenant, oid)) ? null : "NotPinned";
    }

    private static bool IsPinnedEntry(string? entry, Guid tenant, Guid oid) =>
        entry?.Split(':', StringSplitOptions.TrimEntries) is [var allowedTenant, var allowedOid] &&
        Guid.TryParse(allowedTenant, out var tenantId) && Guid.TryParse(allowedOid, out var objectId) &&
        tenant == tenantId && oid == objectId;

    private static string HashObjectId(ClaimsPrincipal user)
    {
        var rawOid = user.FindFirst("oid")?.Value ?? user.FindFirst(ObjectIdClaimType)?.Value ?? "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawOid)))[..8];
    }

    private static string? ReadIdentifier(ClaimsPrincipal user, string shortType, string longType, out Guid value)
    {
        value = Guid.Empty;
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

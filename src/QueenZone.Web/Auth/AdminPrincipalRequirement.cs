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
        var settings = options.CurrentValue;
        if (!settings.EnforcePrincipalPin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var tenant = (context.User.FindFirst("tid") ??
            context.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid"))?.Value.Trim();
        var oid = (context.User.FindFirst("oid") ??
            context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier"))?.Value.Trim();
        var allowed = !string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(oid) &&
            (settings.AllowedPrincipals ?? []).Any(entry =>
                entry?.Split(':', StringSplitOptions.TrimEntries) is [var allowedTenant, var allowedOid] &&
                string.Equals(tenant, allowedTenant, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oid, allowedOid, StringComparison.OrdinalIgnoreCase));

        if (allowed)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogWarning("Admin sign-in principal pin failed for tid {TenantId} and oid {ObjectId}", tenant, oid);
            context.Fail();
        }

        return Task.CompletedTask;
    }
}

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace QueenZone.Web;

internal static class MutationRateLimitPartitions
{
    private const string NoAuthenticatedIpSafetyNet = "mutation:authenticated-ip:none";

    public static RateLimitPartition<string> AnonymousWrite(
        HttpContext context,
        MutationRateLimitingOptions options) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"mutation:anonymous-ip:{ClientKey(context)}",
            _ => FixedWindow(options.AnonymousPermitLimit, options.AnonymousWindowMinutes));

    public static RateLimitPartition<string> AuthenticatedWrite(
        HttpContext context,
        MutationRateLimitingOptions options)
    {
        var memberId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partition = !string.IsNullOrWhiteSpace(memberId)
            ? $"mutation:member:{memberId}"
            : $"mutation:unauthenticated-fallback:{ClientKey(context)}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => FixedWindow(
                options.AuthenticatedMemberPermitLimit,
                options.AuthenticatedMemberWindowMinutes));
    }

    public static RateLimitPartition<string> AuthenticatedIpSafetyNet(
        HttpContext context,
        MutationRateLimitingOptions options)
    {
        if (!UsesAuthenticatedWritePolicy(context))
        {
            return RateLimitPartition.GetNoLimiter(NoAuthenticatedIpSafetyNet);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"mutation:authenticated-ip:{ClientKey(context)}",
            _ => FixedWindow(
                options.AuthenticatedIpPermitLimit,
                options.AuthenticatedIpWindowMinutes));
    }

    public static string ClientKey(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is { } remoteIp)
        {
            return remoteIp.ToString();
        }

        if (!string.IsNullOrWhiteSpace(context.Connection.Id))
        {
            return $"connection:{context.Connection.Id}";
        }

        // TestServer and synthetic contexts may not have a socket or connection id.
        // A request-specific fallback avoids collapsing every such request into one
        // shared "unknown" bucket. Tests that exercise a limit set RemoteIpAddress.
        return $"trace:{context.TraceIdentifier}";
    }

    public static bool UsesAuthenticatedWritePolicy(HttpContext context) =>
        string.Equals(
            context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
            QueenZoneRateLimitPolicies.AuthenticatedWrite,
            StringComparison.Ordinal);

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit, int windowMinutes) =>
        new()
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(windowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true,
        };
}

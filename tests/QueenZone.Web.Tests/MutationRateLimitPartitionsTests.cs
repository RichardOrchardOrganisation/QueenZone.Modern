using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace QueenZone.Web.Tests;

public sealed class MutationRateLimitPartitionsTests
{
    [Fact]
    public void AnonymousWrite_partitions_by_client_ip()
    {
        var options = new MutationRateLimitingOptions();
        var first = CreateContext("203.0.113.10");
        var sameIp = CreateContext("203.0.113.10");
        var otherIp = CreateContext("203.0.113.11");

        var firstPartition = MutationRateLimitPartitions.AnonymousWrite(first, options);
        var sameIpPartition = MutationRateLimitPartitions.AnonymousWrite(sameIp, options);
        var otherIpPartition = MutationRateLimitPartitions.AnonymousWrite(otherIp, options);

        Assert.Equal(firstPartition.PartitionKey, sameIpPartition.PartitionKey);
        Assert.NotEqual(firstPartition.PartitionKey, otherIpPartition.PartitionKey);
    }

    [Fact]
    public void AuthenticatedWrite_partitions_by_member_across_client_ips()
    {
        var options = new MutationRateLimitingOptions();
        var memberId = Guid.NewGuid();
        var first = CreateContext("203.0.113.10", memberId);
        var changedIp = CreateContext("203.0.113.11", memberId);
        var otherMember = CreateContext("203.0.113.10", Guid.NewGuid());

        var firstPartition = MutationRateLimitPartitions.AuthenticatedWrite(first, options);
        var changedIpPartition = MutationRateLimitPartitions.AuthenticatedWrite(changedIp, options);
        var otherMemberPartition = MutationRateLimitPartitions.AuthenticatedWrite(otherMember, options);

        Assert.Equal(firstPartition.PartitionKey, changedIpPartition.PartitionKey);
        Assert.NotEqual(firstPartition.PartitionKey, otherMemberPartition.PartitionKey);
    }

    [Fact]
    public void AuthenticatedIpSafetyNet_is_shared_across_members_from_same_ip()
    {
        var options = new MutationRateLimitingOptions
        {
            AuthenticatedMemberPermitLimit = 10,
            AuthenticatedMemberWindowMinutes = 60,
            AuthenticatedIpPermitLimit = 1,
            AuthenticatedIpWindowMinutes = 60,
        };
        var first = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.AuthenticatedWrite);
        var second = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.AuthenticatedWrite);

        using var memberLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => MutationRateLimitPartitions.AuthenticatedWrite(context, options));
        using var ipLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => MutationRateLimitPartitions.AuthenticatedIpSafetyNet(context, options));
        using var combined = PartitionedRateLimiter.CreateChained(memberLimiter, ipLimiter);

        using var accepted = combined.AttemptAcquire(first);
        using var rejected = combined.AttemptAcquire(second);

        Assert.True(accepted.IsAcquired);
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public void AuthenticatedWrite_keeps_member_fairness_below_ip_safety_net()
    {
        var options = new MutationRateLimitingOptions
        {
            AuthenticatedMemberPermitLimit = 1,
            AuthenticatedMemberWindowMinutes = 60,
            AuthenticatedIpPermitLimit = 3,
            AuthenticatedIpWindowMinutes = 60,
        };
        var firstMember = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.AuthenticatedWrite);
        var secondMember = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.AuthenticatedWrite);

        using var memberLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => MutationRateLimitPartitions.AuthenticatedWrite(context, options));
        using var ipLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => MutationRateLimitPartitions.AuthenticatedIpSafetyNet(context, options));
        using var combined = PartitionedRateLimiter.CreateChained(memberLimiter, ipLimiter);

        using var firstAccepted = combined.AttemptAcquire(firstMember);
        using var firstMemberRejected = combined.AttemptAcquire(firstMember);
        using var secondAccepted = combined.AttemptAcquire(secondMember);

        Assert.True(firstAccepted.IsAcquired);
        Assert.False(firstMemberRejected.IsAcquired);
        Assert.True(secondAccepted.IsAcquired);
    }

    [Fact]
    public void AuthenticatedIpSafetyNet_is_noop_for_other_policies()
    {
        var options = new MutationRateLimitingOptions
        {
            AuthenticatedIpPermitLimit = 1,
            AuthenticatedIpWindowMinutes = 60,
        };
        var context = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.MemberWrite);

        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            current => MutationRateLimitPartitions.AuthenticatedIpSafetyNet(current, options));

        using var first = limiter.AttemptAcquire(context);
        using var second = limiter.AttemptAcquire(context);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public void ClientKey_does_not_share_unknown_bucket_without_network_metadata()
    {
        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();
        first.Connection.Id = string.Empty;
        second.Connection.Id = string.Empty;

        var firstKey = MutationRateLimitPartitions.ClientKey(first);
        var secondKey = MutationRateLimitPartitions.ClientKey(second);

        Assert.StartsWith("trace:", firstKey, StringComparison.Ordinal);
        Assert.StartsWith("trace:", secondKey, StringComparison.Ordinal);
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void AnonymousWrite_uses_configured_limit_and_zero_queue()
    {
        var options = new MutationRateLimitingOptions
        {
            AnonymousPermitLimit = 1,
            AnonymousWindowMinutes = 60,
        };
        var context = CreateContext("203.0.113.10");
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            current => MutationRateLimitPartitions.AnonymousWrite(current, options));

        using var first = limiter.AttemptAcquire(context);
        using var second = limiter.AttemptAcquire(context);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(second.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void Mutation_policies_do_not_limit_safe_methods(string method)
    {
        var options = new MutationRateLimitingOptions
        {
            AnonymousPermitLimit = 1,
            AuthenticatedMemberPermitLimit = 1,
            AuthenticatedIpPermitLimit = 1,
        };
        var context = CreateContext(
            "203.0.113.10",
            Guid.NewGuid(),
            QueenZoneRateLimitPolicies.AuthenticatedWrite);
        context.Request.Method = method;

        var anonymousPartition = MutationRateLimitPartitions.AnonymousWrite(context, options);
        var authenticatedPartition = MutationRateLimitPartitions.AuthenticatedWrite(context, options);
        var safetyNetPartition = MutationRateLimitPartitions.AuthenticatedIpSafetyNet(context, options);
        using var anonymous = anonymousPartition.Factory(anonymousPartition.PartitionKey);
        using var authenticated = authenticatedPartition.Factory(authenticatedPartition.PartitionKey);
        using var safetyNet = safetyNetPartition.Factory(safetyNetPartition.PartitionKey);

        using var anonymousLease = anonymous.AttemptAcquire(2);
        using var authenticatedLease = authenticated.AttemptAcquire(2);
        using var safetyNetLease = safetyNet.AttemptAcquire(2);
        Assert.True(anonymousLease.IsAcquired);
        Assert.True(authenticatedLease.IsAcquired);
        Assert.True(safetyNetLease.IsAcquired);
    }

    private static DefaultHttpContext CreateContext(
        string ip,
        Guid? memberId = null,
        string? policyName = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        if (memberId is Guid id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())],
                "test"));
        }

        if (policyName is not null)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new EnableRateLimitingAttribute(policyName)),
                "mutation test"));
        }

        return context;
    }
}

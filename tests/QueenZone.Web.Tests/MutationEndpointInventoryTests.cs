using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class MutationEndpointInventoryTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MutationEndpointInventoryTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Inventory_CurrentApplication_HasNoUnclassifiedMutations()
    {
        var endpoints = GetEndpoints();
        var failures = MutationAbuseControlContract.Evaluate(
            endpoints,
            MutationAbuseControlDecisions.Production,
            factory.Services);

        Assert.True(
            failures.Count == 0,
            MutationAbuseControlContract.FormatFailures(failures));
    }

    [Fact]
    public void Inventory_CurrentApplication_ContainsAllSurfaceKinds()
    {
        var candidates = MutationAbuseControlContract.Discover(GetEndpoints(), factory.Services);

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, candidate => candidate.PagePath is not null);
        Assert.Contains(
            candidates,
            candidate => candidate.PagePath is null
                && candidate.Key.RoutePattern.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            candidates,
            candidate => candidate.PagePath is null
                && !candidate.Key.RoutePattern.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.PagePath, "/Account/Settings", StringComparison.OrdinalIgnoreCase)
                && candidate.HasNamedControl);
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && candidate.PagePath?.StartsWith("/Submit/", StringComparison.OrdinalIgnoreCase) == true
                && candidate.HasNamedControl);
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.Key.Handler, "Watch", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PagePath, "/Forum/Topic", StringComparison.OrdinalIgnoreCase)
                && candidate.HasNamedControl);
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.Key.Handler, "Unwatch", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PagePath, "/Forum/TopicPage", StringComparison.OrdinalIgnoreCase)
                && candidate.HasNamedControl);
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && candidate.Key.RoutePattern.Contains("api/v1/auth/token", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PolicyName, QueenZoneRateLimitPolicies.Auth, StringComparison.Ordinal));
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.PagePath, "/Account/Logout", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PolicyName, QueenZoneRateLimitPolicies.Auth, StringComparison.Ordinal));
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.PagePath, "/Account/LinkExternalLogin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Key.Handler, "Password", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PolicyName, QueenZoneRateLimitPolicies.Auth, StringComparison.Ordinal));
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && string.Equals(candidate.PagePath, "/Trivia/Index", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Key.Handler, "Next", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            candidates,
            candidate => candidate.Key.Method == "POST"
                && candidate.Key.RoutePattern.Contains(
                    "api/v1/me/forum/posts/moderation-state",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inventory_TestOnlyUnprotectedMutation_FailsTheGuard()
    {
        var endpoints = GetEndpoints().Append(
            MutationContractEndpoints.Minimal("POST", "/__contract/unprotected")).ToList();

        var failures = MutationAbuseControlContract.Evaluate(
            endpoints,
            MutationAbuseControlDecisions.Production,
            factory.Services);

        var unexpected = failures
            .Where(failure => failure.Key.RoutePattern != "/__contract/unprotected")
            .ToList();
        Assert.True(
            unexpected.Count == 0,
            MutationAbuseControlContract.FormatFailures(unexpected));
        var failure = Assert.Single(failures);
        Assert.Equal("POST", failure.Key.Method);
        Assert.Equal("/__contract/unprotected", failure.Key.RoutePattern);
        Assert.Contains("missing effective named rate-limit policy", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_DocumentedServiceLevelException_PassesTheGuard()
    {
        var send = MutationContractEndpoints.Minimal("POST", "/__contract/pm-send");
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            PersistentExceptions =
            [
                new PersistentControlException(
                    MutationKey.Create("POST", "/__contract/pm-send", null),
                    typeof(PrivateMessageRateLimiter),
                    nameof(PrivateMessageRateLimiter.IsSendAllowedAsync),
                    "Repository-backed volume, duplicate, and fan-out checks in PrivateMessageRateLimiter.IsSendAllowedAsync.",
                    nameof(PrivateMessageRateLimiterTests)),
            ],
        };

        var failures = MutationAbuseControlContract.Evaluate([send], decisions);

        Assert.Empty(failures);
        Assert.Contains("volume", decisions.PersistentExceptions[0].Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate", decisions.PersistentExceptions[0].Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fan-out", decisions.PersistentExceptions[0].Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inventory_AuthTokenInheritsEnclosingAuthPolicy()
    {
        var token = Assert.Single(
            MutationAbuseControlContract.Discover(GetEndpoints(), factory.Services),
            candidate => candidate.Key.Method == "POST"
                && candidate.Key.RoutePattern.Contains("/api/v1/auth/token", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(QueenZoneRateLimitPolicies.Auth, token.PolicyName);
        Assert.True(token.HasNamedControl);
        Assert.NotNull(token.Endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>());
    }

    private IReadOnlyList<Endpoint> GetEndpoints()
    {
        using var client = factory.CreateAnonymousClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class MutationAbuseControlContractTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Evaluate_UnsafeMethodWithoutControl_ReportsViolation(string method)
    {
        var endpoint = MutationContractEndpoints.Minimal(method, "/items");

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        var failure = Assert.Single(failures);
        Assert.Equal(method, failure.Key.Method);
        Assert.Equal("/items", failure.Key.RoutePattern);
        Assert.Contains("missing effective named rate-limit policy", failure.Message, StringComparison.Ordinal);
        Assert.Contains(method, MutationAbuseControlContract.FormatFailures(failures), StringComparison.Ordinal);
        Assert.Contains("/items", MutationAbuseControlContract.FormatFailures(failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_UnrestrictedMethodRoute_ReportsAllMutationVerbs()
    {
        var endpoint = MutationContractEndpoints.Minimal(httpMethods: [], route: "/open");

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.Equal(
            MutationAbuseControlContract.MutationMethods.OrderBy(method => method, StringComparer.Ordinal),
            failures.Select(failure => failure.Key.Method).OrderBy(method => method, StringComparer.Ordinal));
        Assert.All(failures, failure => Assert.Equal("/open", failure.Key.RoutePattern));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void Evaluate_SafeMethodOnly_Passes(string method)
    {
        var endpoint = MutationContractEndpoints.Minimal(method, "/safe");

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.Empty(failures);
    }

    [Fact]
    public void Evaluate_NamedPolicy_Passes()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/writes",
            new EnableRateLimitingAttribute(QueenZoneRateLimitPolicies.AuthenticatedWrite));

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.Empty(failures);
    }

    [Fact]
    public void Evaluate_GroupMetadata_IsAcceptedAsEnclosingPolicy()
    {
        var inherited = MutationContractEndpoints.Minimal(
            "POST",
            "/group/child",
            new EnableRateLimitingAttribute(QueenZoneRateLimitPolicies.Auth));
        var overridden = MutationContractEndpoints.Minimal(
            "POST",
            "/group/override",
            new EnableRateLimitingAttribute(QueenZoneRateLimitPolicies.Auth),
            new EnableRateLimitingAttribute(QueenZoneRateLimitPolicies.AuthenticatedWrite));

        Assert.Empty(MutationAbuseControlContract.Evaluate([inherited], MutationAbuseControlDecisions.Empty));
        var inspection = MutationAbuseControlContract.InspectRateLimit(overridden);
        Assert.Equal(QueenZoneRateLimitPolicies.AuthenticatedWrite, inspection.PolicyName);
        Assert.True(inspection.HasNamedControl);
        Assert.Empty(MutationAbuseControlContract.Evaluate([overridden], MutationAbuseControlDecisions.Empty));
    }

    [Fact]
    public void Evaluate_DisabledNamedPolicy_ReportsViolation()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/disabled",
            new EnableRateLimitingAttribute(QueenZoneRateLimitPolicies.Auth),
            new DisableRateLimitingAttribute());

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        var failure = Assert.Single(failures);
        Assert.Contains("explicitly disabled", failure.Message, StringComparison.Ordinal);
        Assert.False(MutationAbuseControlContract.InspectRateLimit(endpoint).HasNamedControl);
    }

    [Fact]
    public void Evaluate_MemberAuthorizationOnly_ReportsViolation()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/member-only",
            new AuthorizeAttribute(MemberAuthenticationSchemes.MemberPolicy));

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        var failure = Assert.Single(failures);
        Assert.Equal("/member-only", failure.Key.RoutePattern);
        Assert.Contains("missing effective named rate-limit policy", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_AllowAnonymousOverridesAdminMetadata_ReportsViolation()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/admin-anonymous",
            new AuthorizeAttribute(AdminAuthenticationSchemes.Policy),
            new AllowAnonymousAttribute());

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.False(MutationAbuseControlContract.IsAdminOnly(endpoint));
        Assert.Single(failures);
    }

    [Fact]
    public void Evaluate_AdminOnlyPolicy_IsOutOfScope()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/admin/news",
            new AuthorizeAttribute(AdminAuthenticationSchemes.Policy));

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.True(MutationAbuseControlContract.IsAdminOnly(endpoint));
        Assert.Empty(failures);
    }

    [Fact]
    public void Evaluate_MixedMemberAdminAuthoring_RemainsInScope()
    {
        var endpoint = MutationContractEndpoints.Minimal(
            "POST",
            "/authoring",
            new AuthorizeAttribute(AdminAuthenticationSchemes.Policy),
            new AuthorizeAttribute("Authoring"));

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);

        Assert.False(MutationAbuseControlContract.IsAdminOnly(endpoint));
        Assert.Single(failures);
    }

    [Fact]
    public void Evaluate_UnprotectedNamedPageHandler_ReportsHandlerInFailure()
    {
        var endpoint = MutationContractEndpoints.Page(
            "/account/demo",
            "/Account/Demo",
            [("POST", "Submit")]);

        var failures = MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty);
        var formatted = MutationAbuseControlContract.FormatFailures(failures);

        var failure = Assert.Single(failures);
        Assert.Equal("POST", failure.Key.Method);
        Assert.Equal("/account/demo", failure.Key.RoutePattern);
        Assert.Equal("Submit", failure.Key.Handler);
        Assert.Equal("/Account/Demo", failure.PagePath);
        Assert.Contains("handler=Submit", formatted, StringComparison.Ordinal);
        Assert.Contains("/account/demo", formatted, StringComparison.Ordinal);
        Assert.Contains("Page: /Account/Demo.", formatted, StringComparison.Ordinal);
        Assert.Contains("missing effective named rate-limit policy", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReadOnlyClassification_DoesNotCoverSiblingHandler()
    {
        var next = MutationContractEndpoints.Page("/Trivia/Index", "/Trivia/Index", [("POST", "Next")]);
        var submit = MutationContractEndpoints.Page("/Trivia/Index", "/Trivia/Index", [("POST", "Submit")]);
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            ReadOnlyClassifications =
            [
                new ReadOnlyClassification(
                    MutationKey.Create("POST", "/Trivia/Index", "Next"),
                    "Loads another published trivia fact."),
            ],
        };

        Assert.Empty(MutationAbuseControlContract.Evaluate([next], decisions));
        var failures = MutationAbuseControlContract.Evaluate([next, submit], decisions);
        var submitFailure = Assert.Single(failures);
        Assert.Equal("Submit", submitFailure.Key.Handler);
    }

    [Fact]
    public void Evaluate_ExactPersistentException_Passes()
    {
        var endpoint = MutationContractEndpoints.Minimal("POST", "/__contract/pm-send");
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            PersistentExceptions = [CreatePrivateMessageException("/__contract/pm-send")],
        };

        var failures = MutationAbuseControlContract.Evaluate([endpoint], decisions);

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("POST", "/__contract/pm-send-other", null)]
    [InlineData("PUT", "/__contract/pm-send", null)]
    [InlineData("POST", "/__contract/pm-send", "Extra")]
    public void Evaluate_PersistentException_DoesNotMatchSiblingVerbOrHandler(
        string method,
        string route,
        string? handler)
    {
        var endpoint = handler is null
            ? MutationContractEndpoints.Minimal(method, route)
            : MutationContractEndpoints.Page(route, "/Contract/Send", [(method, handler)]);
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            PersistentExceptions = [CreatePrivateMessageException("/__contract/pm-send")],
        };

        var failures = MutationAbuseControlContract.Evaluate([endpoint], decisions);

        Assert.Contains(
            failures,
            failure => MutationKeyComparer.Instance.Equals(
                failure.Key,
                MutationKey.Create(method, route, handler)));
    }

    [Fact]
    public void ValidateDecisions_RejectsWildcardDuplicateIncompleteAndStaleEntries()
    {
        var live = MutationContractEndpoints.Minimal("POST", "/live");
        var candidates = MutationAbuseControlContract.Discover([live]);
        var decisions = new MutationAbuseControlDecisions(
            ReadOnlyClassifications:
            [
                new ReadOnlyClassification(MutationKey.Create("POST", "/items/*", null), "wildcard"),
                new ReadOnlyClassification(MutationKey.Create("POST", "/live", null), ""),
                new ReadOnlyClassification(MutationKey.Create("POST", "/live", null), "duplicate"),
                new ReadOnlyClassification(MutationKey.Create("POST", "/gone", null), "stale read"),
            ],
            PersistentExceptions:
            [
                new PersistentControlException(
                    MutationKey.Create("POST", "/incomplete", null),
                    typeof(PrivateMessageRateLimiter),
                    "",
                    "",
                    ""),
            ],
            InfrastructureExclusions:
            [
                new InfrastructureExclusion("stale probe", ExactRoutePattern: "/health/custom-missing"),
                new InfrastructureExclusion("", FallbackRoutePattern: "{*path:nonfile}"),
                new InfrastructureExclusion("both", ExactRoutePattern: "/health", FallbackRoutePattern: "{*path:nonfile}"),
            ]);

        var failures = MutationAbuseControlContract.ValidateDecisions(decisions, candidates, [live]);
        var formatted = MutationAbuseControlContract.FormatFailures(failures);

        Assert.Contains("Wildcard", formatted, StringComparison.Ordinal);
        Assert.Contains("missing a concise rationale", formatted, StringComparison.Ordinal);
        Assert.Contains("Duplicate", formatted, StringComparison.Ordinal);
        Assert.Contains("Stale read-only classification", formatted, StringComparison.Ordinal);
        Assert.Contains("Persistent-control exception must name a real service method", formatted, StringComparison.Ordinal);
        Assert.Contains("Stale infrastructure exclusion", formatted, StringComparison.Ordinal);
        Assert.Contains("either an exact route or a Fallback display-name route", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_KnownHealthProbes_AreExcluded()
    {
        var health = MutationContractEndpoints.Minimal("GET", "/health");
        var ready = MutationContractEndpoints.Unrestricted("/health/ready");
        var warmup = MutationContractEndpoints.Minimal("GET", "/warmup");
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            InfrastructureExclusions = MutationAbuseControlDecisions.Production.InfrastructureExclusions
                .Where(exclusion => exclusion.ExactRoutePattern is not null)
                .ToArray(),
        };

        var failures = MutationAbuseControlContract.Evaluate([health, ready, warmup], decisions);

        Assert.Empty(failures);
        Assert.Equal(4, MutationAbuseControlContract.Discover([ready]).Count);
    }

    [Fact]
    public void Evaluate_NotFoundFallback_IsExcluded_ButCatchAllPostIsNot()
    {
        var fallback = MutationContractEndpoints.NotFoundFallback();
        var catchAll = MutationContractEndpoints.Minimal("POST", "{*anything}");
        var customHealth = MutationContractEndpoints.Minimal("POST", "/health/custom-write");
        var decisions = MutationAbuseControlDecisions.Empty with
        {
            InfrastructureExclusions =
            [
                new InfrastructureExclusion("Framework fallback.", FallbackRoutePattern: "{*path:nonfile}"),
            ],
        };

        var failures = MutationAbuseControlContract.Evaluate([fallback, catchAll, customHealth], decisions);

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.Key.RoutePattern == "{*anything}");
        Assert.Contains(failures, failure => failure.Key.RoutePattern == "/health/custom-write");
        Assert.True(MutationAbuseControlContract.IsFrameworkFallback(fallback, "{*path:nonfile}"));
        Assert.DoesNotContain(
            failures,
            failure => failure.Key.RoutePattern == MutationAbuseControlContract.GetRoutePattern(fallback));
    }

    [Fact]
    public void Discover_UncompiledPage_WithoutLoader_FailsExplicitly()
    {
        var endpoint = MutationContractEndpoints.UncompiledPage("/account/login", "/Account/Login");

        var exception = Assert.Throws<InvalidOperationException>(
            () => MutationAbuseControlContract.Discover([endpoint]));

        Assert.Contains("/Account/Login", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(PageLoader), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_UncompiledPage_UsesPageLoader()
    {
        var compiled = MutationContractEndpoints.CreateCompiledPage(
            "/Account/Login",
            [("POST", null)]);
        var services = new ServiceCollection()
            .AddSingleton<PageLoader>(new StubPageLoader(compiled))
            .BuildServiceProvider();
        var endpoint = MutationContractEndpoints.UncompiledPage("/account/login", "/Account/Login");

        var candidates = MutationAbuseControlContract.Discover([endpoint], services);

        var candidate = Assert.Single(candidates);
        Assert.Equal("POST", candidate.Key.Method);
        Assert.Equal("/account/login", candidate.Key.RoutePattern);
        Assert.Equal("/Account/Login", candidate.PagePath);
    }

    [Fact]
    public void FormatFailures_IncludesMethodRouteHandlerAndMissingControl()
    {
        var endpoint = MutationContractEndpoints.Page(
            "/account/link-external-login",
            "/Account/LinkExternalLogin",
            [("POST", "Password")]);

        var formatted = MutationAbuseControlContract.FormatFailures(
            MutationAbuseControlContract.Evaluate([endpoint], MutationAbuseControlDecisions.Empty));

        Assert.Contains("POST /account/link-external-login [handler=Password]:", formatted, StringComparison.Ordinal);
        Assert.Contains("missing effective named rate-limit policy or documented persistent control.", formatted, StringComparison.Ordinal);
        Assert.Contains("Page: /Account/LinkExternalLogin.", formatted, StringComparison.Ordinal);
        Assert.Contains("Add endpoint/page policy metadata or an exact reviewed control exception.", formatted, StringComparison.Ordinal);
    }

    private static PersistentControlException CreatePrivateMessageException(string route) =>
        new(
            MutationKey.Create("POST", route, null),
            typeof(PrivateMessageRateLimiter),
            nameof(PrivateMessageRateLimiter.IsSendAllowedAsync),
            "Repository-backed volume, duplicate, and fan-out checks in PrivateMessageRateLimiter.IsSendAllowedAsync.",
            nameof(PrivateMessageRateLimiterTests));

    private sealed class StubPageLoader(CompiledPageActionDescriptor compiled) : PageLoader
    {
        [Obsolete]
        public override Task<CompiledPageActionDescriptor> LoadAsync(PageActionDescriptor actionDescriptor) =>
            LoadAsync(actionDescriptor, EndpointMetadataCollection.Empty);

        public override Task<CompiledPageActionDescriptor> LoadAsync(
            PageActionDescriptor actionDescriptor,
            EndpointMetadataCollection endpointMetadata) =>
            Task.FromResult(compiled);
    }
}

internal static class MutationContractEndpoints
{
    public static RouteEndpoint Minimal(string method, string route, params object[] metadata) =>
        Minimal([method], route, metadata);

    public static RouteEndpoint Minimal(IReadOnlyList<string> httpMethods, string route, params object[] metadata)
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            order: 0)
        {
            DisplayName = route,
        };
        if (httpMethods.Count > 0)
        {
            builder.Metadata.Add(new HttpMethodMetadata(httpMethods));
        }

        foreach (var item in metadata)
        {
            builder.Metadata.Add(item);
        }

        return (RouteEndpoint)builder.Build();
    }

    public static RouteEndpoint Unrestricted(string route) => Minimal([], route);

    public static RouteEndpoint Page(
        string route,
        string pagePath,
        IReadOnlyList<(string Method, string? Handler)> handlers,
        params object[] metadata)
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            order: 0)
        {
            DisplayName = pagePath,
        };
        builder.Metadata.Add(CreateCompiledPage(pagePath, handlers));
        builder.Metadata.Add(new HttpMethodMetadata(handlers.Select(handler => handler.Method).Distinct().ToArray()));
        foreach (var item in metadata)
        {
            builder.Metadata.Add(item);
        }

        return (RouteEndpoint)builder.Build();
    }

    public static RouteEndpoint UncompiledPage(string route, string pagePath)
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            order: 0);
        builder.Metadata.Add(new PageActionDescriptor { ViewEnginePath = pagePath });
        return (RouteEndpoint)builder.Build();
    }

    public static RouteEndpoint NotFoundFallback()
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("{*path:nonfile}"),
            order: 0)
        {
            DisplayName = "Fallback {*path:nonfile}",
        };
        builder.Metadata.Add(CreateCompiledPage("/NotFound", [("GET", null)]));
        return (RouteEndpoint)builder.Build();
    }

    public static CompiledPageActionDescriptor CreateCompiledPage(
        string pagePath,
        IReadOnlyList<(string Method, string? Handler)> handlers) =>
        new()
        {
            ViewEnginePath = pagePath,
            RelativePath = $"/Pages{pagePath}.cshtml",
            HandlerMethods = handlers
                .Select(handler => new HandlerMethodDescriptor
                {
                    HttpMethod = handler.Method,
                    Name = handler.Handler ?? string.Empty,
                    MethodInfo = DummyHandlerMethod,
                })
                .ToList(),
        };

    private static readonly MethodInfo DummyHandlerMethod =
        typeof(MutationContractEndpoints).GetMethod(nameof(DummyHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void DummyHandler()
    {
    }
}

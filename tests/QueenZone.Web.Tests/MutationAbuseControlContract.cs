using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

internal readonly record struct MutationKey(string Method, string RoutePattern, string? Handler)
{
    public static MutationKey Create(string method, string routePattern, string? handler) =>
        new(NormalizeMethod(method), routePattern.Trim(), NormalizeHandler(handler));

    public string Display() =>
        string.IsNullOrEmpty(Handler)
            ? $"{Method} {RoutePattern}"
            : $"{Method} {RoutePattern} [handler={Handler}]";

    internal static string NormalizeMethod(string method) => method.Trim().ToUpperInvariant();

    internal static string? NormalizeHandler(string? handler) =>
        string.IsNullOrWhiteSpace(handler) ? null : handler.Trim();
}

internal sealed class MutationKeyComparer : IEqualityComparer<MutationKey>
{
    public static readonly MutationKeyComparer Instance = new();

    public bool Equals(MutationKey x, MutationKey y) =>
        string.Equals(x.Method, y.Method, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.RoutePattern, y.RoutePattern, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Handler, y.Handler, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(MutationKey obj) =>
        HashCode.Combine(
            obj.Method.ToUpperInvariant(),
            obj.RoutePattern.ToUpperInvariant(),
            obj.Handler?.ToUpperInvariant());
}

internal sealed record PersistentControlException(
    MutationKey Key,
    Type ServiceType,
    string MethodName,
    string Rationale,
    string EvidenceTest);

internal sealed record ReadOnlyClassification(MutationKey Key, string Rationale);

internal sealed record InfrastructureExclusion(
    string Rationale,
    string? ExactRoutePattern = null,
    string? FallbackRoutePattern = null);

internal sealed record MutationCandidate(
    MutationKey Key,
    Endpoint Endpoint,
    string? PagePath,
    bool IsAdminOnly,
    bool HasNamedControl,
    string? PolicyName,
    bool RateLimitingDisabled);

internal sealed record MutationDecisionFailure(MutationKey Key, string Message, string? PagePath);

internal sealed record MutationAbuseControlDecisions(
    IReadOnlyList<ReadOnlyClassification> ReadOnlyClassifications,
    IReadOnlyList<PersistentControlException> PersistentExceptions,
    IReadOnlyList<InfrastructureExclusion> InfrastructureExclusions)
{
    public static MutationAbuseControlDecisions Empty { get; } = new([], [], []);

    public static MutationAbuseControlDecisions Production { get; } = new(
        ReadOnlyClassifications:
        [
            new ReadOnlyClassification(
                MutationKey.Create("POST", "/api/v1/me/forum/posts/moderation-state", null),
                "Bounded report/block lookup for the current forum page; not a write."),
            new ReadOnlyClassification(
                MutationKey.Create("POST", "/Trivia/Index", "Next"),
                "Loads another published trivia fact; does not persist state. Conventional Razor route."),
            new ReadOnlyClassification(
                MutationKey.Create("POST", "trivia", "Next"),
                "Loads another published trivia fact; does not persist state. @page template alias."),
        ],
        PersistentExceptions: [],
        InfrastructureExclusions:
        [
            new InfrastructureExclusion(
                "Liveness probe short-circuited before rate limiting.",
                ExactRoutePattern: "/health"),
            new InfrastructureExclusion(
                "Readiness MapHealthChecks probe; excluded by exact identity even when method metadata is absent.",
                ExactRoutePattern: "/health/ready"),
            new InfrastructureExclusion(
                "Deploy warmup probe; excluded by exact identity.",
                ExactRoutePattern: "/warmup"),
            new InfrastructureExclusion(
                "Framework-generated MapFallbackToPage(\"/NotFound\") catch-all; not a product mutation.",
                FallbackRoutePattern: "{*path:nonfile}"),
            new InfrastructureExclusion(
                "API v1 MapFallback 404 for unmatched /api/v1 paths; not a product mutation.",
                FallbackRoutePattern: "/api/v1/{*path}"),
        ]);
}

internal static class MutationAbuseControlContract
{
    internal static readonly string[] MutationMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private static readonly HashSet<string> MutationMethodSet = new(MutationMethods, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MutationCandidate> Discover(
        IEnumerable<Endpoint> endpoints,
        IServiceProvider? services = null)
    {
        var candidates = new List<MutationCandidate>();
        foreach (var endpoint in endpoints)
        {
            candidates.AddRange(GetMutationCandidates(endpoint, services));
        }

        // Keep every endpoint. DistinctBy-first would drop an unprotected sibling
        // when a protected or admin-only endpoint with the same key appears first.
        return candidates;
    }

    public static IReadOnlyList<MutationCandidate> GetMutationCandidates(
        Endpoint endpoint,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var page = endpoint.Metadata.GetMetadata<PageActionDescriptor>();
        if (page is not null)
        {
            return DiscoverPageMutations(endpoint, page, services).ToList();
        }

        return DiscoverNonPageMutations(endpoint).ToList();
    }

    public static IReadOnlyList<MutationDecisionFailure> Evaluate(
        IEnumerable<Endpoint> endpoints,
        MutationAbuseControlDecisions decisions,
        IServiceProvider? services = null)
    {
        var endpointList = endpoints as IReadOnlyList<Endpoint> ?? endpoints.ToList();
        var candidates = Discover(endpointList, services);
        return Evaluate(candidates, decisions, endpointList);
    }

    public static IReadOnlyList<MutationDecisionFailure> Evaluate(
        IReadOnlyList<MutationCandidate> candidates,
        MutationAbuseControlDecisions decisions,
        IReadOnlyList<Endpoint>? endpointsForValidation = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(decisions);

        var failures = new List<MutationDecisionFailure>();
        failures.AddRange(ValidateDecisions(decisions, candidates, endpointsForValidation));

        var readOnly = new HashSet<MutationKey>(
            decisions.ReadOnlyClassifications.Select(entry => entry.Key),
            MutationKeyComparer.Instance);
        var persistent = new HashSet<MutationKey>(
            decisions.PersistentExceptions.Select(entry => entry.Key),
            MutationKeyComparer.Instance);

        foreach (var candidate in candidates)
        {
            if (candidate.IsAdminOnly || IsInfrastructure(candidate, decisions))
            {
                continue;
            }

            if (readOnly.Contains(candidate.Key) || persistent.Contains(candidate.Key) || candidate.HasNamedControl)
            {
                continue;
            }

            failures.Add(new MutationDecisionFailure(
                candidate.Key,
                FormatMissingControl(candidate),
                candidate.PagePath));
        }

        return failures
            .OrderBy(failure => failure.Key.RoutePattern, StringComparer.OrdinalIgnoreCase)
            .ThenBy(failure => failure.Key.Method, StringComparer.OrdinalIgnoreCase)
            .ThenBy(failure => failure.Key.Handler ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(failure => failure.Key, MutationKeyComparer.Instance)
            .ToList();
    }

    public static IReadOnlyList<MutationDecisionFailure> ValidateDecisions(
        MutationAbuseControlDecisions decisions,
        IReadOnlyList<MutationCandidate> candidates,
        IReadOnlyList<Endpoint>? endpoints = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(candidates);

        var failures = new List<MutationDecisionFailure>();
        var seen = new HashSet<MutationKey>(MutationKeyComparer.Instance);

        foreach (var entry in decisions.ReadOnlyClassifications)
        {
            ValidateMutationDecision(
                failures,
                seen,
                entry.Key,
                entry.Rationale,
                "read-only classification",
                candidates,
                serviceType: null,
                methodName: null,
                evidenceTest: null);
        }

        foreach (var entry in decisions.PersistentExceptions)
        {
            ValidateMutationDecision(
                failures,
                seen,
                entry.Key,
                entry.Rationale,
                "persistent-control exception",
                candidates,
                entry.ServiceType,
                entry.MethodName,
                entry.EvidenceTest);
        }

        var seenInfrastructure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exclusion in decisions.InfrastructureExclusions)
        {
            ValidateInfrastructureExclusion(failures, seenInfrastructure, exclusion, endpoints, candidates);
        }

        return failures;
    }

    public static string FormatFailures(IReadOnlyList<MutationDecisionFailure> failures)
    {
        if (failures.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            failures.Select(failure =>
            {
                var page = string.IsNullOrEmpty(failure.PagePath)
                    ? string.Empty
                    : $"{Environment.NewLine}Page: {failure.PagePath}.";
                return $"{failure.Key.Display()}:{Environment.NewLine}{failure.Message}{page}";
            }));
    }

    internal static bool HasWildcard(string routePattern) =>
        routePattern.Contains('*', StringComparison.Ordinal);

    internal static bool IsMutationMethod(string method) =>
        MutationMethodSet.Contains(MutationKey.NormalizeMethod(method));

    internal static RateLimitInspection InspectRateLimit(Endpoint endpoint)
    {
        var disabled = endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null;
        var policy = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var hasNamedControl = !disabled && !string.IsNullOrWhiteSpace(policy);
        return new RateLimitInspection(hasNamedControl, policy, disabled);
    }

    internal static bool IsAdminOnly(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return false;
        }

        var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (authorize.Count == 0)
        {
            return false;
        }

        var hasAdmin = authorize.Any(data =>
            string.Equals(data.Policy, AdminAuthenticationSchemes.Policy, StringComparison.Ordinal));
        if (!hasAdmin)
        {
            return false;
        }

        var mixed = authorize.Any(data =>
            string.Equals(data.Policy, MemberAuthenticationSchemes.MemberPolicy, StringComparison.Ordinal)
            || string.Equals(data.Policy, MemberAuthenticationSchemes.MobileMemberPolicy, StringComparison.Ordinal)
            || string.Equals(data.Policy, "Authoring", StringComparison.Ordinal));
        return !mixed;
    }

    internal static bool IsFrameworkFallback(Endpoint endpoint, string routePattern)
    {
        var displayName = endpoint.DisplayName ?? string.Empty;
        return displayName.StartsWith("Fallback", StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetRoutePattern(endpoint), routePattern, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetRoutePattern(Endpoint endpoint)
    {
        if (endpoint is RouteEndpoint route)
        {
            return route.RoutePattern.RawText
                ?? route.RoutePattern.ToString()
                ?? route.DisplayName
                ?? "(unknown)";
        }

        return endpoint.DisplayName ?? "(unknown)";
    }

    internal static string? GetPagePath(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath;

    private static IEnumerable<MutationCandidate> DiscoverPageMutations(
        Endpoint endpoint,
        PageActionDescriptor descriptor,
        IServiceProvider? services)
    {
        var compiled = EnsureCompiled(descriptor, endpoint, services);
        var accepted = GetDeclaredMethods(endpoint);
        var inspection = InspectRateLimit(endpoint);
        var adminOnly = IsAdminOnly(endpoint);
        var pagePath = compiled.ViewEnginePath;
        var routePattern = GetRoutePattern(endpoint);
        var handlers = compiled.HandlerMethods
            ?? throw new InvalidOperationException(
                $"Compiled page '{pagePath}' has no handler method list.");

        foreach (var handler in handlers)
        {
            if (string.IsNullOrWhiteSpace(handler.HttpMethod) || !IsMutationMethod(handler.HttpMethod))
            {
                continue;
            }

            var method = MutationKey.NormalizeMethod(handler.HttpMethod);
            if (accepted.Count > 0 && !accepted.Contains(method))
            {
                continue;
            }

            yield return new MutationCandidate(
                MutationKey.Create(method, routePattern, handler.Name),
                endpoint,
                pagePath,
                adminOnly,
                inspection.HasNamedControl,
                inspection.PolicyName,
                inspection.Disabled);
        }
    }

    private static IEnumerable<MutationCandidate> DiscoverNonPageMutations(Endpoint endpoint)
    {
        var declared = GetDeclaredMethods(endpoint);
        var methods = declared.Count == 0
            ? MutationMethods
            : declared.Where(IsMutationMethod).ToArray();
        var inspection = InspectRateLimit(endpoint);
        var adminOnly = IsAdminOnly(endpoint);
        var routePattern = GetRoutePattern(endpoint);

        foreach (var method in methods)
        {
            yield return new MutationCandidate(
                MutationKey.Create(method, routePattern, null),
                endpoint,
                PagePath: null,
                adminOnly,
                inspection.HasNamedControl,
                inspection.PolicyName,
                inspection.Disabled);
        }
    }

    private static CompiledPageActionDescriptor EnsureCompiled(
        PageActionDescriptor descriptor,
        Endpoint endpoint,
        IServiceProvider? services)
    {
        if (descriptor is CompiledPageActionDescriptor compiled)
        {
            return compiled;
        }

        var loader = services?.GetService<PageLoader>()
            ?? throw new InvalidOperationException(
                $"Page '{descriptor.ViewEnginePath}' is not compiled and no {nameof(PageLoader)} is available to resolve it.");
        var loaded = loader.LoadAsync(descriptor, endpoint.Metadata).GetAwaiter().GetResult();
        return loaded
            ?? throw new InvalidOperationException(
                $"PageLoader returned no compiled descriptor for '{descriptor.ViewEnginePath}'.");
    }

    private static HashSet<string> GetDeclaredMethods(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        if (metadata?.HttpMethods is not { Count: > 0 } methods)
        {
            return [];
        }

        return methods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Select(MutationKey.NormalizeMethod)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateMutationDecision(
        List<MutationDecisionFailure> failures,
        HashSet<MutationKey> seen,
        MutationKey key,
        string rationale,
        string kind,
        IReadOnlyList<MutationCandidate> candidates,
        Type? serviceType,
        string? methodName,
        string? evidenceTest)
    {
        if (HasWildcard(key.RoutePattern))
        {
            failures.Add(new MutationDecisionFailure(
                key,
                $"Wildcard {kind} entries are not allowed.",
                PagePath: null));
            return;
        }

        if (string.IsNullOrWhiteSpace(rationale))
        {
            failures.Add(new MutationDecisionFailure(
                key,
                $"{kind} is missing a concise rationale.",
                PagePath: null));
        }

        if (kind == "persistent-control exception")
        {
            if (serviceType is null
                || string.IsNullOrWhiteSpace(methodName)
                || string.IsNullOrWhiteSpace(evidenceTest)
                || serviceType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null)
            {
                failures.Add(new MutationDecisionFailure(
                    key,
                    "Persistent-control exception must name a real service method and supporting test.",
                    PagePath: null));
            }
        }

        if (!seen.Add(key))
        {
            failures.Add(new MutationDecisionFailure(
                key,
                $"Duplicate {kind} key.",
                PagePath: null));
        }

        if (candidates.Count > 0
            && !candidates.Any(candidate => MutationKeyComparer.Instance.Equals(candidate.Key, key)))
        {
            failures.Add(new MutationDecisionFailure(
                key,
                $"Stale {kind}; no matching routed mutation.",
                PagePath: null));
        }
    }

    private static void ValidateInfrastructureExclusion(
        List<MutationDecisionFailure> failures,
        HashSet<string> seen,
        InfrastructureExclusion exclusion,
        IReadOnlyList<Endpoint>? endpoints,
        IReadOnlyList<MutationCandidate> candidates)
    {
        var hasExact = !string.IsNullOrWhiteSpace(exclusion.ExactRoutePattern);
        var hasFallback = !string.IsNullOrWhiteSpace(exclusion.FallbackRoutePattern);
        var identity = exclusion.FallbackRoutePattern ?? exclusion.ExactRoutePattern ?? "(unspecified)";

        if (hasExact == hasFallback)
        {
            failures.Add(new MutationDecisionFailure(
                MutationKey.Create("POST", identity, null),
                "Infrastructure exclusion must be either an exact route or a Fallback display-name route, not both or neither.",
                PagePath: null));
            return;
        }

        if (string.IsNullOrWhiteSpace(exclusion.Rationale))
        {
            failures.Add(new MutationDecisionFailure(
                MutationKey.Create("POST", identity, null),
                "Infrastructure exclusion is missing a concise rationale.",
                PagePath: null));
        }

        if (!seen.Add(identity))
        {
            failures.Add(new MutationDecisionFailure(
                MutationKey.Create("POST", identity, null),
                "Duplicate infrastructure exclusion.",
                PagePath: null));
        }

        var searchable = endpoints ?? candidates.Select(candidate => candidate.Endpoint).Distinct().ToList();
        if (searchable.Count == 0)
        {
            return;
        }

        var matched = hasFallback
            ? searchable.Any(endpoint => IsFrameworkFallback(endpoint, exclusion.FallbackRoutePattern!))
            : searchable.Any(endpoint =>
                string.Equals(GetRoutePattern(endpoint), exclusion.ExactRoutePattern, StringComparison.OrdinalIgnoreCase));
        if (!matched)
        {
            failures.Add(new MutationDecisionFailure(
                MutationKey.Create("POST", identity, null),
                "Stale infrastructure exclusion; no matching endpoint.",
                PagePath: null));
        }
    }

    private static bool IsInfrastructure(MutationCandidate candidate, MutationAbuseControlDecisions decisions) =>
        decisions.InfrastructureExclusions.Any(exclusion =>
            exclusion.FallbackRoutePattern is { Length: > 0 } fallback
                ? IsFrameworkFallback(candidate.Endpoint, fallback)
                : string.Equals(
                    candidate.Key.RoutePattern,
                    exclusion.ExactRoutePattern,
                    StringComparison.OrdinalIgnoreCase));

    private static string FormatMissingControl(MutationCandidate candidate)
    {
        var missing = candidate.RateLimitingDisabled
            ? "rate limiting is explicitly disabled; this is not protection."
            : "missing effective named rate-limit policy or documented persistent control.";
        return $"{missing}{Environment.NewLine}Add endpoint/page policy metadata or an exact reviewed control exception.";
    }

    internal readonly record struct RateLimitInspection(bool HasNamedControl, string? PolicyName, bool Disabled);
}

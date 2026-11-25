namespace QueenZone.Web;

/// <summary>
/// Named predicates over <see cref="IHostEnvironment.EnvironmentName"/>, so that adding a new
/// automated-test environment (for example <see cref="E2E"/>) is a change in this file instead
/// of every call site that previously compared against an environment name string literal. The
/// deployed dev App Service deliberately remains production-shaped and is not a test host.
/// </summary>
public static class QueenZoneEnvironments
{
    /// <summary>WebApplicationFactory suites: in-memory data/storage, test auth handlers.</summary>
    public const string Testing = "Testing";

    /// <summary>Nightly Playwright suite against the real SQL mirror: test auth handlers, but real data.</summary>
    public const string E2E = "E2E";

    /// <summary>
    /// True when the host should register the local admin handler and
    /// the local member handler, add the member-policy test scheme, and skip the
    /// Entra fail-closed check.
    /// </summary>
    public static bool UsesTestAuth(IHostEnvironment environment) =>
        environment.IsEnvironment(Testing) || environment.IsEnvironment(E2E);

    /// <summary>
    /// True only for the WebApplicationFactory <see cref="Testing"/> host, which uses in-memory
    /// repositories instead of the legacy SQL backend. The nightly <see cref="E2E"/> suite needs
    /// the real SQL mirror (guarded by <see cref="E2EConnectionGuard"/>), so it must not match
    /// this predicate.
    /// </summary>
    public static bool UsesInMemoryData(IHostEnvironment environment) =>
        environment.IsEnvironment(Testing);

    /// <summary>
    /// True for hosts that must never write upload bytes to a real blob container: the
    /// WebApplicationFactory <see cref="Testing"/> host and the nightly <see cref="E2E"/> suite.
    /// E2E exercises real SQL rows and moderation state but keeps blob storage in-memory, so a
    /// nightly run can never write junk objects into production containers.
    /// </summary>
    public static bool UsesInMemoryBlobStorage(IHostEnvironment environment) =>
        environment.IsEnvironment(Testing) || environment.IsEnvironment(E2E);

    /// <summary>
    /// True for hosts running as an automated test suite (WebApplicationFactory or the nightly
    /// Playwright E2E run), where output caching, telemetry, response compression, and the
    /// production HSTS/exception-handler branch should be skipped.
    /// </summary>
    public static bool IsAutomatedTestHost(IHostEnvironment environment) =>
        environment.IsEnvironment(Testing) || environment.IsEnvironment(E2E);

    /// <summary>
    /// True for deployed environments that must fail closed on missing secrets and connection
    /// strings. Development, Testing, and E2E are excluded so local and automated hosts can
    /// boot with empty connection strings and test auth.
    /// </summary>
    public static bool IsProductionLike(IHostEnvironment environment) =>
        environment.IsProduction()
        || environment.IsEnvironment("Staging")
        || environment.IsEnvironment("Preview");
}

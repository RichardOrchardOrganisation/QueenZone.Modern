namespace QueenZone.Web;

/// <summary>
/// Fail-closed startup guard for the in-memory sample data fallback. A production-like host
/// with an empty <c>ConnectionStrings:QueenZoneLegacy</c> would otherwise boot on sample
/// content and keep member writes in process memory. The deployed dev App Service runs
/// production-shaped on sample data until its curated snapshot is ready, so it opts in with
/// <see cref="AllowWithoutDatabaseKey"/>; production never sets it.
/// </summary>
public static class SampleDataFallbackGuard
{
    public const string AllowWithoutDatabaseKey = "SampleData:AllowWithoutDatabase";

    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="environment"/> is production-like and
    /// <see cref="AllowWithoutDatabaseKey"/> is not <c>true</c>.
    /// </exception>
    public static void EnsureAllowed(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!QueenZoneEnvironments.IsProductionLike(environment)
            || configuration.GetValue<bool>(AllowWithoutDatabaseKey))
        {
            return;
        }

        throw new InvalidOperationException(
            $"ConnectionStrings:QueenZoneLegacy is required in {environment.EnvironmentName}. " +
            "Refusing to fall back to in-memory sample data. Configure ConnectionStrings__QueenZoneLegacy, " +
            "or set SampleData__AllowWithoutDatabase=true on a host that is meant to serve sample data.");
    }
}

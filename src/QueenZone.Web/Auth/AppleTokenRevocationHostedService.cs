using System.Diagnostics;

namespace QueenZone.Web;

/// <summary>
/// Revokes Sign in with Apple refresh tokens queued by the scheduled account-deletion purge.
/// The purge runs in <c>QueenZone.Maintenance.Worker</c> (#1677), but the tokens are encrypted
/// with the web app's data-protection key ring, which only this host can read. Registered only
/// when Apple sign-in is configured and the maintenance jobs are not running in the web host.
/// </summary>
public sealed class AppleTokenRevocationHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AppleTokenRevocationHostedService> logger)
    : PeriodicScopedHostedService(scopeFactory, timeProvider, logger, DefaultRunInterval)
{
    /// <summary>Matches the worker's six-hourly account-deletion schedule.</summary>
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(6);

    protected override string FailureMessage => "Sign in with Apple token revocation failed.";

    protected override async Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        using var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
            "AppleTokenRevocation",
            ActivityKind.Internal);
        var tokens = scopedServices.GetRequiredService<AppleAccountTokenService>();
        await tokens.RevokePendingAsync(cancellationToken);
    }
}

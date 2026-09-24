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
    ILogger<AppleTokenRevocationHostedService> logger) : BackgroundService
{
    /// <summary>
    /// Same rationale as <see cref="MemberAccountDeletionHostedService.DefaultStartupDelay"/>:
    /// keep the first outbound call out of the container start probe window (#666).
    /// </summary>
    internal static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>Matches the worker's six-hourly account-deletion schedule.</summary>
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(6);

    internal TimeSpan StartupDelay { get; init; } = DefaultStartupDelay;

    internal TimeSpan RunInterval { get; init; } = DefaultRunInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
                "AppleTokenRevocation",
                ActivityKind.Internal))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var tokens = scope.ServiceProvider.GetRequiredService<AppleAccountTokenService>();
                    await tokens.RevokePendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Sign in with Apple token revocation failed.");
                }
            }

            await Task.Delay(RunInterval, timeProvider, stoppingToken);
        }
    }
}

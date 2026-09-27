namespace QueenZone.Web;

public sealed class MemberAccountDeletionHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<MemberAccountDeletionHostedService> logger)
    : PeriodicScopedHostedService(scopeFactory, timeProvider, logger, DefaultRunInterval)
{
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(6);

    private readonly TimeProvider _timeProvider = timeProvider;

    protected override string FailureMessage => "Member account deletion purge failed.";

    protected override Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        MaintenanceJobs.RunMemberAccountDeletionAsync(scopedServices, _timeProvider, Logger, cancellationToken);
}

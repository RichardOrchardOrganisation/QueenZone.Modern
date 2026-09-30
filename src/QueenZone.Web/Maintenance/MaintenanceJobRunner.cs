using System.Diagnostics;

namespace QueenZone.Web;

/// <summary>
/// Runs the selected <see cref="MaintenanceJobs"/> once, each in its own DI scope. A failing job
/// is logged and the remaining jobs still run; the exit code is 1 when any job failed so Task
/// Scheduler records the run as failed.
/// </summary>
public sealed class MaintenanceJobRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<MaintenanceJobRunner> logger)
{
    public async Task<int> RunAsync(
        IReadOnlyList<string> jobs,
        CancellationToken cancellationToken = default)
    {
        var failures = 0;
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            logger.LogInformation("Starting maintenance job {MaintenanceJob}.", job);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await MaintenanceJobs.RunAsync(job, scope.ServiceProvider, timeProvider, logger, cancellationToken);
                logger.LogInformation(
                    "Maintenance job {MaintenanceJob} completed in {ElapsedMilliseconds} ms.",
                    job,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures++;
                logger.LogError(ex, "Maintenance job {MaintenanceJob} failed.", job);
            }
        }

        return failures == 0 ? 0 : 1;
    }
}

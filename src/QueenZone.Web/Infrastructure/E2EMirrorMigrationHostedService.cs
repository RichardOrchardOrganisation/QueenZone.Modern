using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Applies pending EF migrations to the disposable SQL Express mirror when the
/// <see cref="QueenZoneEnvironments.E2E"/> host starts. Sync/skip_sync copies production
/// Azure SQL, which can lag modern tables such as <c>QuizSprintRuns</c>. The connection
/// string has already passed <see cref="E2EConnectionGuard"/>, so this never targets
/// Azure SQL or any other remote server.
/// </summary>
public sealed class E2EMirrorMigrationHostedService(
    IDbContextFactory<QueenZoneDbContext> dbContextFactory,
    ILogger<E2EMirrorMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Applying EF migrations to the E2E SQL Express mirror.");
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("E2E SQL Express mirror migrations applied.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class QueenZoneDbTransactionsTests
{
    [Fact]
    public async Task ExecuteAsync_ClearsTrackedEntitiesBeforeRetrying()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var failOnce = new FailOnceSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IExecutionStrategyFactory, ForcedRetryExecutionStrategyFactory>()
            .AddInterceptors(failOnce)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        failOnce.Armed = false;
        await dbContext.Database.EnsureCreatedAsync();

        var memberId = Guid.NewGuid();
        failOnce.Armed = true;
        var attempts = await QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            async ct =>
            {
                dbContext.MemberAccounts.Add(new MemberAccount
                {
                    Id = memberId,
                    Email = "transaction-retry@example.com",
                    NormalizedEmail = "TRANSACTION-RETRY@EXAMPLE.COM",
                    DisplayName = "Transaction Retry",
                    CreatedAt = DateTime.UtcNow,
                });
                await dbContext.SaveChangesAsync(ct);
                return failOnce.Attempts;
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(1, await dbContext.MemberAccounts.CountAsync(
            member => member.Id == memberId,
            CancellationToken.None));
    }

    private sealed class ForcedRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ForcedRetryExecutionStrategy(dependencies);
    }

    private sealed class ForcedRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is ForcedTransientException;
    }

    private sealed class ForcedTransientException() : Exception("Forced transient failure for retry coverage.");

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (!Armed)
            {
                return ValueTask.FromResult(result);
            }

            Armed = false;
            throw new ForcedTransientException();
        }
    }
}

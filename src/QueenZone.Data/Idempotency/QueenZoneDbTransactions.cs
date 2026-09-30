using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Data;

/// <summary>
/// Starts an execution-strategy transaction, or joins one already opened on
/// the same <see cref="QueenZoneDbContext"/>. Write repositories join an
/// idempotency store's outer transaction so the receipt and resource commit
/// together.
/// </summary>
public static class QueenZoneDbTransactions
{
    /// <summary>
    /// Runs the operation without a transaction when no EF context is registered,
    /// as in the in-memory Testing host; otherwise uses the retry-safe transaction.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        IServiceProvider? serviceProvider,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (serviceProvider?.GetService<QueenZoneDbContext>() is not { } dbContext)
        {
            return await operation(cancellationToken);
        }

        return await ExecuteAsync(dbContext, operation, cancellationToken);
    }

    public static Task<T> ExecuteAsync<T>(
        QueenZoneDbContext dbContext,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(dbContext, IsolationLevel.Unspecified, operation, cancellationToken);

    public static async Task<T> ExecuteAsync<T>(
        QueenZoneDbContext dbContext,
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = isolationLevel == IsolationLevel.Unspecified
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // Disposal rolls back when commit never happened.
                }

                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }
}

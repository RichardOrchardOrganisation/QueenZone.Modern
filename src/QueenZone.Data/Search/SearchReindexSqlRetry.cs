using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace QueenZone.Data;

/// <summary>
/// Bounded retry for idempotent search-reindex work after SQL deadlock (1205)
/// or client command timeout (-2). EF <c>EnableRetryOnFailure</c> already retries
/// 1205 per command (see <see cref="QueenZoneSqlServerOptions"/>) but does not
/// retry -2, and a contended full rebuild can exhaust that policy. Callers must
/// recreate any EF scope/DbContext between attempts so a failed context is not reused.
/// </summary>
public static class SearchReindexSqlRetry
{
    /// <summary>SQL Server deadlock-victim error number.</summary>
    public const int DeadlockNumber = 1205;

    /// <summary>Initial attempt plus this many retries after transient faults.</summary>
    public const int MaxAttempts = 6;

    /// <summary>Cap between retry attempts (seconds).</summary>
    public const int MaxDelaySeconds = 20;

    public static bool IsTransient(Exception? exception)
    {
        if (SiteSearchSqlTimeout.IsCommandTimeout(exception))
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockNumber })
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="failedAttempt">1-based attempt that just failed (1 = first try).</param>
    public static TimeSpan DelayBeforeAttempt(int failedAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedAttempt);
        var exponent = Math.Min(failedAttempt - 1, 5);
        var seconds = Math.Min(1 << exponent, MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    public static string FormatFailureMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sql = FindSqlException(exception);
        var raw = sql is not null
            ? $"Reindex failed: SqlException ({sql.Number}): {sql.Message}"
            : $"Reindex failed: {exception.GetType().Name}: {exception.Message}";

        const int maxLength = 240;
        var collapsed = raw.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength];
    }

    public static Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken,
        Action<int, int, Exception>? onRetry = null) =>
        ExecuteAsync(action, timeProvider, logger, cancellationToken, MaxAttempts, DelayBeforeAttempt, onRetry);

    internal static async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken,
        int maxAttempts,
        Func<int, TimeSpan> delayBeforeRetry,
        Action<int, int, Exception>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(delayBeforeRetry);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action(cancellationToken);
                return;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && attempt < maxAttempts
                && !cancellationToken.IsCancellationRequested
                && IsTransient(ex))
            {
                var delay = delayBeforeRetry(attempt);
                logger.LogWarning(
                    ex,
                    "Search reindex hit a transient SQL fault (attempt {Attempt}/{MaxAttempts}); retrying in {DelaySeconds}s.",
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);
                onRetry?.Invoke(attempt, maxAttempts, ex);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, cancellationToken);
                }
            }
        }
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }
}

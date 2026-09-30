using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class SearchReindexSqlRetryTests
{
    [Fact]
    public void IsTransient_detects_deadlock_and_timeout_including_wrappers()
    {
        var deadlock = SqlExceptionFactory.Create(
            SearchReindexSqlRetry.DeadlockNumber,
            "Transaction (Process ID 83) was deadlocked on lock resources with another process and has been chosen as the deadlock victim.");
        var timeout = SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");

        Assert.True(SearchReindexSqlRetry.IsTransient(deadlock));
        Assert.True(SearchReindexSqlRetry.IsTransient(new InvalidOperationException("wrapped", deadlock)));
        Assert.True(SearchReindexSqlRetry.IsTransient(new DbUpdateException("save", deadlock)));
        Assert.True(SearchReindexSqlRetry.IsTransient(timeout));
        Assert.True(SearchReindexSqlRetry.IsTransient(new InvalidOperationException("wrapped", timeout)));
        Assert.True(SearchReindexSqlRetry.IsTransient(SqlExceptionFactory.Create(0, "Execution Timeout Expired")));
    }

    [Fact]
    public void IsTransient_ignores_non_retryable_failures()
    {
        Assert.False(SearchReindexSqlRetry.IsTransient(null));
        Assert.False(SearchReindexSqlRetry.IsTransient(new InvalidOperationException("nope")));
        Assert.False(SearchReindexSqlRetry.IsTransient(SqlExceptionFactory.Create(208, "Invalid object name.")));
        Assert.False(SearchReindexSqlRetry.IsTransient(
            new DbUpdateException("save", SqlExceptionFactory.Create(208, "Invalid object name."))));
    }

    [Fact]
    public void DelayBeforeAttempt_is_bounded_exponential()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), SearchReindexSqlRetry.DelayBeforeAttempt(1));
        Assert.Equal(TimeSpan.FromSeconds(2), SearchReindexSqlRetry.DelayBeforeAttempt(2));
        Assert.Equal(TimeSpan.FromSeconds(4), SearchReindexSqlRetry.DelayBeforeAttempt(3));
        Assert.Equal(TimeSpan.FromSeconds(8), SearchReindexSqlRetry.DelayBeforeAttempt(4));
        Assert.Equal(TimeSpan.FromSeconds(16), SearchReindexSqlRetry.DelayBeforeAttempt(5));
        Assert.Equal(TimeSpan.FromSeconds(SearchReindexSqlRetry.MaxDelaySeconds), SearchReindexSqlRetry.DelayBeforeAttempt(6));
    }

    [Fact]
    public void FormatFailureMessage_includes_sql_number_and_collapses_newlines()
    {
        var sql = SqlExceptionFactory.Create(
            SearchReindexSqlRetry.DeadlockNumber,
            "Transaction was deadlocked.\r\nRerun the transaction.");

        var message = SearchReindexSqlRetry.FormatFailureMessage(new InvalidOperationException("outer", sql));

        Assert.Contains("SqlException (1205)", message, StringComparison.Ordinal);
        Assert.Contains("deadlocked", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\r', message);
        Assert.True(message.Length <= 240);
    }

    [Fact]
    public void FormatFailureMessage_uses_exception_type_when_not_sql()
    {
        var message = SearchReindexSqlRetry.FormatFailureMessage(new InvalidOperationException("boom"));

        Assert.Equal("Reindex failed: InvalidOperationException: boom", message);
    }

    [Fact]
    public async Task ExecuteAsync_retries_transient_faults_then_succeeds()
    {
        var logger = new CollectingLogger<SearchReindexSqlRetryTests>();
        var remaining = 2;
        var calls = 0;
        var retries = 0;

        await SearchReindexSqlRetry.ExecuteAsync(
            _ =>
            {
                calls++;
                if (remaining-- > 0)
                {
                    throw SqlExceptionFactory.Create(SearchReindexSqlRetry.DeadlockNumber, "deadlock");
                }

                return Task.CompletedTask;
            },
            TimeProvider.System,
            logger,
            CancellationToken.None,
            maxAttempts: 4,
            delayBeforeRetry: _ => TimeSpan.Zero,
            onRetry: (_, _, _) => retries++);

        Assert.Equal(3, calls);
        Assert.Equal(2, retries);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Fact]
    public async Task ExecuteAsync_does_not_retry_non_transient_failures()
    {
        var logger = new CollectingLogger<SearchReindexSqlRetryTests>();
        var sql = SqlExceptionFactory.Create(208, "Invalid object name.");
        var calls = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() =>
            SearchReindexSqlRetry.ExecuteAsync(
                _ =>
                {
                    calls++;
                    throw sql;
                },
                TimeProvider.System,
                logger,
                CancellationToken.None,
                maxAttempts: 4,
                delayBeforeRetry: _ => TimeSpan.Zero));

        Assert.Same(sql, thrown);
        Assert.Equal(1, calls);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_rethrows_after_attempts_exhausted()
    {
        var logger = new CollectingLogger<SearchReindexSqlRetryTests>();
        var timeout = SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired");
        var calls = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() =>
            SearchReindexSqlRetry.ExecuteAsync(
                _ =>
                {
                    calls++;
                    throw timeout;
                },
                TimeProvider.System,
                logger,
                CancellationToken.None,
                maxAttempts: 3,
                delayBeforeRetry: _ => TimeSpan.Zero));

        Assert.Same(timeout, thrown);
        Assert.Equal(3, calls);
        Assert.Equal(2, logger.Entries.Count);
    }
}

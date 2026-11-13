using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class SiteSearchSqlTimeoutTests
{
    [Fact]
    public void IsCommandTimeout_detects_number_minus_two()
    {
        var timeout = SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");

        Assert.True(SiteSearchSqlTimeout.IsCommandTimeout(timeout));
        Assert.True(SiteSearchSqlTimeout.IsCommandTimeout(new InvalidOperationException("wrapped", timeout)));
    }

    [Fact]
    public void IsCommandTimeout_detects_execution_timeout_message()
    {
        var timeout = SqlExceptionFactory.Create(0, "Execution Timeout Expired");

        Assert.True(SiteSearchSqlTimeout.IsCommandTimeout(timeout));
    }

    [Fact]
    public void IsCommandTimeout_ignores_other_sql_errors()
    {
        var missingObject = SqlExceptionFactory.Create(208, "Invalid object name.");

        Assert.False(SiteSearchSqlTimeout.IsCommandTimeout(missingObject));
        Assert.False(SiteSearchSqlTimeout.IsCommandTimeout(new InvalidOperationException("nope")));
        Assert.False(SiteSearchSqlTimeout.IsCommandTimeout(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_logs_abort_metadata_without_query_text(bool cancelled)
    {
        const string query = "bohemian zanzibar 1985";
        var logger = new AbortLogger();
        using var cts = new CancellationTokenSource();
        Exception failure = cancelled
            ? new OperationCanceledException(cts.Token)
            : SqlExceptionFactory.Create(SiteSearchSqlTimeout.SqlErrorNumber, "Execution Timeout Expired");
        if (cancelled)
        {
            cts.Cancel();
        }

        var thrown = await Record.ExceptionAsync(() =>
            SiteSearchSqlTimeout.ExecuteAsync<SiteSearchPage>(
                _ => throw failure, logger, query, "news", 3, cts.Token));

        if (cancelled)
        {
            Assert.Same(failure, thrown);
        }
        else
        {
            var timeout = Assert.IsType<SiteSearchTimeoutException>(thrown);
            Assert.Equal(query.Length, timeout.QueryLength);
            Assert.Equal(3, timeout.TermCount);
            Assert.True(timeout.Duration >= TimeSpan.Zero);
            Assert.Same(failure, timeout.InnerException);
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(1443, "SiteSearchAborted"), entry.EventId);
        Assert.Equal(cancelled ? "Cancelled" : "Timeout", entry.State["Outcome"]);
        Assert.Equal("news", entry.State["ContentType"]);
        Assert.Equal(3, entry.State["Page"]);
        Assert.Equal(query.Length, entry.State["QueryLength"]);
        Assert.Equal(3, entry.State["TermCount"]);
        Assert.True(Assert.IsType<int>(entry.State["ElapsedMs"]) >= 0);
        Assert.DoesNotContain(query, entry.Message, StringComparison.Ordinal);
        Assert.All(entry.State.Values, value =>
            Assert.DoesNotContain(query, value?.ToString() ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_maps_sql_error_after_caller_cancellation_to_cancelled()
    {
        var logger = new AbortLogger();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sql = SqlExceptionFactory.Create(0, "Operation cancelled by user.");

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SiteSearchSqlTimeout.ExecuteAsync<SiteSearchPage>(
                _ => throw sql, logger, "queen", null, 1, cts.Token));

        Assert.Same(sql, thrown.InnerException);
        Assert.Equal(cts.Token, thrown.CancellationToken);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Cancelled", entry.State["Outcome"]);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_log_unrelated_cancellation()
    {
        var logger = new AbortLogger();
        var failure = new OperationCanceledException();
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SiteSearchSqlTimeout.ExecuteAsync<SiteSearchPage>(
                _ => throw failure, logger, "queen", null, 1));
        Assert.Same(failure, thrown);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_returns_result_when_search_succeeds()
    {
        var logger = new CollectingLogger<EfSiteSearchService>();
        var page = new SiteSearchPage([], 0, 1, 20);

        var result = await SiteSearchSqlTimeout.ExecuteAsync(
            _ => Task.FromResult(page),
            logger,
            "queen",
            null,
            1,
            CancellationToken.None);

        Assert.Same(page, result);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_swallow_non_timeout_failures()
    {
        var logger = new CollectingLogger<EfSiteSearchService>();
        var sql = SqlExceptionFactory.Create(208, "Invalid object name.");

        var thrown = await Assert.ThrowsAsync<SqlException>(() =>
            SiteSearchSqlTimeout.ExecuteAsync<SiteSearchPage>(
                _ => throw sql,
                logger,
                "queen",
                null,
                1,
                CancellationToken.None));

        Assert.Same(sql, thrown);
        Assert.Empty(logger.Entries);
    }

    private sealed class AbortLogger : ILogger
    {
        public List<(LogLevel Level, EventId EventId, string Message, Dictionary<string, object?> State)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
    }
}

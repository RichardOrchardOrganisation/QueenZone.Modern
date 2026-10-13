using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class SlowQueryLoggingInterceptorTests
{
    [Theory]
    [InlineData(25, true)]
    [InlineData(0, false)]
    [InlineData(600_000, false)]
    public async Task LogsWarningOnlyWhenCommandMeetsThreshold(int thresholdMs, bool expectWarning)
    {
        var logger = new CapturingLogger();
        var interceptor = new SlowQueryLoggingInterceptor(
            Options.Create(new SlowQueryLoggingOptions { SlowQueryThresholdMilliseconds = thresholdMs }),
            logger);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("slow_one", () =>
        {
            Thread.Sleep(60);
            return 1;
        });
        var options = new DbContextOptionsBuilder<DbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new DbContext(options);

        await db.Database.ExecuteSqlRawAsync("SELECT slow_one()");
        await db.Database.ExecuteSqlRawAsync("SELECT 1");

        if (expectWarning)
        {
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("Slow SQL command", warning);
            Assert.Contains("slow_one", warning);
        }
        else
        {
            Assert.Empty(logger.Warnings);
        }
    }

    private sealed class CapturingLogger : ILogger<SlowQueryLoggingInterceptor>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

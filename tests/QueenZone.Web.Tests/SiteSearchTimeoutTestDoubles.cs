using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal sealed class CollectingLoggerFactory(ILogger logger) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => logger;

    public void Dispose()
    {
    }
}

internal sealed class TimeoutSiteSearchService : ISiteSearchService
{
    public TimeoutSiteSearchService(Exception? exception = null)
    {
        Exception = exception ?? new SiteSearchTimeoutException(
            16, 2,
            TimeSpan.FromSeconds(30),
            new InvalidOperationException("Execution Timeout Expired"));
    }

    public Exception Exception { get; }

    public Task<SiteSearchPage> SearchAsync(
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        throw Exception;
}

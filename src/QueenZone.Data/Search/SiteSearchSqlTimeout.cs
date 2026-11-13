using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace QueenZone.Data;

/// <summary>
/// Detects <c>SqlException</c> command timeouts from <c>dbo.SearchDocument_Search</c>
/// and logs timeout/cancellation metadata without query text at Warning.
/// </summary>
public static class SiteSearchSqlTimeout
{
    /// <summary>Client command-timeout number for <c>Execution Timeout Expired</c>.</summary>
    public const int SqlErrorNumber = -2;

    public static bool IsCommandTimeout(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql
                && (sql.Number == SqlErrorNumber || IsTimeoutMessage(sql.Message)))
            {
                return true;
            }
        }

        return false;
    }

    public static SiteSearchTimeoutException CreateAndLog(
        ILogger logger,
        string query,
        string? contentType,
        int page,
        TimeSpan duration,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        LogAborted(logger, "Timeout", query, contentType, page, duration);
        return new SiteSearchTimeoutException(query.Length, CountTerms(query), duration, exception);
    }

    private static int CountTerms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static void LogAborted(
        ILogger logger, string outcome, string query, string? contentType, int page, TimeSpan duration) =>
        logger.LogWarning(
            new EventId(1443, "SiteSearchAborted"),
            "Site search {Outcome} after {ElapsedMs}ms (ContentType {ContentType}, Page {Page}, QueryLength {QueryLength}, TermCount {TermCount})",
            outcome, (int)Math.Round(duration.TotalMilliseconds), contentType, page, query.Length, CountTerms(query));

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> execute,
        ILogger logger,
        string query,
        string? contentType,
        int page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(logger);

        var started = Stopwatch.StartNew();
        try
        {
            return await execute(cancellationToken);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && !IsCommandTimeout(ex))
        {
            // SqlClient can surface a cancelled command as SqlException ("Operation cancelled by user").
            LogAborted(logger, "Cancelled", query, contentType, page, started.Elapsed);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new OperationCanceledException("Site search was cancelled.", ex, cancellationToken);
        }
        catch (Exception ex) when (IsCommandTimeout(ex))
        {
            throw CreateAndLog(logger, query, contentType, page, started.Elapsed, ex);
        }
    }

    private static bool IsTimeoutMessage(string? message) =>
        !string.IsNullOrEmpty(message)
        && message.Contains("Execution Timeout Expired", StringComparison.OrdinalIgnoreCase);
}

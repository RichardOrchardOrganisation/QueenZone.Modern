using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// One retry for live-site read-only sweep HTTP and Playwright <c>goto</c> when the
/// runner drops a socket mid-request (issue #1543). Same spirit as the #1432 harness
/// retry: transport cancel only, never assertion or shape failures.
/// </summary>
/// <remarks>
/// Enabled only when <c>E2E_READONLY</c> is set (LiveSite mode). Nightly RealData
/// against the SQL Express mirror does not retry. Caller-supplied cancellation
/// (the 120s sitemap budget) is not retried.
/// </remarks>
internal static class LiveSiteTransportRetry
{
    public static bool ShouldRetry(
        Exception exception,
        CancellationToken cancellationToken = default,
        Func<string, string?>? getEnv = null)
    {
        if (!RealDataMarkers.IsReadOnlyMode(getEnv))
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return IsTransientCancel(exception);
    }

    public static bool IsTransientCancel(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TaskCanceledException or SocketException)
            {
                return true;
            }

            // Playwright goto reports "Timeout 30000ms exceeded" as PlaywrightException,
            // not TaskCanceledException. Live-site 404/nav flakes on the runner are the
            // same transport interrupt (issue #1543).
            if (IsPlaywrightNavigationTimeout(current))
            {
                return true;
            }

            if (current is HttpRequestException or IOException or PlaywrightException
                && LooksLikeSocketCancel(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<T> RunAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default,
        Func<string, string?>? getEnv = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldRetry(ex, cancellationToken, getEnv))
        {
            TestContext.Out.WriteLine(
                $"Live-site transport cancel; retrying once ({ex.GetType().Name}: {ex.Message})");
            return await action().ConfigureAwait(false);
        }
    }

    private static bool IsPlaywrightNavigationTimeout(Exception exception) =>
        exception is PlaywrightException
        && exception.Message.Contains("Timeout", StringComparison.Ordinal)
        && exception.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase);

    internal static bool LooksLikeSocketCancel(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            || message.Contains("aborted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
            || message.Contains("transport connection", StringComparison.OrdinalIgnoreCase)
            || message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase)
            || message.Contains("net::ERR_CONNECTION", StringComparison.OrdinalIgnoreCase)
            || message.Contains("net::ERR_ABORTED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("net::ERR_SOCKET", StringComparison.OrdinalIgnoreCase)
            || message.Contains("net::ERR_EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase)
            || message.Contains("socket", StringComparison.OrdinalIgnoreCase);
    }
}

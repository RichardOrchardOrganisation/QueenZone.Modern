using System.Net.Http;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// One LiveSite-only recheck of a main-document navigation after a 5xx response
/// or Playwright navigation timeout (issue #2195). Waits for <c>/warmup</c> 200
/// (bounded ~3 minutes, at least ~90s since the failure) then navigates again
/// with the same caller asserts. Nightly RealData and DevJourney do not recheck.
/// </summary>
internal static class LiveSiteNavigationRecheck
{
    internal static readonly TimeSpan DefaultWarmupBound = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan DefaultMinimumWait = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan DefaultWarmupPollInterval = TimeSpan.FromSeconds(5);

    public static bool ShouldRecheck(
        int? status,
        Exception? exception,
        Func<string, string?>? getEnv = null)
    {
        if (!RealDataMarkers.IsReadOnlyMode(getEnv))
        {
            return false;
        }

        if (exception is not null)
        {
            return LiveSiteTransportRetry.IsPlaywrightNavigationTimeout(exception);
        }

        return IsServerError(status);
    }

    public static bool IsServerError(int? status) =>
        status is >= 500 and <= 599;

    public static async Task<IResponse?> GotoAsync(
        IPage page,
        string url,
        PageGotoOptions? options = null,
        LiveSiteRecheckOptions? optionsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        return await RunAsync(
            url,
            () => page.GotoAsync(url, options),
            response => response?.Status,
            optionsOverride).ConfigureAwait(false);
    }

    public static async Task<T> RunAsync<T>(
        string path,
        Func<Task<T>> navigate,
        Func<T, int?> getStatus,
        LiveSiteRecheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        ArgumentNullException.ThrowIfNull(getStatus);
        options ??= new LiveSiteRecheckOptions();

        T first;
        try
        {
            first = await navigate().ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRecheck(status: null, exception, options.GetEnv))
        {
            return await RecheckAfterWarmupAsync(
                path,
                "timeout",
                navigate,
                getStatus,
                options).ConfigureAwait(false);
        }

        if (!ShouldRecheck(getStatus(first), exception: null, options.GetEnv))
        {
            return first;
        }

        return await RecheckAfterWarmupAsync(
            path,
            StatusLabel(getStatus(first)),
            navigate,
            getStatus,
            options).ConfigureAwait(false);
    }

    internal static async Task WaitForWarmupAsync(
        LiveSiteRecheckOptions options,
        DateTimeOffset failureAt,
        CancellationToken cancellationToken = default)
    {
        if (options.WaitForWarmupAsync is not null)
        {
            await options.WaitForWarmupAsync(cancellationToken).ConfigureAwait(false);
            await HonorMinimumWaitAsync(options, failureAt, cancellationToken).ConfigureAwait(false);
            return;
        }

        var baseUrl = options.WarmupBaseUrl
            ?? Environment.GetEnvironmentVariable("E2E_BASE_URL")
            ?? "https://www.queenzone.org";
        var warmupUri = new Uri(new Uri(EnsureTrailingSlash(baseUrl)), "warmup");
        var time = options.Time ?? TimeProvider.System;
        var delay = options.DelayAsync ?? DelayAsync;
        var bound = options.WarmupBound <= TimeSpan.Zero ? DefaultWarmupBound : options.WarmupBound;
        var poll = options.WarmupPollInterval <= TimeSpan.Zero ? DefaultWarmupPollInterval : options.WarmupPollInterval;
        var deadline = failureAt + bound;

        using var client = options.WarmupHandler is null
            ? new HttpClient()
            : new HttpClient(options.WarmupHandler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(15);

        while (time.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(warmupUri, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode == 200)
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Site is still recycling; keep polling until the bound.
            }

            var remaining = deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var sleep = remaining < poll ? remaining : poll;
            await delay(sleep, cancellationToken).ConfigureAwait(false);
        }

        await HonorMinimumWaitAsync(options, failureAt, cancellationToken).ConfigureAwait(false);
    }

    internal static string FormatLog(string path, string first, string second) =>
        $"LIVESITE RECHECK {path}: {first} -> {second}";

    private static async Task<T> RecheckAfterWarmupAsync<T>(
        string path,
        string firstLabel,
        Func<Task<T>> navigate,
        Func<T, int?> getStatus,
        LiveSiteRecheckOptions options)
    {
        var time = options.Time ?? TimeProvider.System;
        var failureAt = time.GetUtcNow();
        await WaitForWarmupAsync(options, failureAt).ConfigureAwait(false);

        try
        {
            var second = await navigate().ConfigureAwait(false);
            Log(options, path, firstLabel, StatusLabel(getStatus(second)));
            return second;
        }
        catch (Exception exception)
        {
            Log(options, path, firstLabel, DescribeException(exception));
            throw;
        }
    }

    private static async Task HonorMinimumWaitAsync(
        LiveSiteRecheckOptions options,
        DateTimeOffset failureAt,
        CancellationToken cancellationToken)
    {
        var time = options.Time ?? TimeProvider.System;
        var delay = options.DelayAsync ?? DelayAsync;
        var minimum = options.MinimumWaitSinceFailure < TimeSpan.Zero
            ? DefaultMinimumWait
            : options.MinimumWaitSinceFailure;
        var remaining = failureAt + minimum - time.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Log(LiveSiteRecheckOptions options, string path, string first, string second)
    {
        var line = FormatLog(path, first, second);
        (options.Log ?? DefaultLog)(line);
        var summary = options.StepSummaryPath
            ?? Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            File.AppendAllText(summary, line + Environment.NewLine);
        }
    }

    private static void DefaultLog(string line)
    {
        TestContext.Out.WriteLine(line);
    }

    private static string StatusLabel(int? status) =>
        status is null ? "no response" : status.Value.ToString();

    private static string DescribeException(Exception exception) =>
        LiveSiteTransportRetry.IsPlaywrightNavigationTimeout(exception) ? "timeout" : exception.GetType().Name;

    private static string EnsureTrailingSlash(string baseUrl) =>
        baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal sealed record LiveSiteRecheckOptions
{
    public Func<string, string?>? GetEnv { get; init; }

    public Func<CancellationToken, Task>? WaitForWarmupAsync { get; init; }

    public string? WarmupBaseUrl { get; init; }

    public HttpMessageHandler? WarmupHandler { get; init; }

    public TimeSpan WarmupBound { get; init; } = LiveSiteNavigationRecheck.DefaultWarmupBound;

    public TimeSpan MinimumWaitSinceFailure { get; init; } = LiveSiteNavigationRecheck.DefaultMinimumWait;

    public TimeSpan WarmupPollInterval { get; init; } = LiveSiteNavigationRecheck.DefaultWarmupPollInterval;

    public TimeProvider? Time { get; init; }

    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }

    public Action<string>? Log { get; init; }

    public string? StepSummaryPath { get; init; }
}

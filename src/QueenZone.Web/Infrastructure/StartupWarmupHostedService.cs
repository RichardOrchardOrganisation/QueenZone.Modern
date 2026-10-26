using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class StartupWarmupOptions
{
    public const string SectionName = "StartupWarmup";

    /// <summary>Master switch. The service also only runs in the Production environment.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Per-page budget for the loopback requests, in seconds.</summary>
    public int PageTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Anonymous public pages rendered once after startup so the first visitor does not pay for
    /// JIT, Razor view activation and first-use EF queries. Each must be a cheap, cacheable GET.
    /// </summary>
    public string[] Paths { get; init; } = ["/", "/forum", "/news"];
}

/// <summary>
/// After the server is listening, primes the process in the background: the same public query
/// caches as <c>/warmup</c>, then a few real page renders over loopback. A restart (deploy or
/// platform) otherwise leaves the first visitors waiting 5 to 60 seconds on a B1 instance (#2391).
/// Everything is best-effort; a failure is logged and never affects startup or readiness.
/// </summary>
/// <remarks>
/// Loopback is a trusted forwarding peer (<see cref="QueenZoneForwardedHeaders"/>), so
/// <c>X-Forwarded-Proto: https</c> avoids the HTTPS redirect and the canonical host from
/// <c>Site:PublicBaseUrl</c> passes host filtering and the canonical-host redirect.
/// </remarks>
public class StartupWarmupHostedService(
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    IServer server,
    IServiceScopeFactory scopeFactory,
    IOptions<StartupWarmupOptions> options,
    IOptions<SiteOptions> siteOptions,
    ILogger<StartupWarmupHostedService> logger,
    HttpMessageHandler? handler = null) : IHostedService
{
    private const string Loopback = "127.0.0.1";

    private Task? running;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsProduction() || !options.Value.Enabled)
        {
            return Task.CompletedTask;
        }

        // Host shutdown (ApplicationStopping) cancels the warm-up; no token source of our own to dispose.
        var stopping = lifetime.ApplicationStopping;
        lifetime.ApplicationStarted.Register(() => running = Task.Run(() => RunAsync(stopping), stopping));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (running is { } task)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        try
        {
            var cachesMs = await WarmCachesAsync(cancellationToken);
            var pages = await WarmPagesAsync(cancellationToken);
            logger.LogInformation(
                "Startup warmup finished in {TotalMs}ms (public caches {CachesMs}ms, {Pages} pages).",
                total.ElapsedMilliseconds,
                cachesMs,
                pages);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(exception, "Startup warmup stopped by shutdown after {TotalMs}ms.", total.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Startup warmup aborted after {TotalMs}ms.", total.ElapsedMilliseconds);
        }
    }

    /// <summary>Resolves <see cref="PublicWarmupService"/> in its own scope and primes the caches.</summary>
    protected virtual async Task PrimeCachesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PublicWarmupService>()
            .WarmPublicCachesAsync(cancellationToken);
    }

    private async Task<long> WarmCachesAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await PrimeCachesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // PublicWarmupService already logged which step failed; keep warming pages anyway.
            logger.LogWarning(exception, "Startup warmup could not prime every public cache.");
        }

        return timer.ElapsedMilliseconds;
    }

    private async Task<int> WarmPagesAsync(CancellationToken cancellationToken)
    {
        var baseAddress = ResolveLoopbackAddress(
            server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);
        if (baseAddress is null
            || !Uri.TryCreate(siteOptions.Value.PublicBaseUrl, UriKind.Absolute, out var publicBase))
        {
            logger.LogInformation("Startup warmup found no loopback address; skipping page renders.");
            return 0;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(options.Value.PageTimeoutSeconds, 1));
        using var client = handler is null
            ? new HttpClient { Timeout = timeout }
            : new HttpClient(handler, disposeHandler: false) { Timeout = timeout };

        var warmed = 0;
        foreach (var path in options.Value.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, path));
                request.Headers.Host = publicBase.Authority;
                request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
                request.Headers.UserAgent.ParseAdd("QueenZone-StartupWarmup");
                using var response = await client.SendAsync(request, cancellationToken);
                await response.Content.ReadAsByteArrayAsync(cancellationToken);
                warmed++;
                logger.LogInformation(
                    "Startup warmup rendered {Path} with {StatusCode} in {DurationMs}ms.",
                    path,
                    (int)response.StatusCode,
                    timer.ElapsedMilliseconds);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Startup warmup could not render {Path} after {DurationMs}ms.",
                    path,
                    timer.ElapsedMilliseconds);
            }
        }

        return warmed;
    }

    /// <summary>
    /// The first plain-HTTP listener with a real port, with wildcard hosts mapped to loopback.
    /// Null when the server reports none (for example an in-memory test server).
    /// </summary>
    internal static Uri? ResolveLoopbackAddress(IEnumerable<string> addresses)
    {
        foreach (var address in addresses)
        {
            var candidate = address
                .Replace("[::]", Loopback, StringComparison.Ordinal)
                .Replace("0.0.0.0", Loopback, StringComparison.Ordinal)
                .Replace("*", Loopback, StringComparison.Ordinal)
                .Replace("+", Loopback, StringComparison.Ordinal);
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp
                && uri.Port > 0)
            {
                return uri;
            }
        }

        return null;
    }
}

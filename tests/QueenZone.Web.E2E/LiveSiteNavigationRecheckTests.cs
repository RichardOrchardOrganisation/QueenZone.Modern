using System.Net;
using System.Net.Http;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for the LiveSite 5xx / navigation-timeout recheck (issue #2195).
/// No browser.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class LiveSiteNavigationRecheckTests
{
    private static readonly Func<string, string?> ReadOnlyEnv =
        name => name == "E2E_READONLY" ? "true" : null;

    private static readonly Func<string, string?> MirrorEnv = _ => null;

    [Test]
    public void ShouldRecheck_FalseWhenNotLiveSite()
    {
        Assert.That(
            LiveSiteNavigationRecheck.ShouldRecheck(502, exception: null, MirrorEnv),
            Is.False);
        Assert.That(
            LiveSiteNavigationRecheck.ShouldRecheck(
                status: null,
                new PlaywrightException("Timeout 30000ms exceeded."),
                MirrorEnv),
            Is.False);
    }

    [Test]
    public void ShouldRecheck_TrueFor5xxAndNavigationTimeoutOnLiveSite()
    {
        Assert.That(LiveSiteNavigationRecheck.ShouldRecheck(502, exception: null, ReadOnlyEnv), Is.True);
        Assert.That(LiveSiteNavigationRecheck.ShouldRecheck(500, exception: null, ReadOnlyEnv), Is.True);
        Assert.That(
            LiveSiteNavigationRecheck.ShouldRecheck(
                status: null,
                new PlaywrightException("Timeout 60000ms exceeded."),
                ReadOnlyEnv),
            Is.True);
    }

    [Test]
    public void ShouldRecheck_FalseFor4xxAndAssertionFailures()
    {
        Assert.That(LiveSiteNavigationRecheck.ShouldRecheck(404, exception: null, ReadOnlyEnv), Is.False);
        Assert.That(LiveSiteNavigationRecheck.ShouldRecheck(200, exception: null, ReadOnlyEnv), Is.False);
        Assert.That(
            LiveSiteNavigationRecheck.ShouldRecheck(
                status: null,
                new InvalidOperationException("expected HTTP 200"),
                ReadOnlyEnv),
            Is.False);
        Assert.That(
            LiveSiteNavigationRecheck.ShouldRecheck(
                status: null,
                new AssertionException("shape failed"),
                ReadOnlyEnv),
            Is.False);
    }

    [Test]
    public async Task RunAsync_5xxThen200_PassesAndLogs()
    {
        var calls = 0;
        var logs = new List<string>();
        var warmupCalls = 0;

        var result = await LiveSiteNavigationRecheck.RunAsync(
            "/about",
            () => Task.FromResult(calls++ == 0 ? 502 : 200),
            status => status,
            InstantOptions(logs, () => warmupCalls++));

        Assert.That(result, Is.EqualTo(200));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(warmupCalls, Is.EqualTo(1));
        Assert.That(logs, Has.One.EqualTo("LIVESITE RECHECK /about: 502 -> 200"));
    }

    [Test]
    public async Task RunAsync_5xxThen5xx_ReturnsSecondStatusAndLogs()
    {
        var calls = 0;
        var logs = new List<string>();

        var result = await LiveSiteNavigationRecheck.RunAsync(
            "/articles",
            () => Task.FromResult(502 + calls++),
            status => status,
            InstantOptions(logs));

        Assert.That(result, Is.EqualTo(503));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(logs, Has.One.EqualTo("LIVESITE RECHECK /articles: 502 -> 503"));
    }

    [Test]
    public void RunAsync_DoesNotRetryAssertionFailure()
    {
        var calls = 0;

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            LiveSiteNavigationRecheck.RunAsync<int>(
                "/news",
                () =>
                {
                    calls++;
                    throw new InvalidOperationException("expected HTTP 200");
                },
                _ => 200,
                InstantOptions()));

        Assert.That(ex, Is.Not.Null);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_DoesNotRetry4xx()
    {
        var calls = 0;
        var logs = new List<string>();

        var result = await LiveSiteNavigationRecheck.RunAsync(
            "/missing",
            () =>
            {
                calls++;
                return Task.FromResult(404);
            },
            status => status,
            InstantOptions(logs));

        Assert.That(result, Is.EqualTo(404));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(logs, Is.Empty);
    }

    [Test]
    public async Task RunAsync_TimeoutThen200_PassesAndLogs()
    {
        var calls = 0;
        var logs = new List<string>();

        var result = await LiveSiteNavigationRecheck.RunAsync(
            "/articles",
            () =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new PlaywrightException("Timeout 60000ms exceeded.");
                }

                return Task.FromResult(200);
            },
            status => status,
            InstantOptions(logs));

        Assert.That(result, Is.EqualTo(200));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(logs, Has.One.EqualTo("LIVESITE RECHECK /articles: timeout -> 200"));
    }

    [Test]
    public void RunAsync_TimeoutThenTimeout_FailsAndLogs()
    {
        var calls = 0;
        var logs = new List<string>();

        Assert.ThrowsAsync<PlaywrightException>(() =>
            LiveSiteNavigationRecheck.RunAsync<int>(
                "/articles",
                () =>
                {
                    calls++;
                    throw new PlaywrightException("Timeout 60000ms exceeded.");
                },
                _ => null,
                InstantOptions(logs)));

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(logs, Has.One.EqualTo("LIVESITE RECHECK /articles: timeout -> timeout"));
    }

    [Test]
    public void RunAsync_DoesNotRecheckWhenNotLiveSite()
    {
        var calls = 0;

        var result = LiveSiteNavigationRecheck.RunAsync(
            "/about",
            () =>
            {
                calls++;
                return Task.FromResult(502);
            },
            status => status,
            new LiveSiteRecheckOptions
            {
                GetEnv = MirrorEnv,
                MinimumWaitSinceFailure = TimeSpan.Zero,
                WaitForWarmupAsync = _ => Task.CompletedTask,
            }).GetAwaiter().GetResult();

        Assert.That(result, Is.EqualTo(502));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task WaitForWarmup_PollsUntil200ThenHonorsMinimumWait()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));
        var start = DateTimeOffset.Parse("2026-10-07T06:23:00Z");
        var time = new ManualTime(start);
        var delays = new List<TimeSpan>();

        await LiveSiteNavigationRecheck.WaitForWarmupAsync(
            new LiveSiteRecheckOptions
            {
                WarmupBaseUrl = "https://www.queenzone.org",
                WarmupHandler = handler,
                WarmupBound = TimeSpan.FromMinutes(3),
                WarmupPollInterval = TimeSpan.FromSeconds(5),
                MinimumWaitSinceFailure = TimeSpan.FromSeconds(90),
                Time = time,
                DelayAsync = (delay, _) =>
                {
                    delays.Add(delay);
                    time.Advance(delay);
                    return Task.CompletedTask;
                },
            },
            start);

        Assert.That(handler.Requests, Has.Count.EqualTo(2));
        Assert.That(handler.Requests[0].RequestUri?.AbsolutePath, Is.EqualTo("/warmup"));
        Assert.That(delays, Has.Some.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(delays.Sum(delay => delay.TotalSeconds), Is.GreaterThanOrEqualTo(90));
    }

    [Test]
    public async Task WaitForWarmup_StopsAtBoundWhenNever200()
    {
        var handler = new RepeatingHandler(HttpStatusCode.BadGateway);
        var start = DateTimeOffset.Parse("2026-10-07T06:23:00Z");
        var time = new ManualTime(start);

        await LiveSiteNavigationRecheck.WaitForWarmupAsync(
            new LiveSiteRecheckOptions
            {
                WarmupBaseUrl = "https://www.queenzone.org",
                WarmupHandler = handler,
                WarmupBound = TimeSpan.FromSeconds(10),
                WarmupPollInterval = TimeSpan.FromSeconds(5),
                MinimumWaitSinceFailure = TimeSpan.Zero,
                Time = time,
                DelayAsync = (delay, _) =>
                {
                    time.Advance(delay);
                    return Task.CompletedTask;
                },
            },
            start);

        Assert.That(handler.Requests, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(time.GetUtcNow(), Is.GreaterThanOrEqualTo(start.AddSeconds(10)));
    }

    [Test]
    public async Task RunAsync_WritesRecheckLineToStepSummary()
    {
        var summary = Path.Combine(Path.GetTempPath(), $"livesite-recheck-{Guid.NewGuid():N}.md");
        var calls = 0;
        var options = InstantOptions() with { StepSummaryPath = summary };
        try
        {
            var result = await LiveSiteNavigationRecheck.RunAsync(
                "/about",
                () => Task.FromResult(calls++ == 0 ? 502 : 200),
                status => status,
                options);

            Assert.That(result, Is.EqualTo(200));
            Assert.That(
                await File.ReadAllTextAsync(summary),
                Does.Contain("LIVESITE RECHECK /about: 502 -> 200"));
        }
        finally
        {
            if (File.Exists(summary))
            {
                File.Delete(summary);
            }
        }
    }

    private static LiveSiteRecheckOptions InstantOptions(
        List<string>? logs = null,
        Action? onWarmup = null) =>
        new()
        {
            GetEnv = ReadOnlyEnv,
            MinimumWaitSinceFailure = TimeSpan.Zero,
            WaitForWarmupAsync = _ =>
            {
                onWarmup?.Invoke();
                return Task.CompletedTask;
            },
            Log = line => logs?.Add(line),
        };

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _utc;

        public ManualTime(DateTimeOffset utc) => _utc = utc;

        public override DateTimeOffset GetUtcNow() => _utc;

        public void Advance(TimeSpan delta) => _utc += delta;
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class RepeatingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public RepeatingHandler(HttpStatusCode status) => _status = status;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}

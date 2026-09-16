using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for the live-site transport-cancel retry (issue #1543). No browser.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class LiveSiteTransportRetryTests
{
    private static readonly Func<string, string?> ReadOnlyEnv =
        name => name == "E2E_READONLY" ? "true" : null;

    private static readonly Func<string, string?> MirrorEnv = _ => null;

    [Test]
    public void ShouldRetry_FalseWhenNotLiveSiteReadOnly()
    {
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new TaskCanceledException("The operation was canceled."),
                getEnv: MirrorEnv),
            Is.False);
    }

    [Test]
    public void ShouldRetry_FalseWhenCallerBudgetCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new TaskCanceledException("The operation was canceled."),
                cts.Token,
                ReadOnlyEnv),
            Is.False);
    }

    [Test]
    public void ShouldRetry_TrueForTaskCanceledOnLiveSite()
    {
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new TaskCanceledException("The operation was canceled."),
                getEnv: ReadOnlyEnv),
            Is.True);
    }

    [Test]
    public void ShouldRetry_TrueForSocketExceptionOnLiveSite()
    {
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new SocketException((int)SocketError.OperationAborted),
                getEnv: ReadOnlyEnv),
            Is.True);
    }

    [Test]
    public void ShouldRetry_TrueForHttpRequestExceptionWrappingSocketCancel()
    {
        var inner = new IOException(
            "Unable to read data from the transport connection: The I/O operation has been aborted because of either a thread exit or an application request.",
            new SocketException((int)SocketError.OperationAborted));
        var exception = new HttpRequestException("An error occurred while sending the request.", inner);

        Assert.That(LiveSiteTransportRetry.IsTransientCancel(exception), Is.True);
        Assert.That(LiveSiteTransportRetry.ShouldRetry(exception, getEnv: ReadOnlyEnv), Is.True);
    }

    [Test]
    public void ShouldRetry_TrueForPlaywrightNavigationTimeout()
    {
        var exception = new PlaywrightException("Timeout 30000ms exceeded.");

        Assert.That(LiveSiteTransportRetry.IsTransientCancel(exception), Is.True);
        Assert.That(LiveSiteTransportRetry.ShouldRetry(exception, getEnv: ReadOnlyEnv), Is.True);
    }

    [Test]
    public void ShouldRetry_TrueForPlaywrightSocketReset()
    {
        var exception = new PlaywrightException("net::ERR_CONNECTION_RESET at https://www.queenzone.org/news");

        Assert.That(LiveSiteTransportRetry.IsTransientCancel(exception), Is.True);
        Assert.That(LiveSiteTransportRetry.ShouldRetry(exception, getEnv: ReadOnlyEnv), Is.True);
    }

    [Test]
    public void ShouldRetry_FalseForAssertionOrShapeFailures()
    {
        Assert.That(LiveSiteTransportRetry.IsTransientCancel(new InvalidOperationException("expected HTTP 200")), Is.False);
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new InvalidOperationException("expected HTTP 200"),
                getEnv: ReadOnlyEnv),
            Is.False);
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new FormatException("Expected sitemap root 'sitemapindex'"),
                getEnv: ReadOnlyEnv),
            Is.False);
    }

    [Test]
    public void ShouldRetry_FalseForGenericOperationCanceled()
    {
        Assert.That(
            LiveSiteTransportRetry.ShouldRetry(
                new OperationCanceledException("budget"),
                getEnv: ReadOnlyEnv),
            Is.False);
    }

    [Test]
    public async Task RunAsync_RetriesOnceThenSucceedsOnLiveSiteCancel()
    {
        var calls = 0;

        var result = await LiveSiteTransportRetry.RunAsync(
            () =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new TaskCanceledException("The operation was canceled.");
                }

                return Task.FromResult(42);
            },
            getEnv: ReadOnlyEnv);

        Assert.That(result, Is.EqualTo(42));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void RunAsync_DoesNotRetryWhenNotLiveSite()
    {
        var calls = 0;

        var ex = Assert.ThrowsAsync<TaskCanceledException>(() =>
            LiveSiteTransportRetry.RunAsync<int>(
                () =>
                {
                    calls++;
                    throw new TaskCanceledException("The operation was canceled.");
                },
                getEnv: MirrorEnv));

        Assert.That(ex, Is.Not.Null);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void RunAsync_DoesNotRetryCallerBudgetCancel()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;

        Assert.ThrowsAsync<TaskCanceledException>(() =>
            LiveSiteTransportRetry.RunAsync<int>(
                () =>
                {
                    calls++;
                    throw new TaskCanceledException("The operation was canceled.");
                },
                cts.Token,
                ReadOnlyEnv));

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void RunAsync_DoesNotRetryASecondTime()
    {
        var calls = 0;

        Assert.ThrowsAsync<TaskCanceledException>(() =>
            LiveSiteTransportRetry.RunAsync<int>(
                () =>
                {
                    calls++;
                    throw new TaskCanceledException("The operation was canceled.");
                },
                getEnv: ReadOnlyEnv));

        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase("The operation was canceled.")]
    [TestCase("The I/O operation has been aborted because of either a thread exit or an application request.")]
    [TestCase("Unable to read data from the transport connection")]
    [TestCase("Connection reset by peer")]
    [TestCase("net::ERR_CONNECTION_CLOSED")]
    [TestCase("net::ERR_ABORTED")]
    [TestCase("net::ERR_SOCKET_NOT_CONNECTED")]
    [TestCase("net::ERR_EMPTY_RESPONSE")]
    public void LooksLikeSocketCancel_MatchesTransportPhrases(string message)
    {
        Assert.That(LiveSiteTransportRetry.LooksLikeSocketCancel(message), Is.True);
    }

    [TestCase("expected HTTP 200, got 404")]
    [TestCase("Expected sitemap root 'sitemapindex'")]
    [TestCase(null)]
    [TestCase("")]
    public void LooksLikeSocketCancel_IgnoresShapeFailures(string? message)
    {
        Assert.That(LiveSiteTransportRetry.LooksLikeSocketCancel(message), Is.False);
    }
}

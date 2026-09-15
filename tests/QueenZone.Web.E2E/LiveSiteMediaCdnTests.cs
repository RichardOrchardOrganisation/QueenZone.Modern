using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

/// <summary>
/// Live-site / real-data media host checks (#551). Anonymous public pages only — no admin or
/// member routes. Fan-performance audio is member-gated and app-proxied (#177); anonymous
/// <c>cdn2</c> / raw blob <c>songfiles</c> URLs must fail. Photography images may resolve
/// through <c>cdn.queenzone.org</c> (or follow redirects).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
[Category(E2ECategories.ReadOnly)]
public class LiveSiteMediaCdnTests : RealDataPageTest
{
    private static readonly string[] AllowedImageHosts =
    [
        "cdn.queenzone.org",
        "cdn2.queenzone.org",
        "www.queenzone.org",
        "queenzone.org",
    ];

    /// <summary>
    /// Legacy sample songfile known to exist in the production songfiles container
    /// (same filename as <c>SampleFanPerformanceData</c> / historical Q_STAGE_T rows).
    /// Used only to prove anonymous CDN/blob URLs are denied after #177.
    /// </summary>
    private const string KnownAnonymousSongfileCdnUrl =
        "https://cdn2.queenzone.org/songfiles/2014417798057369.mp3";

    private const string KnownAnonymousSongfileBlobUrl =
        "https://queenzoneprod.blob.core.windows.net/songfiles/2014417798057369.mp3";

    protected override bool AllowsWrites => false;

    [Test]
    public void SongfilesContainerName_IsPrivateSongfiles() =>
        Assert.That(SongFileUrl.ContainerName, Is.EqualTo("songfiles"));

    [Test]
    public async Task AnonymousCdn2SongfileUrl_IsDeniedAsync() =>
        await AssertAnonymousSongfileDeniedAsync(KnownAnonymousSongfileCdnUrl);

    [Test]
    public async Task AnonymousRawBlobSongfileUrl_IsDeniedAsync() =>
        await AssertAnonymousSongfileDeniedAsync(KnownAnonymousSongfileBlobUrl);

    private async Task AssertAnonymousSongfileDeniedAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 1023);

        using var response = await client.SendAsync(request);

        Assert.That(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            Does.Contain(response.StatusCode),
            FailurePrefix() +
            $"anonymous GET {url} must fail after songfiles lockdown (#177); " +
            $"got {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Test]
    public async Task PhotographyImages_ResolveThroughAllowedCdnHostsAsync()
    {
        var response = await Page.GotoAsync(
            "/photography",
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });

        Assert.That(
            response?.Status,
            Is.EqualTo(200),
            FailurePrefix() + "/photography: expected HTTP 200 for media host sampling.");

        var imageSrcs = await Page.Locator("img[src]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('src')).filter(Boolean)");

        var absoluteMedia = imageSrcs
            .Where(src => !string.IsNullOrWhiteSpace(src))
            .Select(src =>
            {
                if (Uri.TryCreate(src, UriKind.Absolute, out var abs))
                {
                    return abs;
                }

                if (Uri.TryCreate(new Uri(BaseUrl.TrimEnd('/') + "/"), src, out var relative))
                {
                    return relative;
                }

                return null;
            })
            .Where(uri => uri is not null)
            .Cast<Uri>()
            .Where(uri => uri.Scheme is "http" or "https")
            // Only check off-site / CDN-shaped hosts; same-origin static assets are fine.
            .Where(uri => !IsSameSiteHost(uri.Host))
            .Take(10)
            .ToList();

        var expectedPhotoHost = Environment.GetEnvironmentVariable("E2E_EXPECTED_PHOTO_HOST");

        if (absoluteMedia.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(expectedPhotoHost))
            {
                Assert.Fail(
                    FailurePrefix() +
                    $"/photography did not render an off-site image from required host {expectedPhotoHost}.");
            }

            TestContext.Out.WriteLine(
                "No off-site photography image hosts found on /photography; skipping CDN host assertions.");
            return;
        }

        var failures = new List<string>();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        foreach (var mediaUri in absoluteMedia)
        {
            if (!string.IsNullOrWhiteSpace(expectedPhotoHost)
                && !string.Equals(mediaUri.Host, expectedPhotoHost, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{FailurePrefix()}image host '{mediaUri.Host}' is not the isolated dev host " +
                    $"'{expectedPhotoHost}' (url={mediaUri}).");
                continue;
            }

            if (!IsAllowedMediaHost(mediaUri.Host))
            {
                failures.Add(
                    $"{FailurePrefix()}image host '{mediaUri.Host}' is not an allowed CDN/public host " +
                    $"(url={mediaUri}). Allowed: {string.Join(", ", AllowedImageHosts)}.");
                continue;
            }

            try
            {
                using var head = new HttpRequestMessage(HttpMethod.Head, mediaUri);
                using var mediaResponse = await client.SendAsync(head);
                // Missing thumbs are soft on archives; only fail hard on unexpected host policy above.
                if ((int)mediaResponse.StatusCode >= 500)
                {
                    failures.Add(
                        $"{FailurePrefix()}{mediaUri}: server error {(int)mediaResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"SOFT: {mediaUri}: {ex.Message}");
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            "Media CDN host failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static bool IsAllowedMediaHost(string host) =>
        AllowedImageHosts.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase));

    private static bool IsSameSiteHost(string host)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        return string.Equals(host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string FailurePrefix() =>
        RealDataMarkers.IsReadOnlyMode() ? "PRODUCTION LIVE-SITE: " : string.Empty;
}

using System.Text.Json;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>Admin feature proofs run only against the local Testing host.</summary>
public abstract class AdminFeatureProofTest : E2EPageTest
{
    public const string FixturePrefix = "e2e-fixture-";
    private const string TestAuthHeaderName = "X-Test-User-Email";

    [SetUp]
    public void GuardAdminProof()
    {
        RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(allowsWrites: true, fixtureName: GetType().Name);
        if (new Uri(BaseUrl).Host != "127.0.0.1")
        {
            throw new InvalidOperationException("Admin proofs require the local Testing host at 127.0.0.1.");
        }
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        GuardAdminProof();
        return new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestAuthHeaderName] = "admin@test.local"
            }
        };
    }

    protected async Task AssertAdminFeatureAsync(string featureId, string? path = null)
    {
        using var map = LoadAdminMap();
        var entry = map.RootElement.GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == featureId);
        var target = new Uri(new Uri(BaseUrl), path ?? entry.GetProperty("url").GetString()!);
        Assert.That(target.Host, Is.EqualTo("127.0.0.1"));

        await using var anonymous = await Playwright.APIRequest.NewContextAsync();
        var challenge = await anonymous.GetAsync(target.ToString(), new() { MaxRedirects = 0 });
        if (challenge.Status != 401)
        {
            Assert.That(challenge.Status, Is.EqualTo(302), "Anonymous requests must be challenged.");
            Assert.That(challenge.Headers.TryGetValue("location", out var location), Is.True);
            var signInPath = new Uri(target, location!).AbsolutePath;
            Assert.That(signInPath, Does.Match("(?i)(signin|sign-in|login|log-in)"));
        }

        await using var member = await Playwright.APIRequest.NewContextAsync(new()
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHeaderName] = "member@test.local" }
        });
        var forbidden = await member.GetAsync(target.ToString(), new() { MaxRedirects = 0 });
        Assert.That(forbidden.Status, Is.EqualTo(403), "A member outside Admin:AllowedEmails must be forbidden.");

        var response = await Page.GotoAsync(target.ToString());
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(200));
        foreach (var selector in entry.GetProperty("selectors").EnumerateArray())
        {
            await Expect(Page.Locator(selector.GetString()!)).ToBeVisibleAsync();
        }

        var artifacts = Environment.GetEnvironmentVariable("E2E_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "screenshot.png"), FullPage = true });
        }
    }

    private static JsonDocument LoadAdminMap()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "feature-map", "web", "admin.json");
            if (File.Exists(path))
            {
                return JsonDocument.Parse(File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException("Could not locate docs/feature-map/web/admin.json from the E2E output directory.");
    }
}

using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;

namespace QueenZone.Web.E2E;

/// <summary>
/// Shared Playwright page fixture: base URL, tracing, and failure screenshots/traces.
/// Secondary browser contexts created via <see cref="CreateExtraContextAsync"/> are traced and
/// captured on failure (admin moderation journeys use a separate admin context).
/// </summary>
public abstract class E2EPageTest : PageTest
{
    private readonly List<IBrowserContext> _extraContexts = [];

    protected static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://127.0.0.1:5099";

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
        };

    /// <summary>
    /// Creates a browser context that participates in failure artifact capture. Prefer this over
    /// <see cref="IBrowser.NewContextAsync"/> so admin/member secondary contexts are traced.
    /// Do not dispose the returned context yourself — TearDown stops tracing and disposes it.
    /// </summary>
    protected async Task<IBrowserContext> CreateExtraContextAsync(BrowserNewContextOptions? options = null)
    {
        var context = await Browser.NewContextAsync(options ?? new BrowserNewContextOptions { BaseURL = BaseUrl });
        _extraContexts.Add(context);
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Title = $"{TestContext.CurrentContext.Test.FullName} (extra-{_extraContexts.Count})",
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        return context;
    }

    [SetUp]
    public async Task StartTracingAsync()
    {
        _extraContexts.Clear();
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Title = TestContext.CurrentContext.Test.FullName,
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
    }

    [TearDown]
    public async Task CaptureFailureArtifactsAsync()
    {
        var outcome = TestContext.CurrentContext.Result.Outcome.Status;
        var failed = outcome is TestStatus.Failed or TestStatus.Warning;

        if (outcome == TestStatus.Passed && Environment.GetEnvironmentVariable("E2E_CAPTURE_PROOF") == "true")
        {
            await CapturePassedProofAsync();
        }

        var dir = failed ? E2EArtifactPaths.EnsureDirectory() : null;
        var name = E2EArtifactPaths.SanitizeFileName(TestContext.CurrentContext.Test.Name);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        if (failed && dir is not null)
        {
            await CaptureContextArtifactsAsync(Context, Page, dir, name, stamp, label: null);
            var index = 0;
            foreach (var extra in _extraContexts)
            {
                index++;
                IPage? page = null;
                try
                {
                    page = extra.Pages.LastOrDefault();
                }
                catch (Exception ex)
                {
                    TestContext.Out.WriteLine($"Extra context {index} page lookup failed: {ex.Message}");
                }

                await CaptureContextArtifactsAsync(
                    extra,
                    page,
                    dir,
                    name,
                    stamp,
                    label: $"extra{index}");
            }
        }
        else
        {
            await StopTracingQuietlyAsync(Context);
            foreach (var extra in _extraContexts)
            {
                await StopTracingQuietlyAsync(extra);
            }
        }

        foreach (var extra in _extraContexts)
        {
            try
            {
                await extra.DisposeAsync();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Extra context dispose failed: {ex.Message}");
            }
        }

        _extraContexts.Clear();
    }

    private async Task CapturePassedProofAsync()
    {
        var pages = new[] { Page }.Concat(_extraContexts.SelectMany(context => context.Pages));
        foreach (var page in pages.Where(page => !page.IsClosed && page.Url.StartsWith(BaseUrl, StringComparison.Ordinal)))
        {
            var dir = Path.Combine(E2EArtifactPaths.EnsureDirectory(), "passed-proof");
            Directory.CreateDirectory(dir);
            var name = Guid.NewGuid().ToString("N");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, name + ".png"), FullPage = true });
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(new
            {
                fixture = GetType().Name,
                test = TestContext.CurrentContext.Test.FullName,
                url = page.Url,
                screenshot = name + ".png"
            }));
        }
    }

    private static async Task CaptureContextArtifactsAsync(
        IBrowserContext context,
        IPage? page,
        string dir,
        string name,
        string stamp,
        string? label)
    {
        var suffix = string.IsNullOrEmpty(label) ? string.Empty : $"-{label}";
        if (page is not null)
        {
            try
            {
                TestContext.Out.WriteLine(
                    $"Failure artifact context{suffix}: url={page.Url}");
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(dir, $"{name}{suffix}-{stamp}.png"),
                    FullPage = true
                });
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Screenshot capture failed{suffix}: {ex.Message}");
            }
        }
        else
        {
            TestContext.Out.WriteLine($"Screenshot skipped{suffix}: no open page on context.");
        }

        try
        {
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine(dir, $"{name}{suffix}-{stamp}.zip")
            });
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Trace capture failed{suffix}: {ex.Message}");
            await StopTracingQuietlyAsync(context);
        }
    }

    private static async Task StopTracingQuietlyAsync(IBrowserContext context)
    {
        try
        {
            await context.Tracing.StopAsync();
        }
        catch
        {
            // ignore
        }
    }
}

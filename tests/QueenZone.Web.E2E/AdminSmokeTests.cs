using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminSmokeTests : E2EPageTest
{
    private static string AdminEmail =>
        Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL") ?? "admin@test.local";

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestAuthHeaderName] = AdminEmail
            }
        };

    private const string TestAuthHeaderName = "X-Test-User-Email";

    [Test]
    public async Task AdminNews_ShowsEditorialList()
    {
        await GotoAdminAsync("/admin/news");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Admin news", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Create article" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminNews_CanCreateDraft()
    {
        var uniqueTitle = $"E2E admin draft {DateTime.UtcNow:yyyyMMddHHmmss}";

        await GotoAdminAsync("/admin/news/new");
        await Page.GetByLabel("Title").FillAsync(uniqueTitle);
        await Page.GetByLabel("Excerpt").FillAsync("Playwright admin smoke excerpt.");
        await Page.Locator("[data-testid='rich-text-editor']").FillAsync("Playwright admin smoke body.");
        await Page.GetByLabel("Publication date").FillAsync("2026-06-14");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/news/\\d+/edit"));
        await Expect(Page.GetByText(uniqueTitle)).ToBeVisibleAsync();
    }

    [Test]
    public async Task FanPerformanceReports_FilterSelectionMatchesTheRenderedList()
    {
        await GotoAdminAsync("/admin/fan-performance-reports");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Fan performance reports", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Status")).ToHaveValueAsync("Open");
        await Expect(Page.GetByText("No reports in this view.")).ToBeVisibleAsync();

        await Page.GetByLabel("Status").SelectOptionAsync("Resolved");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/fan-performance-reports\\?status=Resolved$"));
        await Expect(Page.GetByLabel("Status")).ToHaveValueAsync("Resolved");
        await Expect(Page.GetByText("No reports in this view.")).ToBeVisibleAsync();

        await GotoAdminAsync("/admin/fan-performance-reports?status=resolved");
        await Expect(Page.GetByLabel("Status")).ToHaveValueAsync("Resolved");
        await GotoAdminAsync("/admin/fan-performance-reports?status=unknown");
        await Expect(Page.GetByLabel("Status")).ToHaveValueAsync("Open");
    }

    private async Task GotoAdminAsync(string path)
    {
        var response = await Page.GotoAsync(path);
        Assert.That(response?.Status, Is.EqualTo(200), $"Expected {path} to load as admin user {AdminEmail}.");
    }
}

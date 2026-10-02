using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceReportListRoutesTests : IClassFixture<FanPerformanceReportListFactory>
{
    private readonly FanPerformanceReportListFactory factory;

    public FanPerformanceReportListRoutesTests(FanPerformanceReportListFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("", "Open", 50)]
    [InlineData("?status=all", "all", 50)]
    [InlineData("?status=ALL&pageNumber=2", "all", 3)]
    [InlineData("?status=Resolved", "Resolved", 1)]
    [InlineData("?status=resolved", "Resolved", 1)]
    [InlineData("?status=Dismissed", "Dismissed", 1)]
    [InlineData("?status=unknown", "Open", 50)]
    [InlineData("?status=", "Open", 50)]
    [InlineData("?pageNumber=0", "Open", 50)]
    public async Task List_FiltersAndSelectsTheCanonicalStatus(string query, string status, int count)
    {
        var response = await factory.CreateAdminClient().GetAsync("/admin/fan-performance-reports" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, Assert.Single(document.QuerySelectorAll("select[name=status] option[selected]")).GetAttribute("value"));
        var rows = document.QuerySelectorAll("tbody tr");
        Assert.Equal(count, rows.Length);
        if (status != "all")
        {
            Assert.All(rows, row => Assert.Equal(status, row.QuerySelectorAll("td")[4].TextContent.Trim()));
        }
        Assert.All(rows, row =>
        {
            Assert.Contains("List Reporter", row.TextContent);
            Assert.StartsWith("/admin/fan-performance-reports/", row.QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("Open", row.QuerySelector("a")!.TextContent);
        });
    }

    [Fact]
    public async Task List_ShowsOpenReportTitlePerformerReasonAndReporter()
    {
        var document = await LoadAsync("");
        var row = document.QuerySelector("tbody tr")!;
        Assert.Contains("Performance", row.TextContent);
        Assert.Contains("List performer", row.TextContent);
        Assert.Contains("List reason", row.TextContent);
        Assert.Contains("List Reporter", row.TextContent);
    }

    [Fact]
    public async Task List_ShowsFallbacksAndEncodesReportText()
    {
        var document = await LoadAsync("?status=Resolved");
        Assert.Contains("#9001", document.Body!.TextContent);
        Assert.Contains("—", document.Body.TextContent);
        var dismissed = await LoadAsync("?status=Dismissed");
        Assert.Contains("<script>alert(1)</script>", dismissed.Body!.TextContent);
        Assert.Null(dismissed.QuerySelector("tbody script"));
    }

    [Theory]
    [InlineData("?status=Resolved&pageNumber=2")]
    [InlineData("?pageNumber=3")]
    public async Task List_WhenNoRowsMatch_ShowsEmptyState(string query)
    {
        var document = await LoadAsync(query);
        Assert.Contains("No reports in this view.", document.Body!.TextContent);
        Assert.Empty(document.QuerySelectorAll("tbody tr"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("fan@test.local", HttpStatusCode.Forbidden)]
    public async Task List_RejectsVisitorsOutsideAdminAllowlist(string? email, HttpStatusCode expected)
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        if (email is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User-Email", email);
        }
        Assert.Equal(expected, (await client.GetAsync("/admin/fan-performance-reports")).StatusCode);
    }

    private async Task<IDocument> LoadAsync(string query)
    {
        var response = await factory.CreateAdminClient().GetAsync("/admin/fan-performance-reports" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
    }
}

public sealed class FanPerformanceReportListFactory : QueenZoneWebApplicationFactory
{
    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var reports = new InMemoryFanPerformanceReportRepository(_ => new MemberAccount { DisplayName = "List Reporter" });
            for (var stageId = 1; stageId <= 51; stageId++)
            {
                reports.CreateAsync(new NewFanPerformanceReport(stageId, Guid.NewGuid(), "List reason", $"Performance {stageId}", "List performer")).GetAwaiter().GetResult();
            }
            var resolved = reports.CreateAsync(new NewFanPerformanceReport(9001, Guid.NewGuid(), "", "", null)).GetAwaiter().GetResult();
            reports.UpdateStatusAsync(resolved.ReportId!.Value, FanPerformanceReportStatus.Resolved, "admin@test.local").GetAwaiter().GetResult();
            var dismissed = reports.CreateAsync(new NewFanPerformanceReport(9002, Guid.NewGuid(), "<script>alert(1)</script>", "Dismissed performance", "Dismissed performer")).GetAwaiter().GetResult();
            reports.UpdateStatusAsync(dismissed.ReportId!.Value, FanPerformanceReportStatus.Dismissed, "admin@test.local").GetAwaiter().GetResult();
            services.RemoveAll<IFanPerformanceReportRepository>();
            services.AddSingleton<IFanPerformanceReportRepository>(reports);
        });
    }
}

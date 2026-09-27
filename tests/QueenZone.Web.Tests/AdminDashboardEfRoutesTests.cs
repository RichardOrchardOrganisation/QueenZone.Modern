using System.Net;

namespace QueenZone.Web.Tests;

/// <summary>
/// EF/SQLite HTTP regression test for /admin. Guarantees the dashboard loads via a
/// parallel-safe pattern (independent scopes / contexts), not concurrent use of one
/// scoped DbContext (which 500s in production). See issues #322 and #335.
/// </summary>
public sealed class AdminDashboardEfRoutesTests : IClassFixture<AdminDashboardEfWebApplicationFactory>
{
    private readonly AdminDashboardEfWebApplicationFactory factory;

    public AdminDashboardEfRoutesTests(AdminDashboardEfWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AdminDashboard_EfBacked_ReturnsOk()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin sections", body);
        Assert.Contains("Submission queue", body);
        Assert.DoesNotContain("A second operation was started on this context", body);
    }
}

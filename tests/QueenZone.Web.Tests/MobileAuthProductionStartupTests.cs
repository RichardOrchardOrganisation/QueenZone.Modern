using System.Net;

namespace QueenZone.Web.Tests;

[Collection(ProductionHostCollection.Name)]
public sealed class MobileAuthProductionStartupTests
{
    private readonly ProductionHostFixture production;

    public MobileAuthProductionStartupTests(ProductionHostFixture production)
    {
        this.production = production;
    }

    [Fact]
    public async Task ProductionHost_ServesPublicPagesWithoutMobileAuthSigningKey()
    {
        var client = production.WithoutMobileAuthSigningKey.CreateClient();

        using var health = await client.GetAsync("/health");
        using var home = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }
}

using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HomeOnThisDayTests : IClassFixture<WebHostVariantCache>
{
    private readonly WebHostVariantCache variants;

    public HomeOnThisDayTests(WebHostVariantCache variants)
    {
        this.variants = variants;
    }

    [Fact]
    public async Task HomePageRendersOnThisDayMatchesForFixedDate()
    {
        var client = variants.Get(WebHostVariants.FixedUtc20260713).CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("This Day in Queen History", body);
        Assert.Contains("Queen&#x27;s Live Aid performance", body);
        Assert.Contains("Queen released", body);
        Assert.Contains("<time datetime=\"1985-07-13\">13 Jul 1985</time>", body);
        Assert.DoesNotContain("nearby dates", body);
    }

    [Fact]
    public async Task HomePageFallsBackToNearbyDatesWhenNoExactMatchExists()
    {
        var client = variants.Get(WebHostVariants.FixedUtc20260712).CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("nearby dates from the archive", body);
        Assert.Contains("Queen&#x27;s Live Aid performance", body);
    }
}

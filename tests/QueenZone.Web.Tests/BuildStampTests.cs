using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class BuildStampTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public BuildStampTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PublicPages_IncludeBuildStampWithMetadataAttributes()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum");

        Assert.Contains("qz-build-stamp", body);
        Assert.Contains("data-build-version=\"", body);
        Assert.Contains("data-build-utc=\"", body);
        Assert.Contains("toLocaleString", body);
    }

    [Fact]
    public void BuildMetadata_ExposesVersionAndUtcTimestamp()
    {
        Assert.True(BuildMetadata.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(BuildMetadata.Version));
        Assert.True(DateTimeOffset.TryParse(BuildMetadata.BuiltAtUtc, out _));
    }
}

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class StylesheetConventionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] AllowedMaxWidths = ["640", "768", "900"];
    private readonly WebApplicationFactory<Program> factory;

    public StylesheetConventionsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task SiteCssUsesDocumentedResponsiveBreakpoints()
    {
        var client = factory.CreateClient();
        var css = await client.GetStringAsync("/css/site.css");

        var maxWidths = Regex.Matches(css, @"@media[^{}]*\(max-width:\s*(\d+)px\)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(AllowedMaxWidths, maxWidths);
    }
}

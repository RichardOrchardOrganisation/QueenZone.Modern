using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class WellKnownStaticFileTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public WellKnownStaticFileTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MicrosoftIdentityAssociationFile_IsServedUnderWellKnown()
    {
        // PhysicalFileProvider excludes dot-prefixed paths by default, so this regression-tests
        // that /.well-known/* is explicitly opted back in rather than silently 404ing.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/microsoft-identity-association.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("associatedApplications", body);
    }
}

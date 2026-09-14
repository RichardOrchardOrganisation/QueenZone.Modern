using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class ArchiveAuthorPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ArchiveAuthorPageTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task ArchiveAuthorPage_RendersPostsForKnownLegacyAuthor()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/archive-authors/5001");

        Assert.Contains("brightonrock", body);
        Assert.Contains("Ranking every studio album", body);
        Assert.Contains("1 post", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveAuthorPage_PaginatesMultiplePosts()
    {
        var client = factory.CreateClient();

        var page1 = await client.GetStringAsync("/forum/archive-authors/5002");
        var page2 = await client.GetStringAsync("/forum/archive-authors/5002?pageNumber=2");

        Assert.Contains("Page 1 of 2", page1);
        Assert.Contains("Archive reply 1125", page1);
        Assert.DoesNotContain("Archive reply 1103", page1);
        Assert.Contains("Archive reply 1103", page2);
    }

    [Fact]
    public async Task ArchiveAuthorPage_UnknownLegacyId_ReturnsNotFound()
    {
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/forum/archive-authors/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveAuthorPage_PageNumberBelowOne_ReturnsNotFound()
    {
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/forum/archive-authors/5001?pageNumber=0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveAuthorPage_PageNumberBeyondLastPage_ReturnsNotFound()
    {
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/forum/archive-authors/5001?pageNumber=2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveAuthorPage_LinkedLegacyUser_RedirectsToMemberProfile()
    {
        using var linkedFactory = QueenZoneWebApplicationFactory.WithServices(_ => { });
        using var scope = linkedFactory.Services.CreateScope();
        var memberAccountRepository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var member = await memberAccountRepository.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "brightonrock-linked@example.com",
            DisplayName = "Brighton Rock",
            CreatedAt = DateTime.UtcNow,
        });
        await memberAccountRepository.LinkLegacyUserIdAsync(member.Id, 5001);

        using var client = linkedFactory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/forum/archive-authors/5001");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"/members/{member.Id}", response.Headers.Location?.OriginalString);
    }
}

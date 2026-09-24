using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class EditorialJsonLdTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public EditorialJsonLdTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task NewsDetailEmitsNewsArticleJsonLd()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.Contains("application/ld+json", body);
        Assert.Contains("\"@type\":\"NewsArticle\"", body);
        Assert.Contains("\"headline\":\"QueenZone modernisation begins\"", body);
        Assert.Contains("\"@context\":\"https://schema.org\"", body);
        Assert.Contains("\"datePublished\":", body);
        Assert.Contains("\"description\":", body);
        Assert.Contains("\"publisher\":", body);
        Assert.Contains("/news/1003/queenzone-modernisation-begins", body);
    }

    [Fact]
    public async Task ArticleDetailEmitsArticleJsonLd()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles/101/inside-the-making-of-bohemian-rhapsody");

        Assert.Contains("application/ld+json", body);
        Assert.Contains("\"@type\":\"Article\"", body);
        Assert.Contains("\"headline\":\"Inside the Making of Bohemian Rhapsody\"", body);
        Assert.Contains("\"@context\":\"https://schema.org\"", body);
        Assert.Contains("\"datePublished\":", body);
        Assert.Contains("\"description\":", body);
        Assert.Contains("\"publisher\":", body);
        Assert.Contains("/articles/101/inside-the-making-of-bohemian-rhapsody", body);
    }

    [Fact]
    public void BuildNewsArticle_ProducesValidJsonWithRequiredFields()
    {
        var json = JsonDocument.Parse(
            EditorialJsonLd.BuildNewsArticle(
                headline: "Test Headline",
                canonicalPath: "/news/1/test-headline",
                datePublished: new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                description: "Test excerpt.",
                publicBaseUrl: "https://www.queenzone.org"));

        var root = json.RootElement;
        Assert.Equal("https://schema.org", root.GetProperty("@context").GetString());
        Assert.Equal("NewsArticle", root.GetProperty("@type").GetString());
        Assert.Equal("Test Headline", root.GetProperty("headline").GetString());
        Assert.Equal("https://www.queenzone.org/news/1/test-headline", root.GetProperty("url").GetString());
        Assert.Equal("2026-01-15T09:00:00Z", root.GetProperty("datePublished").GetString());
        Assert.Equal("Test excerpt.", root.GetProperty("description").GetString());
        Assert.Equal("QueenZone", root.GetProperty("publisher").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("author", out _), "NewsArticle should not emit an author field");
    }

    [Fact]
    public void BuildArticle_WithAuthor_IncludesAuthorField()
    {
        var json = JsonDocument.Parse(
            EditorialJsonLd.BuildArticle(
                headline: "Community Article",
                canonicalPath: "/articles/community-slug",
                datePublished: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                description: "A community piece.",
                publicBaseUrl: "https://www.queenzone.org",
                authorName: "Jane Fan"));

        var root = json.RootElement;
        Assert.Equal("Article", root.GetProperty("@type").GetString());
        Assert.Equal("Person", root.GetProperty("author").GetProperty("@type").GetString());
        Assert.Equal("Jane Fan", root.GetProperty("author").GetProperty("name").GetString());
    }

    [Fact]
    public void BuildArticle_WithoutAuthor_OmitsAuthorField()
    {
        var json = JsonDocument.Parse(
            EditorialJsonLd.BuildArticle(
                headline: "Legacy Article",
                canonicalPath: "/articles/101/legacy",
                datePublished: new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                description: null,
                publicBaseUrl: "https://www.queenzone.org"));

        var root = json.RootElement;
        Assert.Equal("Article", root.GetProperty("@type").GetString());
        Assert.False(root.TryGetProperty("author", out _));
        Assert.False(root.TryGetProperty("description", out _), "null description should be omitted");
    }

}

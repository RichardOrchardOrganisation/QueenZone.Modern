using QueenZone.Data;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Tests;

public sealed class CoreSitemapBuilderTests
{
    [Theory]
    [InlineData(SitemapSections.News, "/news")]
    [InlineData(SitemapSections.Articles, "/articles")]
    [InlineData(SitemapSections.Biography, "/biography")]
    [InlineData(SitemapSections.ForumCategories, "/forum")]
    [InlineData(SitemapSections.Photography, "/photography")]
    [InlineData(SitemapSections.FanPerformances, "/fan-performances")]
    [InlineData(SitemapSections.Discography, "/discography")]
    public async Task Section_contains_its_canonical_landing_page(string section, string expectedPath)
    {
        var entries = await CreateBuilder().BuildSectionAsync(section);

        Assert.NotNull(entries);
        Assert.Contains(entries, entry => entry.Path == expectedPath);
    }

    [Fact]
    public async Task Unknown_section_returns_no_sitemap()
    {
        Assert.Null(await CreateBuilder().BuildSectionAsync("missing"));
    }

    private static CoreSitemapBuilder CreateBuilder() =>
        new(new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
            new ArticleSitemapEntriesBuilder(new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
                new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository())),
            new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
            new InMemoryForumRepository(SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats()),
            new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories())),
            new InMemoryFanPerformanceRepository([]),
            new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()));
}

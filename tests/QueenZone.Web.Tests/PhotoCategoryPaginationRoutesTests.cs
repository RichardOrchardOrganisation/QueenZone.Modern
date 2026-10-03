using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoCategoryPaginationRoutesTests : IClassFixture<PhotoCategoryPaginationFactory>
{
    private readonly HttpClient client;

    public PhotoCategoryPaginationRoutesTests(PhotoCategoryPaginationFactory factory)
    {
        client = factory.CreateAnonymousClient(allowAutoRedirect: false);
    }

    [Theory]
    [InlineData(1, 1, 24)]
    [InlineData(2, 25, 48)]
    [InlineData(3, 49, 49)]
    public async Task CategoryPage_ShowsOnlyItsPhotosAndKeepsPageOnFilters(int page, int first, int last)
    {
        var path = page == 1 ? "/photography/pagination" : $"/photography/pagination/page/{page}";
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        var photoLinks = document.QuerySelectorAll("a[href]")
            .Select(link => link.GetAttribute("href"))
            .Where(href => href is not null && href.StartsWith("/photography/pagination/", StringComparison.Ordinal)
                && !href.Contains("/page/", StringComparison.Ordinal))
            .Distinct().ToArray();
        Assert.Equal(Enumerable.Range(first, last - first + 1).Select(id => $"/photography/pagination/{id}"), photoLinks);
        var filterLinks = document.QuerySelectorAll("nav[aria-label='Filter photos by size'] a");
        Assert.Equal(PhotoListFilter.AllPresets.Count, filterLinks.Length);
        Assert.All(filterLinks, link => Assert.StartsWith(path, link.GetAttribute("href")));
        Assert.Contains($"Showing {first}–{last} of 49", document.Body!.TextContent);
    }

    [Theory]
    [InlineData("", "/photography/pagination")]
    [InlineData("?size=desktop", "/photography/pagination?size=desktop")]
    [InlineData("?size=phone", "/photography/pagination?size=phone")]
    public async Task PageOne_RedirectsPermanentlyAndKeepsSize(string query, string expected)
    {
        var response = await client.GetAsync("/photography/pagination/page/1" + query);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/photography/pagination/page/0")]
    [InlineData("/photography/pagination/page/-1")]
    [InlineData("/photography/pagination/page/4")]
    [InlineData("/photography/pagination/page/4?size=phone")]
    [InlineData("/photography/missing/page/2")]
    public async Task InvalidPage_ReturnsNotFound(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task PageTwo_WithoutSizeMatches_ShowsEmptyStateAndUnfilteredPageLink()
    {
        var response = await client.GetAsync("/photography/pagination/page/2?size=phone");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        Assert.Contains("No images match", document.Body!.TextContent);
        var showAll = Assert.Single(document.QuerySelectorAll("a"), link => link.TextContent == "Show all sizes");
        Assert.Equal("/photography/pagination/page/2", showAll.GetAttribute("href"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(showAll.GetAttribute("href"))).StatusCode);
    }

    [Fact]
    public async Task PageTwo_WithDesktopFilter_KeepsFilterOnPhotosAndPagination()
    {
        var response = await client.GetAsync("/photography/pagination/page/2?size=desktop");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        Assert.Contains("matching Desktop wallpaper", document.Body!.TextContent);
        Assert.Contains(document.QuerySelectorAll("a"), link => link.GetAttribute("href") == "/photography/pagination/25?size=desktop");
        Assert.Contains(document.QuerySelectorAll("a"), link => link.GetAttribute("href") == "/photography/pagination/page/3?size=desktop");
        Assert.Contains(document.QuerySelectorAll("a"), link => link.GetAttribute("href") == "/photography/pagination?size=desktop");
    }
}

public sealed class PhotoCategoryPaginationFactory : QueenZoneWebApplicationFactory
{
    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var photos = Enumerable.Range(1, 49).Select(id => new PhotoItemSeed(
                id, $"Pagination photo {id}", $"/pagination/{id}.jpg", $"/pagination/{id}-t.jpg",
                new DateTime(1986, 7, 12).AddDays(-id), 1920, 1080)).ToArray();
            services.RemoveAll<IPhotoRepository>();
            services.AddSingleton<IPhotoRepository>(new InMemoryPhotoRepository(new SharedPhotoStore(
                [new PhotoCategorySeed(99, "Pagination", photos)])));
        });
    }
}

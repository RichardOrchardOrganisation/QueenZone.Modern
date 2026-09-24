using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web.Pages;

namespace QueenZone.Web.Tests;

public sealed class SearchPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SearchPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SearchPageRendersWithoutQuery()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search");

        Assert.Contains("Search the Archive", body);
        Assert.Contains("Bohemian Rhapsody", body);
        Assert.Contains("Freddie Mercury", body);
        Assert.DoesNotContain("result", body);
    }

    [Fact]
    public async Task SearchPageRendersForumResultsForMatchingQuery()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=studio+album");

        Assert.Contains("studio album", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ranking every studio album", body);
        Assert.Contains("/forum/topic/1002/ranking-every-studio-album", body);
    }

    [Fact]
    public async Task SearchPageRendersNewsResultsForMatchingQuery()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=modernisation");

        Assert.Contains("QueenZone modernisation begins", body);
        Assert.Contains("/news/1003/", body);
    }

    [Fact]
    public async Task SearchPage_sql_timeout_renders_in_page_unavailable_not_not_found()
    {
        using var timeoutFactory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<ISiteSearchService>();
            services.AddSingleton<ISiteSearchService>(new TimeoutSiteSearchService());
        });
        using var client = timeoutFactory.CreateAnonymousClient(allowAutoRedirect: false);

        using var response = await client.GetAsync("/search?q=Bohemian+Rhapsody");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SearchModel.UnavailableMessage, body, StringComparison.Ordinal);
        Assert.Contains("Bohemian Rhapsody", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role=\"alert\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Page Not Found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("No results found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPage_sql_timeout_sets_unavailable_flag()
    {
        var model = new SearchModel(new TimeoutSiteSearchService())
        {
            Query = "Bohemian Rhapsody",
            PageContext = new PageContext
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.True(model.SearchUnavailable);
        Assert.Null(model.Results);
    }

    [Fact]
    public async Task SearchPageRendersNoResultsMessageForUnmatchedQuery()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=xyzzy_no_match_zzzqq");

        Assert.Contains("No results found", body);
    }

    [Fact]
    public async Task SearchPageWithQuery_keeps_form_and_filters_in_one_section()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=studio+album");
        var section = AssertSingleSearchSection(body);

        Assert.Contains("<form", section, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Filter search results by content type\"", section, StringComparison.Ordinal);
        Assert.Contains("class=\"qz-tag-row u-mt-4\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPageEmptyResults_renders_message_under_chips_with_u_mt_4()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=xyzzy_no_match_zzzqq");
        var section = AssertSingleSearchSection(body);

        Assert.Contains("<form", section, StringComparison.Ordinal);
        AssertFilterThenMessage(section, "No results found", "u-mt-4");
    }

    [Fact]
    public async Task SearchPage_sql_timeout_renders_unavailable_under_chips_with_u_mt_4()
    {
        using var timeoutFactory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<ISiteSearchService>();
            services.AddSingleton<ISiteSearchService>(new TimeoutSiteSearchService());
        });
        using var client = timeoutFactory.CreateAnonymousClient(allowAutoRedirect: false);

        var body = await client.GetStringAsync("/search?q=Bohemian+Rhapsody");
        var section = AssertSingleSearchSection(body);

        Assert.Contains("<form", section, StringComparison.Ordinal);
        AssertFilterThenMessage(section, SearchModel.UnavailableMessage, "u-mt-4");
    }

    [Fact]
    public async Task SearchPageWithoutQuery_keeps_example_tags_u_mt_5()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search");
        var section = AssertSingleSearchSection(body);

        Assert.Contains("class=\"qz-tag-row u-mt-5\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Filter search results by content type", section, StringComparison.Ordinal);
    }

    private static string AssertSingleSearchSection(string body)
    {
        const string open = """<section class="qz-section">""";
        var mainStart = body.IndexOf("<main", StringComparison.Ordinal);
        var mainEnd = body.IndexOf("</main>", StringComparison.Ordinal);
        Assert.True(mainStart >= 0 && mainEnd > mainStart, "expected a main landmark");
        var main = body[mainStart..mainEnd];

        var first = main.IndexOf(open, StringComparison.Ordinal);
        Assert.True(first >= 0, "expected a search qz-section in main");
        var second = main.IndexOf(open, first + open.Length, StringComparison.Ordinal);
        Assert.True(second < 0, "expected a single search qz-section, not consecutive sections");

        var close = main.IndexOf("</section>", first, StringComparison.Ordinal);
        Assert.True(close > first, "expected the search qz-section to close");
        return main[first..(close + "</section>".Length)];
    }

    private static void AssertFilterThenMessage(string section, string message, string spacingClass)
    {
        var filter = section.IndexOf("Filter search results by content type", StringComparison.Ordinal);
        var messageAt = section.IndexOf(message, StringComparison.Ordinal);
        Assert.True(filter >= 0, "expected filter chips");
        Assert.True(messageAt > filter, "empty/fail-soft message must render under the chips");

        var paragraph = section.LastIndexOf("<p", messageAt, StringComparison.Ordinal);
        Assert.True(paragraph >= 0 && paragraph < messageAt, "expected a paragraph wrapping the message");
        Assert.Contains(spacingClass, section[paragraph..messageAt], StringComparison.Ordinal);
        Assert.Contains("class=\"qz-tag-row u-mt-4\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPageTypeFilterNarrowsToOneContentType()
    {
        var client = factory.CreateClient();

        // "archive" matches both forum threads and news articles in seed data.
        var allBody = await client.GetStringAsync("/search?q=archive");
        var newsOnlyBody = await client.GetStringAsync("/search?q=archive&type=news");

        Assert.Contains("Forum", allBody);
        Assert.DoesNotContain("/forum/topic/", newsOnlyBody);
    }

    [Fact]
    public async Task SearchPageTypeFilterTabsLinkWithTypeParameter()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=archive");

        Assert.Contains("href=\"/search?q=archive&amp;type=news\"", body);
        Assert.Contains("href=\"/search?q=archive&amp;type=forum\"", body);
    }

    [Fact]
    public async Task SearchPageInvalidTypeFilterFallsBackToAllResults()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=archive&type=not-a-real-type");

        Assert.DoesNotContain("No results found", body);
    }

    [Fact]
    public async Task SearchPageShowsPaginationWhenResultsExceedPageSize()
    {
        var client = factory.CreateClient();

        // "archive" matches many seed threads (Archive sample thread 1004–1030)
        var body = await client.GetStringAsync("/search?q=archive&page=1");

        Assert.Contains("Page 1 of", body);
        Assert.Contains("Next", body);
    }

    [Fact]
    public async Task SearchPageExampleTagsLinkToSearch()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search");

        Assert.Contains("href=\"/search?q=Bohemian%20Rhapsody\"", body);
        Assert.Contains("href=\"/search?q=Freddie%20Mercury\"", body);
    }

    [Fact]
    public async Task SearchPageEmitsNoindexFollowRobotsTag()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search");

        Assert.Contains("""<meta name="robots" content="noindex,follow">""", body);
    }

    [Fact]
    public async Task SearchPageWithQueryEmitsNoindexFollowRobotsTag()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/search?q=studio+album");

        Assert.Contains("""<meta name="robots" content="noindex,follow">""", body);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/forum")]
    [InlineData("/news")]
    [InlineData("/articles")]
    public async Task IndexablePagesDoNotEmitNoindexRobotsTag(string path)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        Assert.DoesNotContain("""<meta name="robots" content="noindex""", body);
    }
}

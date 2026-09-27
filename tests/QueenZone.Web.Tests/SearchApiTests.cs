using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Pages;

namespace QueenZone.Web.Tests;

public sealed class SearchApiTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory timeoutHost;

    public SearchApiTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        timeoutHost = variants.Get(WebHostVariants.SiteSearchTimeout);
    }

    [Fact]
    public async Task Search_requires_no_auth_and_returns_forum_hit_for_studio_album()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=studio+album");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.Contains(
            payload!.Items,
            item => item.Title.Contains("Ranking every studio album", StringComparison.OrdinalIgnoreCase)
                && item.Url.Contains("/forum/topic/1002/", StringComparison.Ordinal)
                && item.SourceKey == "forum-thread:1002"
                && item.Id == 1002);
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.SourceKey)));
        Assert.All(payload.Items, item => Assert.DoesNotContain("<mark", item.Summary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_includes_news_for_modernisation()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=modernisation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.Contains(
            payload!.Items,
            item => item.Title == "QueenZone modernisation begins"
                && item.Url.Contains("/news/1003/", StringComparison.Ordinal)
                && item.SourceKey == "news:1003"
                && item.Id == 1003
                && item.ContentType == SiteSearchContentType.News);
    }

    [Fact]
    public async Task Search_unmatched_query_returns_empty_page()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=xyzzy_no_match_zzzqq");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(0, payload.TotalPages);
        Assert.Equal(1, payload.Page);
        Assert.Equal(SearchModel.PageSize, payload.PageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    public async Task Search_empty_or_whitespace_query_returns_empty_page(string query)
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(1, payload.Page);
        Assert.Equal(SearchModel.PageSize, payload.PageSize);
    }

    [Fact]
    public async Task Search_type_biography_returns_chapter_mentioning_bohemian_rhapsody()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=Bohemian%20Rhapsody&type=biography");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal(SiteSearchContentType.Biography, item.ContentType));
        Assert.Contains(
            payload.Items,
            item => item.Title == "1975"
                && item.SourceKey == "biography:3"
                && item.Url.Contains("/biography/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_type_discography_returns_album_for_bohemian_rhapsody()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=Bohemian%20Rhapsody&type=discography");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal(SiteSearchContentType.Discography, item.ContentType));
        Assert.Contains(
            payload.Items,
            item => item.Title == "A Night at the Opera"
                && item.SourceKey == "discography:4"
                && item.Url == "/discography/albums/4/a-night-at-the-opera");
    }

    [Fact]
    public async Task Search_type_discography_returns_album_for_album_name()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(
            $"{SearchApiEndpoints.Path}?q=A%20Night%20at%20the%20Opera&type=discography");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal(SiteSearchContentType.Discography, item.ContentType));
        Assert.Contains(
            payload.Items,
            item => item.Title == "A Night at the Opera"
                && item.SourceKey == "discography:4"
                && item.Url == "/discography/albums/4/a-night-at-the-opera");
    }

    [Fact]
    public async Task Search_type_article_is_not_aliased_to_legacy_article()
    {
        using var client = factory.CreateAnonymousClient();

        using var articleResponse = await client.GetAsync($"{SearchApiEndpoints.Path}?q=Bohemian%20Rhapsody&type=article");
        using var legacyResponse = await client.GetAsync($"{SearchApiEndpoints.Path}?q=Bohemian%20Rhapsody&type=legacy-article");

        Assert.Equal(HttpStatusCode.OK, articleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        var articles = await articleResponse.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        var legacy = await legacyResponse.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(articles);
        Assert.NotNull(legacy);
        Assert.All(articles!.Items, item => Assert.Equal(SiteSearchContentType.Article, item.ContentType));
        Assert.DoesNotContain(articles.Items, item => item.ContentType == SiteSearchContentType.LegacyArticle);
        Assert.Contains(
            legacy!.Items,
            item => item.ContentType == SiteSearchContentType.LegacyArticle
                && item.Title == "Inside the Making of Bohemian Rhapsody");
    }

    [Fact]
    public async Task Search_type_news_hides_forum_paths()
    {
        using var client = factory.CreateAnonymousClient();

        using var allResponse = await client.GetAsync($"{SearchApiEndpoints.Path}?q=studio+album");
        using var newsResponse = await client.GetAsync($"{SearchApiEndpoints.Path}?q=studio+album&type=news");

        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newsResponse.StatusCode);
        var all = await allResponse.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        var newsOnly = await newsResponse.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(all);
        Assert.NotNull(newsOnly);
        Assert.Contains(all!.Items, item => item.Url.Contains("/forum/topic/", StringComparison.Ordinal));
        Assert.DoesNotContain(newsOnly!.Items, item => item.Url.Contains("/forum/topic/", StringComparison.Ordinal));
        Assert.All(newsOnly.Items, item => Assert.Equal(SiteSearchContentType.News, item.ContentType));
    }

    [Fact]
    public async Task Search_unknown_type_searches_all_types()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=studio+album&type=not-a-real-type");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.Contains(payload.Items, item => item.Url.Contains("/forum/topic/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=archive&page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<SearchResultDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
        Assert.True(payload.Items.Count <= ApiPagination.MaxPageSize);
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.SourceKey)));
    }

    [Fact]
    public async Task Search_sql_timeout_returns_problem_details_504_not_empty_page()
    {
        using var client = timeoutHost.CreateAnonymousClient();

        using var response = await client.GetAsync($"{SearchApiEndpoints.Path}?q=Bohemian+Rhapsody");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.GetProperty("status").GetInt32());
        Assert.Equal("Gateway Timeout", problem.GetProperty("title").GetString());
        Assert.Equal(SearchApiEndpoints.TimeoutDetail, problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("items", out _));
        Assert.DoesNotContain("Page Not Found", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_raw_sql_timeout_logs_warning_and_returns_504()
    {
        var logger = new CollectingLogger<object>();
        var loggerFactory = new CollectingLoggerFactory(logger);
        var timeout = SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");

        var result = await SearchApiEndpoints.SearchAsync(
            new TimeoutSiteSearchService(timeout),
            loggerFactory,
            "Bohemian Rhapsody",
            null,
            1,
            20,
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.StatusCode);
        Assert.Equal(SearchApiEndpoints.TimeoutDetail, problem.ProblemDetails.Detail);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Bohemian Rhapsody", warning.Message, StringComparison.Ordinal);
        Assert.Same(timeout, warning.Exception);
    }

    [Fact]
    public async Task Search_typed_timeout_returns_504_without_double_logging()
    {
        var logger = new CollectingLogger<object>();
        var result = await SearchApiEndpoints.SearchAsync(
            new TimeoutSiteSearchService(),
            new CollectingLoggerFactory(logger),
            "Bohemian Rhapsody",
            null,
            1,
            20,
            CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Search_empty_query_still_returns_ok_when_logger_is_present()
    {
        var result = await SearchApiEndpoints.SearchAsync(
            new InMemorySiteSearchService(new SharedSearchIndexStore()),
            NullLoggerFactory.Instance,
            "   ",
            null,
            1,
            20,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<ApiPagedResponse<SearchResultDto>>>(result);
        Assert.Empty(ok.Value!.Items);
    }

    [Fact]
    public async Task OpenApi_document_includes_search_path()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.OpenApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("paths").TryGetProperty("/api/v1/search", out _));
    }

    [Fact]
    public void Mapper_parses_numeric_source_keys_and_leaves_slugs_null()
    {
        var news = SearchApiMapper.ToItem(SampleResult(SiteSearchContentType.News, "news:1003", "/news/1003/title"));
        Assert.Equal(1003, news.Id);
        Assert.Equal("news:1003", news.SourceKey);

        var thread = SearchApiMapper.ToItem(
            SampleResult(SiteSearchContentType.Forum, "forum-thread:1002", "/forum/topic/1002/title"));
        Assert.Equal(1002, thread.Id);

        var article = SearchApiMapper.ToItem(
            SampleResult(SiteSearchContentType.Article, "article:some-slug", "/articles/some-slug"));
        Assert.Null(article.Id);

        var fan = SearchApiMapper.ToItem(
            SampleResult(SiteSearchContentType.FanPerformance, "fan-performance:187", "/fan-performances"));
        Assert.Equal(187, fan.Id);
        Assert.DoesNotContain("<mark", fan.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ef_and_in_memory_maps_include_source_key()
    {
        var row = new EfSiteSearchService.SiteSearchRow
        {
            ContentType = SiteSearchContentType.News,
            SourceKey = "news:42",
            Title = "Title",
            Summary = "Plain excerpt",
            Url = "/news/42/title",
        };
        var fromRow = EfSiteSearchService.Map(row);
        Assert.Equal("news:42", fromRow.SourceKey);
        Assert.Equal("Plain excerpt", fromRow.Summary);

        var fromMemory = InMemorySiteSearchService.Map(new SearchDocumentEntity
        {
            SourceKey = "biography:7",
            ContentType = SiteSearchContentType.Biography,
            Title = "Chapter",
            Summary = "Summary",
            Url = "/biography/7",
        });
        Assert.Equal("biography:7", fromMemory.SourceKey);
        Assert.Equal(SiteSearchContentType.Biography, fromMemory.ContentType);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("news", null)]
    [InlineData("news:", null)]
    [InlineData("article:some-slug", null)]
    [InlineData("news:1003", 1003)]
    [InlineData("forum-thread:4521", 4521)]
    [InlineData("legacy-article:88", 88)]
    [InlineData("timeline:12", 12)]
    [InlineData("discography:3", 3)]
    public void Source_key_numeric_id_parsing(string? sourceKey, int? expected)
    {
        Assert.Equal(expected, SearchDocumentSourceKey.TryParseNumericId(sourceKey));
    }

    [Fact]
    public void Source_key_factories_match_reindex_shapes()
    {
        Assert.Equal("news:42", SearchDocumentSourceKey.ForNews(42));
        Assert.Equal("article:community-slug", SearchDocumentSourceKey.ForArticle("community-slug"));
        Assert.Equal("biography:7", SearchDocumentSourceKey.ForBiography(7));
        Assert.Equal("forum-thread:4521", SearchDocumentSourceKey.ForForumThread(4521));
    }

    private static SiteSearchResult SampleResult(string contentType, string sourceKey, string url) =>
        new(contentType, sourceKey, "Title", "Plain excerpt", url, null, null, null, null);
}

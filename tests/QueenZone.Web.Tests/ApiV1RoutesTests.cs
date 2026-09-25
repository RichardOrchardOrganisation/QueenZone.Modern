using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace QueenZone.Web.Tests;

public sealed class ApiV1RoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ApiV1RoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Discovery_returns_version_openapi_and_conventions()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.Prefix);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ApiV1.Version, payload.GetProperty("version").GetString());
        Assert.Equal(ApiV1.OpenApiPath, payload.GetProperty("openApi").GetString());

        var conventions = payload.GetProperty("conventions");
        Assert.Equal("camelCase", conventions.GetProperty("json").GetProperty("propertyNaming").GetString());
        Assert.Equal("application/problem+json", conventions.GetProperty("errors").GetProperty("mediaType").GetString());

        var pagination = conventions.GetProperty("pagination");
        Assert.Equal(ApiPagination.DefaultPage, pagination.GetProperty("defaultPage").GetInt32());
        Assert.Equal(ApiPagination.DefaultPageSize, pagination.GetProperty("defaultPageSize").GetInt32());
        Assert.Equal(ApiPagination.MaxPageSize, pagination.GetProperty("maxPageSize").GetInt32());
        Assert.Equal("page", pagination.GetProperty("pageQuery").GetString());
        Assert.Equal("pageSize", pagination.GetProperty("pageSizeQuery").GetString());
    }

    [Fact]
    public async Task Unknown_api_path_returns_problem_details_not_html()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status404NotFound, payload.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", payload.GetProperty("title").GetString());
        Assert.Contains("/api/v1/does-not-exist", payload.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Page Not Found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthenticated_api_session_returns_problem_details()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var response = await client.GetAsync(MobileAuthEndpoints.SessionPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status401Unauthorized, payload.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task OpenApi_document_includes_v1_and_excludes_site_endpoints()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.OpenApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("QueenZone API", payload.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal(ApiV1.Version, payload.GetProperty("info").GetProperty("version").GetString());
        Assert.Contains("Problem Details", payload.GetProperty("info").GetProperty("description").GetString(), StringComparison.Ordinal);

        var paths = payload.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/", out _) || paths.TryGetProperty("/api/v1", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/token", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/session", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/articles", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/articles/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/quotes/random", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/quotes/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/timeline", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/timeline/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/trivia/random", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/home-poll", out _));
        Assert.True(paths.GetProperty("/api/v1/content/home-poll").TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/home-poll/votes", out _));
        Assert.True(paths.GetProperty("/api/v1/content/home-poll/votes").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/photos/categories", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/photos/categories/{slug}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/photos/categories/{slug}/items", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/photos/categories/{slug}/items/{picId}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/fan-performances", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/fan-performances/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/content/fan-performances/{id}/audio", out _));
        Assert.True(paths.TryGetProperty("/api/v1/search", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/stats", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/categories", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/categories/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/categories/{id}/topics", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}/posts", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/categories/{id}/topics").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/posts").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}/poll", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/poll").TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}/poll/vote", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/poll/vote").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}/poll/close", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/poll/close").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/topics/{id}/watch", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/watch").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/watch").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/watch").TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/api/v1/member/photo-submissions", out _));
        Assert.True(paths.GetProperty("/api/v1/member/photo-submissions").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/member/fan-performance-submissions", out _));
        Assert.True(paths.GetProperty("/api/v1/member/fan-performance-submissions").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/fan-performances/{id}/report", out _));
        Assert.True(paths.GetProperty("/api/v1/me/fan-performances/{id}/report").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/submissions/fan-performances", out _));
        Assert.True(paths.TryGetProperty("/api/v1/member/news-suggestions", out _));
        Assert.True(paths.GetProperty("/api/v1/member/news-suggestions").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/messages", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/messages/unread-count", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/messages/{conversationId}", out _));
        Assert.True(paths.GetProperty("/api/v1/me/messages/{conversationId}").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/messages/{conversationId}/messages/{messageId}/report", out _));
        Assert.True(paths.GetProperty("/api/v1/me/messages/{conversationId}/messages/{messageId}/report").TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/submissions/photos", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/submissions/news", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/submissions/articles", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/notification-preferences", out _));
        Assert.True(paths.GetProperty("/api/v1/me/notification-preferences").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/me/notification-preferences").TryGetProperty("patch", out _));
        Assert.True(paths.GetProperty("/api/v1/contact")
            .GetProperty("post")
            .GetProperty("responses")
            .TryGetProperty("429", out _));
        Assert.True(paths.GetProperty("/api/v1/me")
            .GetProperty("patch")
            .GetProperty("responses")
            .TryGetProperty("429", out _));
        Assert.True(paths.GetProperty("/api/v1/forum/topics/{id}/watch")
            .GetProperty("post")
            .GetProperty("responses")
            .TryGetProperty("429", out _));
        Assert.False(paths.GetProperty("/api/v1/forum/topics/{id}/watch")
            .GetProperty("get")
            .GetProperty("responses")
            .TryGetProperty("429", out _));
        Assert.True(paths.TryGetProperty("/api/v1/admin/", out _) || paths.TryGetProperty("/api/v1/admin", out _));
        Assert.False(paths.TryGetProperty("/health", out _));
        Assert.False(paths.TryGetProperty("/api/uploads/editor-image", out _));
        Assert.False(paths.TryGetProperty("/news", out _));

        var schemes = payload.GetProperty("components").GetProperty("securitySchemes");
        Assert.True(schemes.TryGetProperty("bearer", out var bearer));
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Site_post_without_antiforgery_still_returns_bad_request_not_not_found()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var response = await client.PostAsync("/contact", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Existing_html_not_found_page_is_unchanged()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/missing-archive-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page Not Found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unversioned_upload_api_is_not_treated_as_v1()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(EditorImageUploadEndpoints.Route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page Not Found", body, StringComparison.Ordinal);
    }
}

public sealed class ApiPaginationTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-3, -1, 1, 20)]
    [InlineData(2, 50, 2, 50)]
    [InlineData(1, 1000, 1, 100)]
    public void Normalize_clamps_page_and_page_size(int? page, int? pageSize, int expectedPage, int expectedPageSize)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedPageSize, request.PageSize);
    }

    [Theory]
    [InlineData(null, null, 15, 15, 1, 15)]
    [InlineData(0, 0, 15, 15, 1, 15)]
    [InlineData(2, 1000, 15, 15, 2, 15)]
    [InlineData(1, 10, 15, 15, 1, 10)]
    public void Normalize_honours_per_endpoint_default_and_max(
        int? page,
        int? pageSize,
        int defaultPageSize,
        int maxPageSize,
        int expectedPage,
        int expectedPageSize)
    {
        var request = ApiPagination.Normalize(page, pageSize, defaultPageSize, maxPageSize);
        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedPageSize, request.PageSize);
    }

    [Fact]
    public void Paged_response_computes_total_pages()
    {
        var page = ApiPagedResponse<string>.Create(["a", "b"], page: 2, pageSize: 2, totalCount: 5);
        Assert.Equal(["a", "b"], page.Items);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public void Paged_response_empty_collection_has_zero_total_pages()
    {
        var page = ApiPagedResponse<int>.Create([], page: 1, pageSize: 20, totalCount: 0);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void Paged_response_serializes_camel_case_envelope()
    {
        var json = JsonSerializer.Serialize(
            ApiPagedResponse<string>.Create(["a"], page: 1, pageSize: 20, totalCount: 1),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"items\":", json, StringComparison.Ordinal);
        Assert.Contains("\"page\":", json, StringComparison.Ordinal);
        Assert.Contains("\"pageSize\":", json, StringComparison.Ordinal);
        Assert.Contains("\"totalCount\":", json, StringComparison.Ordinal);
        Assert.Contains("\"totalPages\":", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1", true)]
    [InlineData("/api/v1/", true)]
    [InlineData("/api/v1/auth/token", true)]
    [InlineData("/api/uploads/editor-image", false)]
    [InlineData("/health", false)]
    [InlineData("/news", false)]
    public void IsApiPath_only_matches_versioned_prefix(string path, bool expected)
    {
        Assert.Equal(expected, ApiV1.IsApiPath(path));
    }
}

public sealed class ApiV1ErrorHandlingTests
{
    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("get", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("PUT", "PUT")]
    [InlineData("DELETE", "DELETE")]
    [InlineData("PATCH", "PATCH")]
    [InlineData("HEAD", "HEAD")]
    [InlineData("OPTIONS", "OPTIONS")]
    [InlineData("TRACE", "OTHER")]
    [InlineData("GET\nSet-Cookie: injected", "OTHER")]
    [InlineData("", "OTHER")]
    public void SanitizeHttpMethodForLog_maps_known_verbs_only(string method, string expected)
    {
        Assert.Equal(expected, ApiV1ErrorHandling.SanitizeHttpMethodForLog(method));
    }

    [Fact]
    public void SanitizeForLog_strips_line_breaks_and_truncates()
    {
        Assert.Equal("OTHER", ApiV1ErrorHandling.SanitizeHttpMethodForLog(null));
        Assert.Equal("/api/v1/news", ApiV1ErrorHandling.SanitizeForLog("/api/v1/news"));
        Assert.Equal("/api/v1/x injected", ApiV1ErrorHandling.SanitizeForLog("/api/v1/x\r\n injected"));
        Assert.Equal(string.Empty, ApiV1ErrorHandling.SanitizeForLog(null));

        var longPath = "/api/v1/" + new string('a', 400);
        var sanitized = ApiV1ErrorHandling.SanitizeForLog(longPath);
        Assert.Equal(256, sanitized.Length);
        Assert.StartsWith("/api/v1/", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_writer_logs_event_id_and_template()
    {
        var logger = new CollectingLogger<object>();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new CollectingLoggerFactory(logger));
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = "/api/v1/news";
        http.Response.Body = new MemoryStream();
        http.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("boom"),
            Path = "/api/v1/news",
        });

        await ApiV1ErrorHandling.WriteUnhandledExceptionAsync(http);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error
                && entry.EventId.Id == 1700
                && entry.EventId.Name == "UnhandledExceptionOnRequest"
                && entry.Message == "Unhandled exception on GET /api/v1/news"
                && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Unhandled_exception_writer_returns_generic_problem_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        await ApiV1ErrorHandling.WriteUnhandledExceptionAsync(http);

        Assert.Equal(StatusCodes.Status500InternalServerError, http.Response.StatusCode);
        Assert.Equal("application/problem+json", http.Response.ContentType?.Split(';')[0].Trim());
        http.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("An unexpected error occurred.", doc.RootElement.GetProperty("title").GetString());
        Assert.False(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Status_code_writer_skips_responses_that_already_have_content_type()
    {
        var http = CreateHttpContext();
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync("""{"error":"invalid_request"}""");

        await ApiV1ErrorHandling.WriteStatusCodeProblemAsync(http);

        http.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(http.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Equal("""{"error":"invalid_request"}""", body);
    }

    [Fact]
    public async Task Status_code_writer_writes_problem_details_for_empty_responses()
    {
        var http = CreateHttpContext();
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await ApiV1ErrorHandling.WriteStatusCodeProblemAsync(http);

        Assert.Equal("application/problem+json", http.Response.ContentType?.Split(';')[0].Trim());
        http.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal(StatusCodes.Status401Unauthorized, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Unauthorized", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Empty_error_middleware_writes_problem_details_after_next()
    {
        var http = CreateHttpContext();

        await ApiV1ErrorHandling.WriteProblemDetailsForEmptyErrorsAsync(http, context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        Assert.Equal("application/problem+json", http.Response.ContentType?.Split(';')[0].Trim());
        http.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal(StatusCodes.Status404NotFound, doc.RootElement.GetProperty("status").GetInt32());
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        return http;
    }
}

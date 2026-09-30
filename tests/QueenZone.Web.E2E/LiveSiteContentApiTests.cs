using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QueenZone.Web.E2E;

/// <summary>
/// HTTP shape sweep of the public mobile JSON API (<c>/api/v1</c>, issues #726 / #731 / #732 / #734) for the
/// live-site job and the nightly RealData suite. Anonymous read-only routes only —
/// no <c>/api/v1/auth</c> (rate-limited writes) and no <c>/api/v1/admin</c> (Entra).
/// Discovers detail ids from list responses instead of hardcoding archive records.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
[Category(E2ECategories.ReadOnly)]
public class LiveSiteContentApiTests : RealDataPageTest
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const int SamplePageSize = 1;
    private static string PhotoCdnOrigin
    {
        get
        {
            var configuredHost = Environment.GetEnvironmentVariable("E2E_EXPECTED_PHOTO_HOST");
            var host = string.IsNullOrWhiteSpace(configuredHost)
                ? "cdn.queenzone.org"
                : configuredHost.Trim().TrimEnd('/');
            return $"https://{host}/";
        }
    }

    private static readonly string[] RequiredOpenApiPaths =
    [
        "/api/v1/content/news",
        "/api/v1/content/news/{id}",
        "/api/v1/content/articles",
        "/api/v1/content/articles/{id}",
        "/api/v1/content/biography",
        "/api/v1/content/biography/{id}",
        "/api/v1/content/discography",
        "/api/v1/content/discography/{id}",
        "/api/v1/content/timeline",
        "/api/v1/content/timeline/{id}",
        "/api/v1/content/freddietribute",
        "/api/v1/content/photos/categories",
        "/api/v1/content/photos/categories/{slug}",
        "/api/v1/content/photos/categories/{slug}/items",
        "/api/v1/content/photos/categories/{slug}/items/{picId}",
        "/api/v1/content/fan-performances",
        "/api/v1/content/fan-performances/{id}",
        "/api/v1/content/fan-performances/{id}/audio",
        "/api/v1/search",
        "/api/v1/forum/categories",
        "/api/v1/forum/categories/{id}",
        "/api/v1/forum/categories/{id}/topics",
        "/api/v1/forum/topics/{id}",
        "/api/v1/forum/topics/{id}/posts",
        "/api/v1/forum/topics/{id}/poll",
        "/api/v1/forum/topics/{id}/poll/vote",
        "/api/v1/forum/topics/{id}/poll/close",
    ];

    private static readonly ContentListSpec[] ContentLists =
    [
        new(
            ListPath: "/api/v1/content/news",
            DetailPathTemplate: "/api/v1/content/news/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["title", "publishedAt", "detailPath"],
            RequiredDetailStrings: ["title", "body", "publishedAt", "detailPath"]),
        new(
            ListPath: "/api/v1/content/articles",
            DetailPathTemplate: "/api/v1/content/articles/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["title", "publishedAt", "detailPath"],
            RequiredDetailStrings: ["title", "body", "publishedAt", "detailPath"]),
        new(
            ListPath: "/api/v1/content/biography",
            DetailPathTemplate: "/api/v1/content/biography/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["title", "summary", "detailPath"],
            RequiredDetailStrings: ["title", "body", "detailPath"]),
        new(
            ListPath: "/api/v1/content/discography",
            DetailPathTemplate: "/api/v1/content/discography/{0}",
            IdProperty: "albumId",
            RequiredItemStrings: ["name", "detailPath"],
            RequiredDetailStrings: ["name", "artistName"]),
        new(
            ListPath: "/api/v1/content/timeline",
            DetailPathTemplate: "/api/v1/content/timeline/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["title", "summary", "eventDate", "formattedDate", "category", "categoryLabel"],
            RequiredDetailStrings: ["title", "summary", "eventDate", "formattedDate", "category", "categoryLabel"]),
        new(
            ListPath: "/api/v1/content/freddietribute",
            DetailPathTemplate: null,
            IdProperty: "id",
            RequiredItemStrings: ["name", "thought", "dateText"],
            RequiredDetailStrings: []),
        new(
            ListPath: "/api/v1/content/fan-performances",
            DetailPathTemplate: "/api/v1/content/fan-performances/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["title", "performedBy", "dateAdded", "detailPath", "audioPath"],
            RequiredDetailStrings: ["title", "performedBy", "dateAdded", "detailPath", "audioPath"]),
        new(
            ListPath: "/api/v1/forum/categories",
            DetailPathTemplate: "/api/v1/forum/categories/{0}",
            IdProperty: "id",
            RequiredItemStrings: ["name", "detailPath"],
            RequiredDetailStrings: ["name", "detailPath"]),
    ];

    protected override bool AllowsWrites => false;

    [Test]
    public async Task DiscoveryAndOpenApi_MeetShapeAssertionsAsync()
    {
        using var client = CreateHttpClient();
        var failures = new List<string>();

        var discovery = await TryGetJsonAsync(client, "/api/v1", failures);
        if (discovery is { } discoveryDoc)
        {
            AssertString(discoveryDoc, "version", "v1", "/api/v1", failures);
            AssertString(discoveryDoc, "openApi", "/openapi/v1.json", "/api/v1", failures);

            if (TryGetObject(discoveryDoc, "conventions", "/api/v1", failures, out var conventions)
                && TryGetObject(conventions, "json", "/api/v1.conventions", failures, out var json)
                && TryGetObject(conventions, "errors", "/api/v1.conventions", failures, out var errors)
                && TryGetObject(conventions, "pagination", "/api/v1.conventions", failures, out var pagination))
            {
                AssertString(json, "propertyNaming", "camelCase", "/api/v1", failures);
                AssertString(errors, "mediaType", "application/problem+json", "/api/v1", failures);
                AssertString(pagination, "pageQuery", "page", "/api/v1", failures);
                AssertString(pagination, "pageSizeQuery", "pageSize", "/api/v1", failures);

                if (pagination.TryGetProperty("responseFields", out var fields)
                    && fields.ValueKind == JsonValueKind.Array)
                {
                    var names = fields.EnumerateArray().Select(e => e.GetString()).ToList();
                    var expected = new[] { "items", "page", "pageSize", "totalCount", "totalPages" };
                    if (!names.SequenceEqual(expected))
                    {
                        failures.Add(
                            $"/api/v1 conventions.pagination.responseFields was [{string.Join(", ", names)}]; " +
                            $"expected [{string.Join(", ", expected)}].");
                    }
                }
                else
                {
                    failures.Add("/api/v1 conventions.pagination.responseFields must be a JSON array.");
                }
            }
        }

        var openApi = await TryGetJsonAsync(client, "/openapi/v1.json", failures);
        if (openApi is { } openApiDoc)
        {
            if (TryGetObject(openApiDoc, "info", "/openapi/v1.json", failures, out var info))
            {
                AssertString(info, "version", "v1", "/openapi/v1.json", failures);
            }

            if (TryGetObject(openApiDoc, "paths", "/openapi/v1.json", failures, out var paths))
            {
                foreach (var path in RequiredOpenApiPaths)
                {
                    if (!HasOpenApiPath(paths, path))
                    {
                        failures.Add($"/openapi/v1.json is missing path '{path}'.");
                    }
                }
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            FailurePrefix() + "API discovery/OpenAPI sweep failures:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task ContentListsAndSampledDetails_MeetShapeAssertionsAsync()
    {
        using var client = CreateHttpClient();
        var failures = new List<string>();

        foreach (var spec in ContentLists)
        {
            var listPath = $"{spec.ListPath}?page=1&pageSize={SamplePageSize}";
            var list = await TryGetJsonAsync(client, listPath, failures);
            if (list is null)
            {
                continue;
            }

            AssertPagedEnvelope(list.Value, listPath, failures);
            if (!list.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (items.GetArrayLength() == 0)
            {
                failures.Add($"{listPath}: expected at least one item in the public archive list.");
                continue;
            }

            var item = items[0];
            AssertRequiredStrings(item, spec.RequiredItemStrings, listPath, failures);

            if (!TryGetPositiveInt(item, spec.IdProperty, out var id))
            {
                failures.Add($"{listPath}: first item is missing a positive integer '{spec.IdProperty}'.");
                continue;
            }

            if (spec.DetailPathTemplate is null)
            {
                continue;
            }

            var detailPath = string.Format(spec.DetailPathTemplate, id);
            var detail = await TryGetJsonAsync(client, detailPath, failures);
            if (detail is null)
            {
                continue;
            }

            if (!TryGetPositiveInt(detail.Value, spec.IdProperty, out var detailId) || detailId != id)
            {
                failures.Add($"{detailPath}: '{spec.IdProperty}' must equal list id {id}.");
            }

            AssertRequiredStrings(detail.Value, spec.RequiredDetailStrings, detailPath, failures);

            if (spec.ListPath.EndsWith("/discography", StringComparison.Ordinal)
                && (!detail.Value.TryGetProperty("songs", out var songs)
                    || songs.ValueKind != JsonValueKind.Array))
            {
                failures.Add($"{detailPath}: 'songs' must be a JSON array.");
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            FailurePrefix() + "Content API sweep failures:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task PhotoGalleries_MeetShapeAssertionsAsync()
    {
        using var client = CreateHttpClient();
        var failures = new List<string>();

        var listPath = $"/api/v1/content/photos/categories?page=1&pageSize={SamplePageSize}";
        var list = await TryGetJsonAsync(client, listPath, failures);
        if (list is null)
        {
            Assert.That(
                failures,
                Is.Empty,
                FailurePrefix() + "Photo gallery sweep could not read categories:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
            return;
        }

        AssertPagedEnvelope(list.Value, listPath, failures);
        if (!list.Value.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() == 0)
        {
            failures.Add($"{listPath}: expected at least one public photo category.");
            Assert.That(
                failures,
                Is.Empty,
                FailurePrefix() + "Photo gallery sweep failures:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
            return;
        }

        AssertRequiredStrings(items[0], ["name", "slug", "detailPath"], listPath, failures);
        AssertOptionalCdnUrl(items[0], "coverThumbnailUrl", listPath, failures);

        var slug = items[0].TryGetProperty("slug", out var slugProperty)
            && slugProperty.ValueKind == JsonValueKind.String
            ? slugProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(slug))
        {
            failures.Add($"{listPath}: first category is missing a slug.");
            Assert.That(
                failures,
                Is.Empty,
                FailurePrefix() + "Photo gallery sweep failures:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
            return;
        }

        var categoryPath = $"/api/v1/content/photos/categories/{slug}";
        var category = await TryGetJsonAsync(client, categoryPath, failures);
        if (category is { } categoryDoc)
        {
            AssertRequiredStrings(categoryDoc, ["name", "slug", "detailPath"], categoryPath, failures);
            AssertOptionalCdnUrl(categoryDoc, "coverThumbnailUrl", categoryPath, failures);
        }

        var itemsPath = $"/api/v1/content/photos/categories/{slug}/items?page=1&pageSize={SamplePageSize}";
        var photos = await TryGetJsonAsync(client, itemsPath, failures);
        if (photos is { } photosDoc)
        {
            AssertPagedEnvelope(photosDoc, itemsPath, failures);
            if (photosDoc.TryGetProperty("items", out var photoItems)
                && photoItems.ValueKind == JsonValueKind.Array
                && photoItems.GetArrayLength() > 0)
            {
                AssertRequiredStrings(
                    photoItems[0],
                    ["title", "thumbnailUrl", "categorySlug", "detailPath", "categoryPath"],
                    itemsPath,
                    failures);
                AssertCdnUrl(photoItems[0], "thumbnailUrl", itemsPath, failures);
                if (photoItems[0].TryGetProperty("imageUrl", out _))
                {
                    failures.Add($"{itemsPath}: list items must not include full 'imageUrl'.");
                }

                if (TryGetPositiveInt(photoItems[0], "picId", out var picId))
                {
                    var detailPath = $"/api/v1/content/photos/categories/{slug}/items/{picId}";
                    var detail = await TryGetJsonAsync(client, detailPath, failures);
                    if (detail is { } detailDoc)
                    {
                        AssertRequiredStrings(
                            detailDoc,
                            ["title", "imageUrl", "thumbnailUrl", "detailPath", "categoryPath"],
                            detailPath,
                            failures);
                        AssertCdnUrl(detailDoc, "imageUrl", detailPath, failures);
                        AssertCdnUrl(detailDoc, "thumbnailUrl", detailPath, failures);
                        if (!TryGetPositiveInt(detailDoc, "picId", out var detailId) || detailId != picId)
                        {
                            failures.Add($"{detailPath}: 'picId' must equal list id {picId}.");
                        }
                    }
                }
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            FailurePrefix() + "Photo gallery sweep failures:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task ForumCategoryTopics_MeetShapeAssertionsAsync()
    {
        using var client = CreateHttpClient();
        var failures = new List<string>();

        var list = await TryGetJsonAsync(client, "/api/v1/forum/categories?page=1&pageSize=1", failures);
        if (list is null
            || !list.Value.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() == 0
            || !TryGetPositiveInt(items[0], "id", out var categoryId))
        {
            Assert.That(
                failures,
                Is.Empty,
                FailurePrefix() + "Forum topics sweep could not read a public category:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
            return;
        }

        var topicsPath = $"/api/v1/forum/categories/{categoryId}/topics?page=1&pageSize={SamplePageSize}";
        var topics = await TryGetJsonAsync(client, topicsPath, failures);
        if (topics is { } topicsDoc)
        {
            AssertPagedEnvelope(topicsDoc, topicsPath, failures);
            if (topicsDoc.TryGetProperty("items", out var topicItems)
                && topicItems.ValueKind == JsonValueKind.Array
                && topicItems.GetArrayLength() > 0)
            {
                AssertRequiredStrings(
                    topicItems[0],
                    ["title", "authorUsername", "lastActivityAt", "detailPath"],
                    topicsPath,
                    failures);

                if (TryGetPositiveInt(topicItems[0], "id", out var topicId))
                {
                    var topicPath = $"/api/v1/forum/topics/{topicId}";
                    var topic = await TryGetJsonAsync(client, topicPath, failures);
                    if (topic is { } topicDoc)
                    {
                        AssertRequiredStrings(
                            topicDoc,
                            ["title", "forumName", "categoryPath", "detailPath"],
                            topicPath,
                            failures);

                        if (topicDoc.TryGetProperty("hasPoll", out var hasPoll)
                            && hasPoll.ValueKind is JsonValueKind.True)
                        {
                            var pollPath = $"/api/v1/forum/topics/{topicId}/poll";
                            var poll = await TryGetJsonAsync(client, pollPath, failures);
                            if (poll is { } pollDoc)
                            {
                                AssertRequiredStrings(
                                    pollDoc,
                                    ["pollId", "question"],
                                    pollPath,
                                    failures);
                                if (!pollDoc.TryGetProperty("options", out var options)
                                    || options.ValueKind != JsonValueKind.Array)
                                {
                                    failures.Add($"{pollPath}: 'options' must be a JSON array.");
                                }
                            }
                        }
                    }

                    var postsPath = $"/api/v1/forum/topics/{topicId}/posts?page=1&pageSize={SamplePageSize}";
                    var posts = await TryGetJsonAsync(client, postsPath, failures);
                    if (posts is { } postsDoc)
                    {
                        AssertPagedEnvelope(postsDoc, postsPath, failures);
                        if (postsDoc.TryGetProperty("items", out var postItems)
                            && postItems.ValueKind == JsonValueKind.Array
                            && postItems.GetArrayLength() > 0)
                        {
                            AssertRequiredStrings(
                                postItems[0],
                                ["body", "authorUsername", "postedAt"],
                                postsPath,
                                failures);
                            if (!postItems[0].TryGetProperty("attachments", out var attachments)
                                || attachments.ValueKind != JsonValueKind.Array)
                            {
                                failures.Add($"{postsPath}: first post 'attachments' must be a JSON array.");
                            }
                        }
                    }
                }
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            FailurePrefix() + "Forum topics sweep failures:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task UnknownApiPath_ReturnsProblemDetailsNotHtmlAsync()
    {
        using var client = CreateHttpClient();
        using var response = await GetWithLiveSiteRetryAsync(client, "/api/v1/does-not-exist");
        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound),
            FailurePrefix() + $"/api/v1/does-not-exist: expected HTTP 404, got {(int)response.StatusCode}.");
        Assert.That(
            mediaType,
            Is.EqualTo("application/problem+json"),
            FailurePrefix() + $"/api/v1/does-not-exist: expected application/problem+json, got '{mediaType}'.");
        Assert.That(
            body,
            Does.Not.Contain("Page Not Found"),
            FailurePrefix() + "/api/v1/does-not-exist must not return the HTML not-found page.");

        using var document = JsonDocument.Parse(body);
        var payload = document.RootElement;
        Assert.That(payload.GetProperty("status").GetInt32(), Is.EqualTo(404));
        Assert.That(payload.GetProperty("title").GetString(), Is.EqualTo("Not Found"));
        Assert.That(
            payload.GetProperty("detail").GetString(),
            Does.Contain("/api/v1/does-not-exist"));
    }

    private HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = RequestTimeout,
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private Uri ToAbsoluteUri(string path)
    {
        var root = BaseUrl.TrimEnd('/');
        return new Uri(root + path);
    }

    private Task<HttpResponseMessage> GetWithLiveSiteRetryAsync(HttpClient client, string path) =>
        LiveSiteTransportRetry.RunAsync(() => client.GetAsync(ToAbsoluteUri(path)));

    private async Task<JsonElement?> TryGetJsonAsync(
        HttpClient client,
        string path,
        List<string> failures)
    {
        HttpResponseMessage response;
        try
        {
            response = await GetWithLiveSiteRetryAsync(client, path);
        }
        catch (Exception ex)
        {
            failures.Add($"{path}: request failed ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }

        using (response)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add($"{path}: expected HTTP 200, got {(int)response.StatusCode} {response.StatusCode}.");
                return null;
            }

            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{path}: expected application/json, got '{mediaType}'.");
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                failures.Add($"{path}: body is not JSON ({ex.Message}).");
                return null;
            }
        }
    }

    private static void AssertPagedEnvelope(JsonElement payload, string path, List<string> failures)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            failures.Add($"{path}: 'items' must be a JSON array.");
            return;
        }

        if (!payload.TryGetProperty("page", out var page) || page.ValueKind != JsonValueKind.Number
            || page.GetInt32() != 1)
        {
            failures.Add($"{path}: 'page' must be 1.");
        }

        if (!payload.TryGetProperty("pageSize", out var pageSize) || pageSize.ValueKind != JsonValueKind.Number
            || pageSize.GetInt32() != SamplePageSize)
        {
            failures.Add($"{path}: 'pageSize' must be {SamplePageSize}.");
        }

        if (!payload.TryGetProperty("totalCount", out var totalCount)
            || totalCount.ValueKind != JsonValueKind.Number)
        {
            failures.Add($"{path}: 'totalCount' must be a number.");
        }
        else if (totalCount.GetInt32() < items.GetArrayLength())
        {
            failures.Add($"{path}: 'totalCount' must be >= items.Length.");
        }

        if (!payload.TryGetProperty("totalPages", out var totalPages)
            || totalPages.ValueKind != JsonValueKind.Number)
        {
            failures.Add($"{path}: 'totalPages' must be a number.");
        }
    }

    private static void AssertCdnUrl(JsonElement obj, string name, string path, List<string> failures)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            failures.Add($"{path}: '{name}' must be a CDN URL.");
            return;
        }

        var url = value.GetString();
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(PhotoCdnOrigin, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{path}: '{name}' must start with {PhotoCdnOrigin}.");
        }
    }

    private static void AssertOptionalCdnUrl(JsonElement obj, string name, string path, List<string> failures)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            failures.Add($"{path}: '{name}' must be a string or null.");
            return;
        }

        var url = value.GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!url.StartsWith(PhotoCdnOrigin, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{path}: '{name}' must start with {PhotoCdnOrigin} when present.");
        }
    }

    private static void AssertRequiredStrings(
        JsonElement obj,
        IReadOnlyList<string> names,
        string path,
        List<string> failures)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                failures.Add($"{path}: '{name}' must be a non-empty string.");
            }
        }
    }

    private static void AssertString(
        JsonElement obj,
        string name,
        string expected,
        string path,
        List<string> failures)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            failures.Add($"{path}: '{name}' must be '{expected}'.");
        }
    }

    private static bool TryGetObject(
        JsonElement obj,
        string name,
        string path,
        List<string> failures,
        out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        failures.Add($"{path}: '{name}' must be a JSON object.");
        value = default;
        return false;
    }

    private static bool TryGetPositiveInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!property.TryGetInt32(out value) || value <= 0)
        {
            return false;
        }

        return true;
    }

    private static bool HasOpenApiPath(JsonElement paths, string path) =>
        paths.TryGetProperty(path, out _)
        || paths.TryGetProperty(path.TrimEnd('/') + "/", out _)
        || paths.TryGetProperty(path.TrimEnd('/'), out _);

    private static string FailurePrefix() =>
        RealDataMarkers.IsReadOnlyMode() ? "PRODUCTION LIVE-SITE: " : string.Empty;

    private sealed record ContentListSpec(
        string ListPath,
        string? DetailPathTemplate,
        string IdProperty,
        string[] RequiredItemStrings,
        string[] RequiredDetailStrings);
}

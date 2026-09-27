using System.Net;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class AdminTimelineRoutesTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory writes;

    public AdminTimelineRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        writes = variants.Get(WebHostVariants.IsolatedAdminTimeline);
    }

    public Task InitializeAsync() => writes.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnonymousUserCannotAccessAdminTimeline()
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/timeline");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanListSeedEvents()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Freddie Mercury born", body);
        Assert.Contains("/admin/timeline/new", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenNewForm()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/timeline/new");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add timeline event", body);
        Assert.Contains("name=\"title\"", body);
        Assert.Contains("name=\"eventDate\"", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenEditFormForSeedEvent()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/timeline/1/edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit timeline event", body);
        Assert.Contains("Freddie Mercury born", body);
    }

    [Fact]
    public async Task AuthorizedAdminGetsNotFoundForMissingEvent()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/timeline/99999/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostCreate_shows_title_on_admin_list_and_public_timeline()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF create {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, title, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/admin/timeline", create.Headers.Location?.OriginalString);

        var list = await client.GetAsync(create.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(title, await list.Content.ReadAsStringAsync());

        var publicTimeline = await client.GetStringAsync("/timeline");
        Assert.Contains(title, publicTimeline);
        Assert.Contains(title, store.GetAll().Select(item => item.Title));
    }

    [Fact]
    public async Task PostCreate_checked_publish_box_stays_published()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF published checkbox {Guid.NewGuid():N}";
        var formPage = await client.GetStringAsync("/admin/timeline/new");
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage)),
            new("title", title),
            new("summary", "Created by AdminTimelineRoutesTests."),
            new("eventDate", DateTime.UtcNow.ToString("yyyy-MM-dd")),
            new("datePrecision", nameof(QueenHistoryDatePrecision.ExactDate)),
            new("category", nameof(QueenHistoryEventCategory.Other)),
            new("importance", "50"),
            new("sourceUrl", string.Empty),
            new("isPublished", "true"),
        };

        var create = await client.PostAsync("/admin/timeline", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.True(store.GetAll().Single(item => item.Title == title).IsPublished);
        Assert.Contains(title, await client.GetStringAsync("/timeline"));
    }

    [Fact]
    public async Task PostCreate_validation_error_redisplays_the_form()
    {
        var client = CreateWriteClient();
        var formPage = await client.GetStringAsync("/admin/timeline/new");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage),
            ["title"] = "   ",
            ["summary"] = "Created by AdminTimelineRoutesTests.",
            ["eventDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["datePrecision"] = nameof(QueenHistoryDatePrecision.ExactDate),
            ["category"] = nameof(QueenHistoryEventCategory.Other),
            ["importance"] = "50",
            ["sourceUrl"] = string.Empty,
            ["isPublished"] = "true",
        };

        var response = await client.PostAsync("/admin/timeline", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required", body);
        Assert.Contains("Add timeline event", body);
    }

    [Fact]
    public async Task PostEdit_updates_title_on_the_admin_form_and_public_timeline()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var original = $"WAF edit source {Guid.NewGuid():N}";
        var updated = $"WAF edit saved {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, original, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == original).Id;

        var editPage = await client.GetStringAsync($"/admin/timeline/{id}/edit");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(editPage),
            ["title"] = updated,
            ["summary"] = "Updated by AdminTimelineRoutesTests.",
            ["eventDate"] = "1985-07-13",
            ["datePrecision"] = nameof(QueenHistoryDatePrecision.ExactDate),
            ["category"] = nameof(QueenHistoryEventCategory.Concert),
            ["importance"] = "90",
            ["sourceUrl"] = string.Empty,
            ["isPublished"] = "true",
        };

        var save = await client.PostAsync($"/admin/timeline/{id}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal($"/admin/timeline/{id}/edit", save.Headers.Location?.OriginalString);

        var savedPage = await client.GetStringAsync(save.Headers.Location);
        Assert.Contains(updated, savedPage);
        Assert.Contains(updated, await client.GetStringAsync("/timeline"));
        Assert.DoesNotContain(original, await client.GetStringAsync("/timeline"));
    }

    [Fact]
    public async Task PostEdit_stale_row_version_reloads_current_values()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var original = $"WAF conflict source {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, original, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == original).Id;

        var editPage = await client.GetStringAsync($"/admin/timeline/{id}/edit");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(editPage);
        var staleRowVersion = store.GetById(id)!.RowVersion!;
        store.Update(id, new AdminQueenHistoryDraft(
            "Someone else saved this",
            "Updated by another admin.",
            new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc),
            QueenHistoryDatePrecision.ExactDate,
            QueenHistoryEventCategory.Concert,
            90,
            null,
            true));

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = "Stale overwrite",
            ["summary"] = "Should not win.",
            ["eventDate"] = "1985-07-13",
            ["datePrecision"] = nameof(QueenHistoryDatePrecision.ExactDate),
            ["category"] = nameof(QueenHistoryEventCategory.Concert),
            ["importance"] = "90",
            ["sourceUrl"] = string.Empty,
            ["isPublished"] = "true",
            ["rowVersion"] = Convert.ToBase64String(staleRowVersion),
        };

        using var save = await client.PostAsync($"/admin/timeline/{id}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var body = await save.Content.ReadAsStringAsync();
        Assert.Contains(OptimisticConcurrencyException.UserMessage, body, StringComparison.Ordinal);
        Assert.Contains("Someone else saved this", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale overwrite", body, StringComparison.Ordinal);
        Assert.Equal("Someone else saved this", store.GetById(id)!.Title);
    }

    [Fact]
    public async Task PostDelete_removes_created_event()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF delete {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, title, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == title).Id;

        var delete = await PostDeleteAsync(client, id);
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/admin/timeline", delete.Headers.Location?.OriginalString);

        var list = await client.GetStringAsync("/admin/timeline");
        Assert.DoesNotContain(title, list);
        Assert.DoesNotContain(store.GetAll(), item => item.Title == title);
    }

    [Fact]
    public async Task PostTogglePublish_hides_event_from_public_timeline()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF unpublish {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, title, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == title).Id;
        Assert.Contains(title, await client.GetStringAsync("/timeline"));

        var listPage = await client.GetStringAsync("/admin/timeline");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["isPublished"] = "true",
        };
        var toggle = await client.PostAsync(
            "/admin/timeline?handler=TogglePublish",
            new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);

        var publicTimeline = await client.GetStringAsync("/timeline");
        Assert.DoesNotContain(title, publicTimeline);
        Assert.False(store.GetById(id)!.IsPublished);
    }

    [Fact]
    public async Task PostDelete_preservesFiltersAndPageNumberOnRedirect()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF delete filtered {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, title, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == title).Id;

        var listPage = await client.GetStringAsync("/admin/timeline");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["PageNumber"] = "2",
            ["Published"] = "unpublished",
            ["Q"] = "queen",
        };

        var delete = await client.PostAsync("/admin/timeline?handler=Delete", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(
            "/admin/timeline?pageNumber=2&published=unpublished&q=queen",
            delete.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostTogglePublish_preservesFiltersAndPageNumberOnRedirect()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var title = $"WAF toggle filtered {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, title, isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Title == title).Id;

        var listPage = await client.GetStringAsync("/admin/timeline");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["isPublished"] = "true",
            ["PageNumber"] = "3",
            ["Published"] = "published",
            ["Q"] = "mercury",
        };

        var toggle = await client.PostAsync("/admin/timeline?handler=TogglePublish", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);
        Assert.Equal(
            "/admin/timeline?pageNumber=3&published=published&q=mercury",
            toggle.Headers.Location?.OriginalString);
    }

    private SharedQueenHistoryStore WriteStore => writes.AdminTimeline!;

    private HttpClient CreateWriteClient() =>
        AdminHttpTestHelpers.CreateClient(writes, AdminHttpTestHelpers.AdminEmail);

    private static async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string title, bool isPublished)
    {
        var formPage = await client.GetStringAsync("/admin/timeline/new");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage),
            ["title"] = title,
            ["summary"] = "Created by AdminTimelineRoutesTests.",
            ["eventDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["datePrecision"] = nameof(QueenHistoryDatePrecision.ExactDate),
            ["category"] = nameof(QueenHistoryEventCategory.Other),
            ["importance"] = "50",
            ["sourceUrl"] = string.Empty,
            ["isPublished"] = isPublished ? "true" : "false",
        };

        return await client.PostAsync("/admin/timeline", new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostDeleteAsync(HttpClient client, int id)
    {
        var listPage = await client.GetStringAsync("/admin/timeline");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
        };

        return await client.PostAsync("/admin/timeline?handler=Delete", new FormUrlEncodedContent(fields));
    }
}

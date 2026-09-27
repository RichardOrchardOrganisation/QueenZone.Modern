using System.Net;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class AdminTriviaRoutesTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory writes;

    public AdminTriviaRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        writes = variants.Get(WebHostVariants.IsolatedTrivia);
    }

    public Task InitializeAsync() => writes.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnonymousUserCannotAccessAdminTrivia()
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/trivia");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousUserCannotAccessNewOrEditTrivia()
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var newResponse = await client.GetAsync("/admin/trivia/new");
        var editResponse = await client.GetAsync("/admin/trivia/1/edit");

        Assert.Equal(HttpStatusCode.Unauthorized, newResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, editResponse.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanListSeedFacts()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/trivia");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Trivia", body);
        Assert.Contains("/admin/trivia/new", body);
        Assert.Contains("Freddie Mercury was born Farrokh Bulsara", body);
        Assert.Contains("/admin/trivia/1/edit", body);
        Assert.Contains("Filter by category", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanFilterListByCategory()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/trivia?category=Albums");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("A Night at the Opera", body);
        Assert.DoesNotContain("Freddie Mercury was born Farrokh Bulsara", body);
        Assert.DoesNotContain("Red Special", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenNewTriviaForm()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/trivia/new");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add trivia fact", body);
        Assert.Contains("name=\"text\"", body);
        Assert.Contains("name=\"category\"", body);
        Assert.Contains("name=\"difficulty\"", body);
        Assert.Contains("name=\"source\"", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenEditFormForSeedFact()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/trivia/1/edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit trivia fact", body);
        Assert.Contains("Freddie Mercury was born Farrokh Bulsara", body);
    }

    [Fact]
    public async Task AuthorizedAdminGetsNotFoundForMissingFact()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/trivia/99999/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostCreate_shows_fact_on_admin_list()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var text = $"WAF create {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, text, category: "Tours", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/admin/trivia", create.Headers.Location?.OriginalString);

        var list = await client.GetAsync(create.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(text, await list.Content.ReadAsStringAsync());
        Assert.Contains(text, store.GetAll().Select(item => item.Text));
    }

    [Fact]
    public async Task PostCreate_validation_error_redisplays_the_form()
    {
        var client = CreateWriteClient();
        var formPage = await client.GetStringAsync("/admin/trivia/new");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage),
            ["text"] = "   ",
            ["category"] = "Band",
            ["difficulty"] = TriviaDifficulty.Easy,
            ["source"] = string.Empty,
            ["isPublished"] = "true",
        };

        var response = await client.PostAsync("/admin/trivia", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fact text is required", body);
        Assert.Contains("Add trivia fact", body);
    }

    [Fact]
    public async Task PostEdit_updates_fact_on_the_admin_form()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var original = $"WAF edit source {Guid.NewGuid():N}";
        var updated = $"WAF edit saved {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, original, category: "Band", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Text == original).Id;

        var editPage = await client.GetStringAsync($"/admin/trivia/{id}/edit");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(editPage),
            ["text"] = updated,
            ["category"] = "Albums",
            ["difficulty"] = TriviaDifficulty.Hard,
            ["source"] = "Updated by AdminTriviaRoutesTests.",
            ["isPublished"] = "true",
        };

        var save = await client.PostAsync($"/admin/trivia/{id}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal($"/admin/trivia/{id}/edit", save.Headers.Location?.OriginalString);

        var savedPage = await client.GetStringAsync(save.Headers.Location);
        Assert.Contains(updated, savedPage);
        var saved = store.GetById(id);
        Assert.NotNull(saved);
        Assert.Equal(updated, saved.Text);
        Assert.Equal("Albums", saved.Category);
        Assert.Equal(TriviaDifficulty.Hard, saved.Difficulty);
    }

    [Fact]
    public async Task PostDelete_removes_created_fact()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var text = $"WAF delete {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, text, category: "Band", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Text == text).Id;

        var listPage = await client.GetStringAsync("/admin/trivia");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
        };
        var delete = await client.PostAsync("/admin/trivia?handler=Delete", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/admin/trivia", delete.Headers.Location?.OriginalString);

        var list = await client.GetStringAsync("/admin/trivia");
        Assert.DoesNotContain(text, list);
        Assert.DoesNotContain(store.GetAll(), item => item.Text == text);
    }

    [Fact]
    public async Task PostTogglePublish_unpublishes_fact()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var text = $"WAF unpublish {Guid.NewGuid():N}";

        var create = await PostCreateAsync(client, text, category: "Band", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Text == text).Id;
        Assert.True(store.GetById(id)!.IsPublished);

        var listPage = await client.GetStringAsync("/admin/trivia");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["isPublished"] = "true",
        };
        var toggle = await client.PostAsync(
            "/admin/trivia?handler=TogglePublish",
            new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);

        Assert.False(store.GetById(id)!.IsPublished);
        Assert.Null(store.GetRandomPublished());
    }

    [Fact]
    public async Task PostDelete_preservesCategoryFilterAndPageNumberOnRedirect()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var text = $"WAF delete filtered {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, text, category: "Albums", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Text == text).Id;

        var listPage = await client.GetStringAsync("/admin/trivia");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["PageNumber"] = "2",
            ["category"] = "Albums",
        };

        var delete = await client.PostAsync("/admin/trivia?handler=Delete", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/admin/trivia?pageNumber=2&category=Albums", delete.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostTogglePublish_preservesCategoryFilterAndPageNumberOnRedirect()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var text = $"WAF toggle filtered {Guid.NewGuid():N}";
        var create = await PostCreateAsync(client, text, category: "Band", isPublished: true);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = store.GetAll().Single(item => item.Text == text).Id;

        var listPage = await client.GetStringAsync("/admin/trivia");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage),
            ["id"] = id.ToString(),
            ["isPublished"] = "true",
            ["PageNumber"] = "3",
            ["category"] = "Band",
        };

        var toggle = await client.PostAsync("/admin/trivia?handler=TogglePublish", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);
        Assert.Equal("/admin/trivia?pageNumber=3&category=Band", toggle.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ListIsPaginatedWhenFactCountExceedsPageSize()
    {
        var store = WriteStore;
        var client = CreateWriteClient();
        var marker = $"WAF page {Guid.NewGuid():N}";
        for (var i = 0; i < AdminTriviaRoutes.ListPageSize + 1; i++)
        {
            await PostCreateAsync(client, $"{marker} {i}", category: "Paging", isPublished: true);
        }

        var firstPage = await client.GetStringAsync("/admin/trivia?category=Paging");
        var secondPage = await client.GetStringAsync("/admin/trivia?category=Paging&pageNumber=2");

        Assert.Contains("archive-pagination", firstPage);
        Assert.Contains($"{marker} {AdminTriviaRoutes.ListPageSize}", firstPage);
        Assert.DoesNotContain($"{marker} 0", firstPage);
        Assert.Contains($"{marker} 0", secondPage);
    }

    private SharedTriviaStore WriteStore => writes.Trivia!;

    private HttpClient CreateWriteClient() =>
        AdminHttpTestHelpers.CreateClient(writes, AdminHttpTestHelpers.AdminEmail);

    private static async Task<HttpResponseMessage> PostCreateAsync(
        HttpClient client,
        string text,
        string? category,
        bool isPublished)
    {
        var formPage = await client.GetStringAsync("/admin/trivia/new");
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage),
            ["text"] = text,
            ["category"] = category ?? string.Empty,
            ["difficulty"] = TriviaDifficulty.Easy,
            ["source"] = "Created by AdminTriviaRoutesTests.",
            ["isPublished"] = isPublished ? "true" : "false",
        };

        return await client.PostAsync("/admin/trivia", new FormUrlEncodedContent(fields));
    }
}

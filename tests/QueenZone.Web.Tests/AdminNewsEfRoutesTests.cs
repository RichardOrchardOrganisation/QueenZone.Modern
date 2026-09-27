using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminNewsEfRoutesTests : IClassFixture<AdminEfWebApplicationFactory>, IAsyncLifetime
{
    private readonly AdminEfWebApplicationFactory factory;

    public AdminNewsEfRoutesTests(AdminEfWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ef_backed_create_edit_publish_unpublish_delete_round_trip()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var createResponse = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "EF integration article",
                ["excerpt"] = "Created through SQLite EF.",
                ["body"] = "Plain text body persisted by EF.",
                ["publishedAt"] = "2026-06-14"
            });

        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
            var created = await repository.GetByIdAsync(articleId);
            Assert.NotNull(created);
            Assert.Equal("EF integration article", created.Title);
            Assert.False(created.IsPublished);
        }

        var editResponse = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            $"/admin/news/{articleId}/edit",
            $"/admin/news/{articleId}",
            new Dictionary<string, string>
            {
                ["title"] = "EF integration article updated",
                ["excerpt"] = "Updated excerpt",
                ["body"] = "Updated body text",
                ["publishedAt"] = "2026-06-15"
            });
        Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
            var updated = await repository.GetByIdAsync(articleId);
            Assert.NotNull(updated);
            Assert.Equal("EF integration article updated", updated.Title);
            Assert.Equal(new DateTime(2026, 6, 15), updated.PublishedAt.Date);
        }

        var beforePublishDate = DateTime.UtcNow.Date;
        var publishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(
            client,
            $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
            var published = await repository.GetByIdAsync(articleId);
            Assert.NotNull(published);
            Assert.True(published.IsPublished);
            Assert.InRange(published.PublishedAt.Date, beforePublishDate, DateTime.UtcNow.Date);
        }

        var unpublishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(
            client,
            $"/admin/news/{articleId}/unpublish");
        Assert.Equal(HttpStatusCode.Redirect, unpublishResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
            var unpublished = await repository.GetByIdAsync(articleId);
            Assert.NotNull(unpublished);
            Assert.False(unpublished.IsPublished);
        }

        var deleteResponse = await AdminHttpTestHelpers.PostNewsActionAsync(
            client,
            $"/admin/news/{articleId}/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
            Assert.Null(await repository.GetByIdAsync(articleId));
        }
    }

    [Fact]
    public async Task Ef_backed_publish_rejects_invalid_draft()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var newsId = await repository.CreateDraftAsync(
            new AdminNewsDraft(
                new string('x', NewsValidation.MaxTitleLength + 1),
                "invalid-title",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
                null),
            AdminHttpTestHelpers.AdminEmail);

        var publishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(
            client,
            $"/admin/news/{newsId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);
        Assert.Equal($"/admin/news/{newsId}/edit", publishResponse.Headers.Location!.OriginalString);

        var editBody = await client.GetStringAsync($"/admin/news/{newsId}/edit");
        Assert.Contains($"Title must be {NewsValidation.MaxTitleLength} characters or fewer.", editBody);

        var article = await repository.GetByIdAsync(newsId);
        Assert.NotNull(article);
        Assert.False(article.IsPublished);
    }
}

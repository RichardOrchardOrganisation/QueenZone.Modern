using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class EfAdminNewsRepositoryTests : IAsyncDisposable
{
    internal const string SqliteLatestNewsSql = AdminNewsSqliteTestHarness.LatestNewsSql;

    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfAdminNewsRepository repository;

    public EfAdminNewsRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        AdminNewsSqliteTestHarness.EnsureNewsTable(dbContext);
        AdminNewsSqliteTestHarness.SeedArticle(dbContext, 4201, "SQLite admin article");
        repository = new EfAdminNewsRepository(dbContext, SqliteLatestNewsSql);
    }

    [Fact]
    public async Task GetAllAsync_materializes_FromSql_results()
    {
        var articles = await repository.GetAllAsync();

        Assert.Single(articles);
        Assert.Equal(4201, articles[0].Id);
    }

    [Fact]
    public async Task GetByIdAsync_materializes_FromSql_results_without_composing_SingleOrDefault()
    {
        var article = await repository.GetByIdAsync(4201);

        Assert.NotNull(article);
        Assert.Equal(4201, article.Id);
        Assert.Equal("SQLite admin article", article.Title);
        Assert.False(article.IsPublished);
        Assert.Null(article.ImageBlobKey);
        Assert.Null(article.ImageGalleryPicId);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_article_is_missing()
    {
        var article = await repository.GetByIdAsync(9999);

        Assert.Null(article);
    }

    [Fact]
    public async Task UpdateAsync_persists_changes_readable_via_GetByIdAsync()
    {
        var draft = new AdminNewsDraft(
            "Updated sqlite title",
            "updated-sqlite-title",
            "Updated excerpt",
            "Updated body",
            new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            null);

        await repository.UpdateAsync(4201, draft, "editor@test.local");

        var article = await repository.GetByIdAsync(4201);
        Assert.NotNull(article);
        Assert.Equal("Updated sqlite title", article.Title);
        Assert.Equal("Updated excerpt", article.Excerpt);
        Assert.Equal("Updated body", article.Body);
    }

    [Fact]
    public async Task DeleteAsync_removes_article_from_GetByIdAsync()
    {
        await repository.DeleteAsync(4201, "editor@test.local");

        var article = await repository.GetByIdAsync(4201);
        Assert.Null(article);
    }

    [Fact]
    public async Task GetById_then_DeleteAsync_matches_admin_delete_workflow()
    {
        var loaded = await repository.GetByIdAsync(4201);
        Assert.NotNull(loaded);
        Assert.Equal("SQLite admin article", loaded.Title);

        await repository.DeleteAsync(4201, "editor@test.local");

        Assert.Null(await repository.GetByIdAsync(4201));
    }

    [Fact]
    public async Task PublishAsync_sets_display_flag_and_publication_date()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET "DATE" = '2024-06-10' WHERE NEWS_ID = 4201;
            """);
        var beforePublishDate = DateTime.UtcNow.Date;

        await repository.PublishAsync(4201, "editor@test.local");

        var article = await repository.GetByIdAsync(4201);
        Assert.NotNull(article);
        Assert.True(article.IsPublished);
        Assert.InRange(article.PublishedAt.Date, beforePublishDate, DateTime.UtcNow.Date);
    }

    [Fact]
    public async Task TrySetForumTopicIdAsync_sets_once_and_is_noop_when_already_set()
    {
        Assert.True(await repository.TrySetForumTopicIdAsync(4201, 88001));
        var first = await repository.GetByIdAsync(4201);
        Assert.Equal(88001, first!.ForumTopicId);

        Assert.False(await repository.TrySetForumTopicIdAsync(4201, 88002));
        var second = await repository.GetByIdAsync(4201);
        Assert.Equal(88001, second!.ForumTopicId);

        await repository.UnpublishAsync(4201, "editor@test.local");
        var unpublished = await repository.GetByIdAsync(4201);
        Assert.False(unpublished!.IsPublished);
        Assert.Equal(88001, unpublished.ForumTopicId);
    }

    [Fact]
    public async Task UnpublishAsync_clears_display_flag()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET DISPLAY = 1 WHERE NEWS_ID = 4201;
            """);

        await repository.UnpublishAsync(4201, "editor@test.local");

        var article = await repository.GetByIdAsync(4201);
        Assert.NotNull(article);
        Assert.False(article.IsPublished);
    }

    [Fact]
    public async Task IsSlugInUseAsync_detects_existing_slug()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = 'shared-slug' WHERE NEWS_ID = 4201;
            """);

        Assert.True(await repository.IsSlugInUseAsync("shared-slug"));
        Assert.False(await repository.IsSlugInUseAsync("shared-slug", excludeNewsId: 4201));
        Assert.False(await repository.IsSlugInUseAsync("unused-slug"));
    }

    [Fact]
    public async Task IsSlugInUseAsync_detects_slugless_legacy_row_via_title()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = NULL, TITLE = 'Legacy Title Slug' WHERE NEWS_ID = 4201;
            """);

        Assert.True(await repository.IsSlugInUseAsync("legacy-title-slug"));
        Assert.False(await repository.IsSlugInUseAsync("legacy-title-slug", excludeNewsId: 4201));
    }

    [Fact]
    public async Task IsSlugInUseAsync_create_conflict_when_another_draft_owns_slug()
    {
        await repository.CreateDraftAsync(
            new AdminNewsDraft(
                "First draft",
                "create-conflict-slug",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
                null),
            "editor@test.local");

        Assert.True(await repository.IsSlugInUseAsync("create-conflict-slug"));
    }

    [Fact]
    public async Task IsSlugInUseAsync_edit_allows_same_slug_on_current_article_only()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = 'edit-own-slug' WHERE NEWS_ID = 4201;
            """);
        AdminNewsSqliteTestHarness.SeedArticle(dbContext, 4202, "Other article");
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = 'other-slug' WHERE NEWS_ID = 4202;
            """);

        Assert.False(await repository.IsSlugInUseAsync("edit-own-slug", excludeNewsId: 4201));
        Assert.True(await repository.IsSlugInUseAsync("other-slug", excludeNewsId: 4201));
    }

    [Fact]
    public async Task IsSlugInUseAsync_publish_conflict_when_resolved_slug_collides()
    {
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = 'publish-collision' WHERE NEWS_ID = 4201;
            """);
        AdminNewsSqliteTestHarness.SeedArticle(dbContext, 4202, "Draft waiting publish");
        dbContext.Database.ExecuteSql($"""
            UPDATE NEWS_T SET SLUG = 'publish-collision' WHERE NEWS_ID = 4202;
            """);

        // Publishing 4202 must see 4201's colliding slug.
        Assert.True(await repository.IsSlugInUseAsync("publish-collision", excludeNewsId: 4202));
    }

    [Fact]
    public async Task DeleteAsync_throws_when_foreign_key_blocks_delete()
    {
        dbContext.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS NEWS_REF_TEST (
                ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                NEWS_ID INTEGER NOT NULL,
                FOREIGN KEY (NEWS_ID) REFERENCES NEWS_T(NEWS_ID)
            );
            """);
        dbContext.Database.ExecuteSql($"""
            INSERT INTO NEWS_REF_TEST (NEWS_ID) VALUES (4201);
            """);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await repository.DeleteAsync(4201, "editor@test.local"));
    }

    [Fact]
    public async Task CreateDraftAsync_uses_database_generated_identity()
    {
        var draft = new AdminNewsDraft(
            "Created draft",
            "created-draft",
            "Created excerpt",
            "Created body",
            new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
            null);

        var newsId = await repository.CreateDraftAsync(draft, "editor@test.local");

        Assert.True(newsId > 4201);
        var article = await repository.GetByIdAsync(newsId);
        Assert.NotNull(article);
        Assert.Equal("Created draft", article.Title);
    }

    [Fact]
    public async Task CreateDraftAsync_preserves_source_url_within_validation_limit()
    {
        var sourceUrl = "https://www.queenonline.com/news/" + new string('a', 400);
        Assert.True(sourceUrl.Length <= NewsValidation.MaxSourceUrlLength);
        var draft = new AdminNewsDraft(
            "Created draft with source",
            "created-draft-with-source",
            "Created excerpt",
            "Created body",
            new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
            sourceUrl);

        var newsId = await repository.CreateDraftAsync(draft, "editor@test.local");

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SOURCE_URL FROM NEWS_T WHERE NEWS_ID = $newsId";
        command.Parameters.AddWithValue("$newsId", newsId);
        var savedSourceUrl = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Equal(sourceUrl, savedSourceUrl);
    }

    [Fact]
    public async Task CreateDraftAsync_and_UpdateAsync_persist_image_references_without_bytes()
    {
        var draft = new AdminNewsDraft(
            "Created draft with image",
            "created-draft-with-image",
            "Created excerpt",
            "Created body",
            new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
            null,
            "editors/me/hero.webp",
            3120);

        var newsId = await repository.CreateDraftAsync(draft, "editor@test.local");
        var created = await repository.GetByIdAsync(newsId);
        Assert.NotNull(created);
        Assert.Equal("editors/me/hero.webp", created.ImageBlobKey);
        Assert.Equal(3120, created.ImageGalleryPicId);

        await repository.UpdateAsync(
            newsId,
            draft with { ImageBlobKey = "gallery:99", ImageGalleryPicId = 99 },
            "editor@test.local");

        var updated = await repository.GetByIdAsync(newsId);
        Assert.NotNull(updated);
        Assert.Equal("gallery:99", updated.ImageBlobKey);
        Assert.Equal(99, updated.ImageGalleryPicId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IMAGE_BLOB_KEY, IMAGE_GALLERY_PIC_ID, typeof(IMAGE_BLOB_KEY)
            FROM NEWS_T WHERE NEWS_ID = $newsId
            """;
        command.Parameters.AddWithValue("$newsId", newsId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("gallery:99", reader.GetString(0));
        Assert.Equal(99, reader.GetInt32(1));
        Assert.Equal("text", reader.GetString(2), ignoreCase: true);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}

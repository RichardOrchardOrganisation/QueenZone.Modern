using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfEditorialArticleRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfEditorialArticleRepository repository;

    public EfEditorialArticleRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        repository = new EfEditorialArticleRepository(dbContext, TimeProvider.System);
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task Published_version_stays_live_while_replacement_draft_is_saved()
    {
        var saved = await repository.SaveDraftAsync(Draft("First title"), "admin");
        await repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");

        var working = await repository.SaveDraftAsync(Draft("Replacement title") with { Id = saved.Id }, "admin");

        Assert.Equal(EditorialArticleStatus.Draft, working.Status);
        var published = Assert.Single(await repository.GetPublishedStandaloneAsync());
        Assert.Equal(EfArticleSubmissionRepository.EstimateWordCount(published.Body), published.WordCount);
        var live = await repository.GetPublishedBySlugAsync("first-title");
        Assert.NotNull(live);
        Assert.Equal("First title", live.Title);
        Assert.Null(await repository.GetPublishedBySlugAsync("replacement-title"));
    }

    [Fact]
    public async Task SaveDraft_after_unpublish_keeps_standalone_article_off_the_public_site()
    {
        var saved = await repository.SaveDraftAsync(Draft("Live feature"), "admin");
        await repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");
        await repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Unpublished, "admin");

        var afterSave = await repository.SaveDraftAsync(
            Draft("Working copy after unpublish") with { Id = saved.Id },
            "admin");

        Assert.Equal(EditorialArticleStatus.Unpublished, afterSave.Status);
        Assert.True(afterSave.HasPublishedVersion);
        Assert.Equal("editors/admin/card.webp", afterSave.PublishedImageBlobKey);
        Assert.Null(await repository.GetPublishedBySlugAsync("live-feature"));
        Assert.Null(await repository.GetPublishedBySlugAsync("working-copy-after-unpublish"));
        Assert.Empty(await repository.GetPublishedStandaloneAsync());

        var row = await dbContext.EditorialArticles.AsNoTracking().SingleAsync(x => x.Id == saved.Id);
        Assert.Equal("Live feature", row.LiveTitle);
        Assert.Equal("live-feature", row.LiveSlug);
        Assert.Equal("Working copy after unpublish", row.Title);
    }

    [Fact]
    public async Task SaveDraft_after_unpublish_keeps_legacy_overlay_off_the_public_site()
    {
        var draft = await repository.SaveDraftAsync(Draft("Edited archive") with { LegacyArticleId = 101 }, "admin");
        await repository.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");
        await repository.SetStatusAsync(draft.Id, EditorialArticleStatus.Unpublished, "admin");

        var afterSave = await repository.SaveDraftAsync(
            Draft("Overlay working copy") with { Id = draft.Id, LegacyArticleId = 101 },
            "admin");

        Assert.Equal(EditorialArticleStatus.Unpublished, afterSave.Status);
        Assert.True(afterSave.HasPublishedVersion);
        var overlays = await repository.GetPublishedLegacyOverlaysAsync([101]);
        Assert.True(overlays.ContainsKey(101));
        Assert.Equal(EditorialArticleStatus.Unpublished, overlays[101].Status);
        Assert.Equal("Edited archive", overlays[101].Title);
        Assert.Equal(1, await repository.GetUnpublishedLegacyOverlayCountAsync());
    }

    [Fact]
    public async Task SaveDraft_rejects_duplicate_slug()
    {
        await repository.SaveDraftAsync(Draft("Taken slug"), "admin");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveDraftAsync(Draft("Taken slug"), "admin"));
        Assert.Equal("That article slug is already in use.", ex.Message);
    }

    private static EditorialArticleDraft Draft(string title) => new(
        null, null, null, title, null, "Excerpt", "<p>Body</p>", "Manual Author", "Interviews",
        "queen,freddie", null, "editors/admin/card.webp", DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
}

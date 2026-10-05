using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryEditorialArticleRepositoryTests
{
    [Fact]
    public async Task Published_version_stays_live_while_replacement_draft_is_saved()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var saved = await repo.SaveDraftAsync(Draft("First title"), "admin");
        await repo.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");

        await repo.SaveDraftAsync(Draft("Replacement title") with { Id = saved.Id }, "admin");

        var live = await repo.GetPublishedBySlugAsync("first-title");
        Assert.NotNull(live);
        Assert.Equal("First title", live.Title);
        Assert.Null(await repo.GetPublishedBySlugAsync("replacement-title"));
    }

    [Fact]
    public async Task Publishing_replacement_atomically_changes_live_version()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var saved = await repo.SaveDraftAsync(Draft("First title"), "admin");
        await repo.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");
        await repo.SaveDraftAsync(Draft("Replacement title") with { Id = saved.Id }, "admin");

        await repo.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");

        Assert.Null(await repo.GetPublishedBySlugAsync("first-title"));
        Assert.Equal("Replacement title", (await repo.GetPublishedBySlugAsync("replacement-title"))!.Title);
    }

    [Fact]
    public async Task Legacy_overlay_is_visible_only_after_publish()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var draft = await repo.SaveDraftAsync(Draft("Edited archive") with { LegacyArticleId = 101 }, "admin");
        Assert.Empty(await repo.GetPublishedLegacyOverlaysAsync([101]));
        await repo.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");
        Assert.Equal("Edited archive", (await repo.GetPublishedLegacyOverlaysAsync([101]))[101].Title);
        Assert.Equal("Edited archive", (await repo.GetAllLegacyOverlaysAsync())[101].Title);
    }

    [Fact]
    public async Task SaveDraft_after_unpublish_keeps_standalone_article_off_the_public_site()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var saved = await repo.SaveDraftAsync(Draft("Live feature"), "admin");
        await repo.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "admin");
        await repo.SetStatusAsync(saved.Id, EditorialArticleStatus.Unpublished, "admin");

        var afterSave = await repo.SaveDraftAsync(
            Draft("Working copy after unpublish") with { Id = saved.Id },
            "admin");

        Assert.Equal(EditorialArticleStatus.Unpublished, afterSave.Status);
        Assert.True(afterSave.HasPublishedVersion);
        Assert.Null(await repo.GetPublishedBySlugAsync("live-feature"));
        Assert.Null(await repo.GetPublishedBySlugAsync("working-copy-after-unpublish"));
        Assert.Empty(await repo.GetPublishedStandaloneAsync());
    }

    [Fact]
    public async Task SaveDraft_after_unpublish_keeps_legacy_overlay_off_the_public_site()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var articles = new QueenZone.Data.InMemoryArticlesRepository(
            [
                new ArticleItem(
                    101,
                    "Original archive title",
                    "Original excerpt",
                    "Original body",
                    new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    "Features",
                    true),
            ],
            repo);
        var draft = await repo.SaveDraftAsync(Draft("Edited archive") with { LegacyArticleId = 101 }, "admin");
        await repo.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");
        await repo.SetStatusAsync(draft.Id, EditorialArticleStatus.Unpublished, "admin");

        var afterSave = await repo.SaveDraftAsync(
            Draft("Overlay working copy") with { Id = draft.Id, LegacyArticleId = 101 },
            "admin");

        Assert.Equal(EditorialArticleStatus.Unpublished, afterSave.Status);
        Assert.True(afterSave.HasPublishedVersion);
        Assert.Null(await articles.GetByIdAsync(101));
        Assert.Empty(await articles.GetArchivePageAsync(1, 10));
        Assert.Empty(await articles.GetLatestAsync(10));
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_legacy_overlay_is_unpublished()
    {
        var repo = new InMemoryEditorialArticleRepository();
        var articles = new QueenZone.Data.InMemoryArticlesRepository(
            [
                new ArticleItem(
                    202,
                    "Visible until unpublished",
                    "Excerpt",
                    "Body",
                    new DateTime(2021, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    "Features",
                    true),
            ],
            repo);
        var draft = await repo.SaveDraftAsync(Draft("Hidden overlay") with { LegacyArticleId = 202 }, "admin");
        await repo.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");
        await repo.SetStatusAsync(draft.Id, EditorialArticleStatus.Unpublished, "admin");

        Assert.Null(await articles.GetByIdAsync(202));
    }

    private static EditorialArticleDraft Draft(string title) => new(
        null, null, null, title, null, "Excerpt", "<p>Body</p>", "Manual Author", "Interviews",
        "queen,freddie", null, "editors/admin/card.webp", DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
}

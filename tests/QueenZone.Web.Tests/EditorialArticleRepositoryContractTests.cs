using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class EditorialArticleRepositoryContractTests
{
    protected abstract IEditorialArticleRepository Repository { get; }
    internal static EditorialArticleDraft Draft(string title) => new(
        null, null, null, title, null, "Excerpt", "<p>Body</p>", "Manual Author", "Interviews",
        "queen,freddie", null, "editors/admin/card.webp", new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Draft_round_trips_normalized_fields_and_fixed_clock()
    {
        var draft = Draft("  First title  ");
        var saved = await Repository.SaveDraftAsync(draft, "editor");
        Assert.Equal("First title", saved.Title);
        Assert.Equal(NewsSlug.Resolve(draft.Title, draft.Slug), saved.Slug);
        Assert.Equal(EditorialArticleStatus.Draft, saved.Status);
        Assert.Equal("editor", saved.UpdatedBy);
        Assert.Equal(NewsEditorialContractClock.Now, saved.UpdatedAt);
        Assert.Equal(draft.PublishedAt, saved.PublishedAt);
        Assert.Equal(draft.Excerpt, saved.Excerpt);
        Assert.Equal(draft.Body, saved.Body);
        Assert.Equal(draft.AuthorName, saved.AuthorName);
        Assert.Equal(draft.Category, saved.Category);
        Assert.Equal(draft.Tags, saved.Tags);
        Assert.Equal(draft.ImageBlobKey, saved.ImageBlobKey);
        Assert.Equal(saved, await Repository.GetAsync(saved.Id));
        Assert.Null(await Repository.GetPublishedBySlugAsync(saved.Slug));
        Assert.Empty(await Repository.GetPublishedStandaloneAsync());
    }

    [Fact]
    public async Task Replacement_draft_keeps_live_snapshot_until_republished()
    {
        var saved = await Repository.SaveDraftAsync(Draft("First title"), "editor");
        var published = await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "publisher");
        Assert.Equal(NewsEditorialContractClock.Now, published!.UpdatedAt);
        Assert.Equal("First title", (await Repository.GetPublishedBySlugAsync("first-title"))!.Title);
        Assert.Equal("First title", Assert.Single(await Repository.GetPublishedStandaloneAsync()).Title);
        var working = await Repository.SaveDraftAsync(Draft("Replacement title") with { Id = saved.Id }, "editor");
        Assert.Equal(EditorialArticleStatus.Draft, working.Status);
        Assert.Equal("First title", (await Repository.GetPublishedBySlugAsync("first-title"))!.Title);
        Assert.Equal("First title", Assert.Single(await Repository.GetPublishedStandaloneAsync()).Title);
        Assert.Null(await Repository.GetPublishedBySlugAsync("replacement-title"));
        await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "publisher");
        Assert.Null(await Repository.GetPublishedBySlugAsync("first-title"));
        Assert.Equal("Replacement title", (await Repository.GetPublishedBySlugAsync("replacement-title"))!.Title);
        Assert.Equal("Replacement title", Assert.Single(await Repository.GetPublishedStandaloneAsync()).Title);
    }

    [Fact]
    public async Task Unpublished_standalone_stays_hidden_after_saving_working_copy()
    {
        var saved = await Repository.SaveDraftAsync(Draft("Live title"), "editor");
        await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "editor");
        await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Unpublished, "editor");
        var working = await Repository.SaveDraftAsync(Draft("Working title") with { Id = saved.Id }, "editor");
        Assert.Equal(EditorialArticleStatus.Unpublished, working.Status);
        Assert.True(working.HasPublishedVersion);
        Assert.Null(await Repository.GetPublishedBySlugAsync("live-title"));
        Assert.Null(await Repository.GetPublishedBySlugAsync("working-title"));
        Assert.Empty(await Repository.GetPublishedStandaloneAsync());
    }

    [Fact]
    public async Task Legacy_overlay_retains_unpublished_live_snapshot_and_never_becomes_standalone()
    {
        var saved = await Repository.SaveDraftAsync(Draft("Edited archive") with { LegacyArticleId = 101 }, "editor");
        Assert.Empty(await Repository.GetPublishedLegacyOverlaysAsync([101]));
        await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Published, "editor");
        Assert.Equal("Edited archive", (await Repository.GetPublishedLegacyOverlaysAsync([101]))[101].Title);
        Assert.Null(await Repository.GetPublishedBySlugAsync("edited-archive"));
        Assert.Empty(await Repository.GetPublishedStandaloneAsync());
        await Repository.SetStatusAsync(saved.Id, EditorialArticleStatus.Unpublished, "editor");
        await Repository.SaveDraftAsync(Draft("Working overlay") with { Id = saved.Id, LegacyArticleId = 101 }, "editor");
        var overlay = (await Repository.GetPublishedLegacyOverlaysAsync([101]))[101];
        Assert.Equal(EditorialArticleStatus.Unpublished, overlay.Status);
        Assert.Equal("Edited archive", overlay.Title);
        Assert.Equal(1, await Repository.GetUnpublishedLegacyOverlayCountAsync());
        Assert.Equal(EditorialArticleStatus.Unpublished, (await Repository.GetAllLegacyOverlaysAsync())[101].Status);
    }

    [Fact]
    public async Task Duplicate_slug_is_rejected_and_unknown_ids_return_null()
    {
        var saved = await Repository.SaveDraftAsync(Draft("Taken slug"), "editor");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SaveDraftAsync(Draft("Taken slug"), "editor"));
        Assert.Equal("That article slug is already in use.", error.Message);
        Assert.Equal(saved, await Repository.GetAsync(saved.Id));
        Assert.Null(await Repository.GetAsync(Guid.Empty));
        Assert.Null(await Repository.SetStatusAsync(Guid.Empty, EditorialArticleStatus.Published, "editor"));
    }
}

public sealed class InMemoryEditorialArticleRepositoryContractTests : EditorialArticleRepositoryContractTests
{
    protected override IEditorialArticleRepository Repository { get; } = new InMemoryEditorialArticleRepository(new NewsEditorialContractClock());
}

public sealed class EfEditorialArticleRepositoryContractTests : EditorialArticleRepositoryContractTests, IAsyncDisposable
{
    private readonly NewsEditorialContractFixture fixture = new();
    protected override IEditorialArticleRepository Repository => fixture.Editorial;
    public ValueTask DisposeAsync() => fixture.DisposeAsync();
}

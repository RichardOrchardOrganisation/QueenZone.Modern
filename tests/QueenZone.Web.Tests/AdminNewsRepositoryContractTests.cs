using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class AdminNewsRepositoryContractTests
{
    protected abstract IAdminNewsRepository Repository { get; }
    protected abstract INewsRepository Public { get; }
    private const string Editor = "editor@test.local";
    private static AdminNewsDraft Draft(string title = "Original") => new(title, null, "Excerpt", "Full body", new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), null);

    [Fact]
    public async Task Draft_creation_update_and_slug_lookup_persist_editorial_fields()
    {
        var draft = Draft() with { Slug = "  custom-slug  " };
        var id = await Repository.CreateDraftAsync(draft, Editor);
        var saved = await Repository.GetByIdAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(draft.Title, saved.Title);
        Assert.Equal(NewsSlug.Resolve(draft.Title, draft.Slug), saved.Slug);
        Assert.Equal(draft.Excerpt, saved.Excerpt);
        Assert.Equal(draft.Body, saved.Body);
        Assert.Equal(Editor, saved.EditorEmail);
        Assert.False(saved.IsPublished);
        Assert.True(await Repository.CreateDraftAsync(Draft("Second"), Editor) > id);
        Assert.True(await Repository.IsSlugInUseAsync(saved.Slug!));
        Assert.False(await Repository.IsSlugInUseAsync(saved.Slug!, id));
        Assert.False(await Repository.IsSlugInUseAsync("missing-slug"));
        await Repository.UpdateAsync(id, Draft("Changed") with { Excerpt = "New excerpt", Body = "New body" }, "second@test.local");
        saved = await Repository.GetByIdAsync(id);
        Assert.Equal("Changed", saved!.Title);
        Assert.Equal("changed", saved.Slug);
        Assert.Equal("New excerpt", saved.Excerpt);
        Assert.Equal("New body", saved.Body);
        Assert.Equal("second@test.local", saved.EditorEmail);
        Assert.False(saved.IsPublished);
    }

    [Fact]
    public async Task Admin_pages_include_drafts_order_by_date_then_id_and_clamp_inputs()
    {
        var old = await Repository.CreateDraftAsync(Draft("Old") with { PublishedAt = new(2008, 3, 4) }, Editor);
        var first = await Repository.CreateDraftAsync(Draft("First"), Editor);
        var last = await Repository.CreateDraftAsync(Draft("Last"), Editor);
        await Repository.PublishAsync(first, Editor);
        await Repository.UpdateAsync(first, Draft("First"), Editor);
        Assert.Equal(new[] { last, first, old }, (await Repository.GetAllAsync()).Select(x => x.Id));
        var page = await Repository.GetPageAsync(0, 0);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(last, Assert.Single(page.Items).Id);
        Assert.Equal(first, Assert.Single((await Repository.GetPageAsync(2, 1)).Items).Id);
        Assert.Equal(last, Assert.Single((await Repository.GetPageAsync(-1, -1)).Items).Id);
    }

    [Fact]
    public async Task Publication_and_deletion_change_public_visibility()
    {
        var id = await Repository.CreateDraftAsync(Draft(), Editor);
        Assert.Null(await Public.GetByIdAsync(id));
        await Repository.PublishAsync(id, Editor);
        Assert.NotNull(await Public.GetByIdAsync(id));
        await Repository.UnpublishAsync(id, Editor);
        Assert.Null(await Public.GetByIdAsync(id));
        Assert.False((await Repository.GetByIdAsync(id))!.IsPublished);
        await Repository.PublishAsync(id, Editor);
        await Repository.DeleteAsync(id, Editor);
        Assert.Null(await Repository.GetByIdAsync(id));
        Assert.Null(await Public.GetByIdAsync(id));
    }

    [Fact]
    public async Task Forum_topic_can_only_be_linked_once()
    {
        var id = await Repository.CreateDraftAsync(Draft(), Editor);
        Assert.True(await Repository.TrySetForumTopicIdAsync(id, 123));
        Assert.False(await Repository.TrySetForumTopicIdAsync(id, 456));
        Assert.Equal(123, (await Repository.GetByIdAsync(id))!.ForumTopicId);
        Assert.False(await Repository.TrySetForumTopicIdAsync(-1, 789));
    }

    [Fact]
    public async Task Missing_mutations_report_not_found_even_with_a_token()
    {
        var token = new DateTime(2026, 9, 1);
        Func<Task>[] operations = [
            () => Repository.UpdateAsync(-1, Draft(), Editor, token),
            () => Repository.PublishAsync(-1, Editor, token),
            () => Repository.UnpublishAsync(-1, Editor, token),
            () => Repository.DeleteAsync(-1, Editor, token)];
        foreach (var operation in operations)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(operation);
            Assert.Equal("News article -1 was not found.", error.Message);
        }
    }

    [Fact]
    public async Task Stale_mutations_preserve_first_writer_and_fresh_tokens_succeed()
    {
        var id = await Repository.CreateDraftAsync(Draft(), Editor);
        var original = (await Repository.GetByIdAsync(id))!.UpdatedAt;
        Assert.NotNull(original);
        await Repository.UpdateAsync(id, Draft("First writer"), Editor, original);
        var first = (await Repository.GetByIdAsync(id))!;
        Assert.NotEqual(original, first.UpdatedAt);
        Func<Task>[] staleOperations = [
            () => Repository.UpdateAsync(id, Draft("Stale writer"), Editor, original),
            () => Repository.PublishAsync(id, Editor, original),
            () => Repository.UnpublishAsync(id, Editor, original),
            () => Repository.DeleteAsync(id, Editor, original)];
        foreach (var operation in staleOperations)
        {
            await Assert.ThrowsAsync<OptimisticConcurrencyException>(operation);
            Assert.Equal(first, await Repository.GetByIdAsync(id));
            Assert.Null(await Public.GetByIdAsync(id));
        }
        await Repository.UpdateAsync(id, Draft("Fresh writer"), Editor, first.UpdatedAt);
        Assert.Equal("Fresh writer", (await Repository.GetByIdAsync(id))!.Title);
        await Repository.PublishAsync(id, Editor, (await Repository.GetByIdAsync(id))!.UpdatedAt);
        Assert.NotNull(await Public.GetByIdAsync(id));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.UnpublishAsync(id, Editor, original));
        Assert.True((await Repository.GetByIdAsync(id))!.IsPublished);
        await Repository.UnpublishAsync(id, Editor, (await Repository.GetByIdAsync(id))!.UpdatedAt);
        Assert.Null(await Public.GetByIdAsync(id));
        await Repository.DeleteAsync(id, Editor, (await Repository.GetByIdAsync(id))!.UpdatedAt);
        Assert.Null(await Repository.GetByIdAsync(id));
    }
}

public sealed class InMemoryAdminNewsRepositoryContractTests : AdminNewsRepositoryContractTests
{
    private readonly SharedNewsStore store = new();
    protected override IAdminNewsRepository Repository => new InMemoryAdminNewsRepository(store);
    protected override INewsRepository Public => new InMemoryNewsRepository(store);
}

public sealed class EfAdminNewsRepositoryContractTests : AdminNewsRepositoryContractTests, IAsyncDisposable
{
    private readonly NewsEditorialContractFixture fixture = new();
    protected override IAdminNewsRepository Repository { get; }
    protected override INewsRepository Public { get; }
    public EfAdminNewsRepositoryContractTests() => (Repository, Public) = fixture.News();
    public ValueTask DisposeAsync() => fixture.DisposeAsync();
}

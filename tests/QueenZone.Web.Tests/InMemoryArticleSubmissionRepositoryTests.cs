using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class InMemoryArticleSubmissionRepositoryTests
{
    private readonly Guid memberId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();

    [Fact]
    public async Task UpsertDraftAsync_CreatesDraftWithGeneratedSlug()
    {
        var repository = CreateRepository();

        var saved = await repository.UpsertDraftAsync(Draft(null, "My Article", "Body text."));

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(ArticleSubmissionStatus.Draft, saved.Status);
        Assert.Equal("my-article", saved.Slug);
    }

    [Fact]
    public async Task UpsertDraftAsync_UpdatesExistingDraft()
    {
        var repository = CreateRepository();
        var created = await repository.UpsertDraftAsync(Draft(null, "Original", "Body."));

        var updated = await repository.UpsertDraftAsync(Draft(created.Id, "Updated title", "New body."));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated title", updated.Title);
        Assert.Equal("updated-title", updated.Slug);
    }

    [Fact]
    public async Task UpsertDraftAsync_ThrowsWhenDraftMissingOrWrongMember()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpsertDraftAsync(Draft(Guid.NewGuid(), "Title", "Body.")));

        var created = await repository.UpsertDraftAsync(Draft(null, "Mine", "Body."));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpsertDraftAsync(
                new ArticleSubmissionDraft(created.Id, otherMemberId, "Stolen", null, "Body.", null, null)));
    }

    [Fact]
    public async Task UpsertDraftAsync_ThrowsWhenStatusIsNotEditable()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Submitted article", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpsertDraftAsync(Draft(draft.Id, "Changed", body)));
    }

    [Fact]
    public async Task UpsertDraftAsync_AllowsUpdateAfterRevisionRequested()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Needs work", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);
        await repository.UpdateStatusAsync(
            draft.Id,
            ArticleSubmissionStatus.RequiresRevision,
            "editor@test.local",
            "Fix intro",
            "Too short");

        var updated = await repository.UpsertDraftAsync(Draft(draft.Id, "Revised title", body + " More text."));

        Assert.Equal("Revised title", updated.Title);
        Assert.Equal(ArticleSubmissionStatus.RequiresRevision, updated.Status);
    }

    [Fact]
    public async Task SubmitForReviewAsync_ReturnsNull_WhenMissingOrWrongMember()
    {
        var repository = CreateRepository();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Article", MinBody()));

        Assert.Null(await repository.SubmitForReviewAsync(Guid.NewGuid(), memberId));
        Assert.Null(await repository.SubmitForReviewAsync(draft.Id, otherMemberId));
    }

    [Fact]
    public async Task SubmitForReviewAsync_ReturnsNull_WhenAlreadySubmitted()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Article", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        Assert.Null(await repository.SubmitForReviewAsync(draft.Id, memberId));
    }

    [Fact]
    public async Task SubmitForReviewAsync_ThrowsWhenBodyTooShort()
    {
        var repository = CreateRepository();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Short", "Too short."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SubmitForReviewAsync(draft.Id, memberId));
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsReviewableStatusesAndClampsPaging()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Draft only", body));
        var submitted = await repository.UpsertDraftAsync(Draft(null, "Submitted article", body));
        await repository.SubmitForReviewAsync(submitted.Id, memberId);

        var pending = await repository.GetPendingAsync(0, 1000);

        Assert.Single(pending);
        Assert.Equal("Submitted article", pending[0].Title);
        Assert.Equal("Test Author", pending[0].AuthorDisplayName);
        Assert.True(pending[0].WordCountEstimate > 0);
        Assert.DoesNotContain(pending, item => item.Id == draft.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var repository = CreateRepository();
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNull_WhenMissing()
    {
        var repository = CreateRepository();
        Assert.Null(await repository.UpdateStatusAsync(
            Guid.NewGuid(),
            ArticleSubmissionStatus.Rejected,
            "editor@test.local",
            null,
            "Not suitable"));
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesMetadataAndPublishedAt()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var draft = await repository.UpsertDraftAsync(Draft(null, "Publish me", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        var published = await repository.UpdateStatusAsync(
            draft.Id,
            ArticleSubmissionStatus.Published,
            "editor@test.local",
            "Looks good",
            null,
            new ArticlePublicationDetails("custom-slug", "Short summary", "Queen, Live"));

        Assert.NotNull(published);
        Assert.Equal(ArticleSubmissionStatus.Published, published!.Status);
        Assert.Equal("custom-slug", published.Slug);
        Assert.Equal("Short summary", published.Excerpt);
        Assert.Equal("Queen, Live", published.Tags);
        Assert.NotNull(published.PublishedAt);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsOnlyPublishedArticles()
    {
        var repository = CreateRepository();
        var body = MinBody();
        var publishedDraft = await repository.UpsertDraftAsync(Draft(null, "Published article", body));
        await repository.SubmitForReviewAsync(publishedDraft.Id, memberId);
        await repository.UpdateStatusAsync(
            publishedDraft.Id,
            ArticleSubmissionStatus.Published,
            "editor@test.local",
            null,
            null);

        await repository.UpsertDraftAsync(Draft(null, "Still draft", body));

        var published = await repository.GetPublishedAsync();

        Assert.Single(published);
        Assert.Equal("Published article", published[0].Title);
        Assert.Equal("Test Author", published[0].AuthorDisplayName);
    }

    [Fact]
    public async Task GetDraftsForMemberAsync_ReturnsMemberRowsOrderedBySubmittedAt()
    {
        var repository = CreateRepository();
        var body = MinBody();
        await repository.UpsertDraftAsync(Draft(null, "Draft one", "Short."));
        var submitted = await repository.UpsertDraftAsync(Draft(null, "Submitted one", body));
        await repository.SubmitForReviewAsync(submitted.Id, memberId);

        var rows = await repository.GetDraftsForMemberAsync(memberId);

        Assert.Equal(2, rows.TotalCount);
        Assert.Equal("Submitted one", rows.Items[0].Title);
    }

    [Fact]
    public async Task UpsertDraftAsync_TrimsLongOptionalFields()
    {
        var repository = CreateRepository();
        var longTitle = new string('T', 400);
        var longTags = new string('g', 600);

        var saved = await repository.UpsertDraftAsync(
            Draft(null, longTitle, "Body.", tags: longTags));

        Assert.Equal(300, saved.Title.Length);
        Assert.Equal(500, saved.Tags!.Length);
    }

    private InMemoryArticleSubmissionRepository CreateRepository()
    {
        var members = new Dictionary<Guid, MemberAccount>
        {
            [memberId] = new()
            {
                Id = memberId,
                Email = "author@example.com",
                NormalizedEmail = "AUTHOR@EXAMPLE.COM",
                DisplayName = "Test Author",
                CreatedAt = DateTime.UtcNow,
            },
        };

        return new InMemoryArticleSubmissionRepository(id =>
            members.TryGetValue(id, out var member) ? member : null);
    }

    private ArticleSubmissionDraft Draft(Guid? id, string title, string body, string? tags = null) =>
        new(id, memberId, title, "Excerpt", body, null, tags);

    private static string MinBody() => new('x', EfArticleSubmissionRepository.MinBodyVisibleChars);
}

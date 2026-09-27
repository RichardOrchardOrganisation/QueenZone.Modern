using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfArticleSubmissionRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfArticleSubmissionRepository repository;
    private readonly Guid memberId = Guid.NewGuid();

    public EfArticleSubmissionRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "article-author@example.com",
            NormalizedEmail = "ARTICLE-AUTHOR@EXAMPLE.COM",
            DisplayName = "Test Author",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();

        repository = new EfArticleSubmissionRepository(dbContext);
    }

    [Fact]
    public async Task UpsertDraftAsync_CreatesDraftWithSlug()
    {
        var saved = await repository.UpsertDraftAsync(Draft(null, "My First Article", "Hello world body here."));

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal("My First Article", saved.Title);
        Assert.Equal(ArticleSubmissionStatus.Draft, saved.Status);
        Assert.Equal("my-first-article", saved.Slug);
        var row = await dbContext.ArticleSubmissions.AsNoTracking().SingleAsync(a => a.Id == saved.Id);
        Assert.Equal(EfArticleSubmissionRepository.EstimateWordCount(saved.Body), row.WordCount);
    }

    [Fact]
    public async Task UpsertDraftAsync_UpdatesExistingDraft()
    {
        var created = await repository.UpsertDraftAsync(Draft(null, "Original Title", "Body here."));

        var updated = await repository.UpsertDraftAsync(Draft(created.Id, "Updated Title", "Updated body."));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("updated-title", updated.Slug);
        var row = await dbContext.ArticleSubmissions.AsNoTracking().SingleAsync(a => a.Id == updated.Id);
        Assert.Equal(EfArticleSubmissionRepository.EstimateWordCount(updated.Body), row.WordCount);
    }

    [Fact]
    public async Task SubmitForReviewAsync_RejectsBodyUnderMinimumLength()
    {
        var draft = await repository.UpsertDraftAsync(Draft(null, "Short", "Too short."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SubmitForReviewAsync(draft.Id, memberId));
    }

    [Fact]
    public async Task SubmitForReviewAsync_TransitionsDraftToSubmitted()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Long article", body));

        var submitted = await repository.SubmitForReviewAsync(draft.Id, memberId);

        Assert.NotNull(submitted);
        Assert.Equal(ArticleSubmissionStatus.Submitted, submitted!.Status);
        Assert.NotNull(submitted.SubmittedAt);
    }

    [Fact]
    public async Task SubmitForReviewAsync_ReturnsNull_WhenIdNotFound()
    {
        var result = await repository.SubmitForReviewAsync(Guid.NewGuid(), memberId);
        Assert.Null(result);
    }

    [Fact]
    public async Task SubmitForReviewAsync_ReturnsNull_WhenWrongMember()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Article", body));

        var result = await repository.SubmitForReviewAsync(draft.Id, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyReviewableStatuses()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);

        var draft = await repository.UpsertDraftAsync(Draft(null, "Draft article", body));
        var submitted = await repository.UpsertDraftAsync(Draft(null, "Submitted article", body));
        await repository.SubmitForReviewAsync(submitted.Id, memberId);

        var pending = await repository.GetPendingAsync(1, 50);

        Assert.Single(pending);
        Assert.Equal("Submitted article", pending[0].Title);
        Assert.DoesNotContain(pending, p => p.Id == draft.Id);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsPublishedAt_WhenPublished()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Publish me", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        var published = await repository.UpdateStatusAsync(
            draft.Id,
            ArticleSubmissionStatus.Published,
            "editor@test.local",
            "Looks good",
            null);

        Assert.NotNull(published);
        Assert.Equal(ArticleSubmissionStatus.Published, published!.Status);
        Assert.NotNull(published.PublishedAt);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsOnlyPublishedArticles()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var article = await repository.UpsertDraftAsync(Draft(null, "Published article", body));
        await repository.SubmitForReviewAsync(article.Id, memberId);
        await repository.UpdateStatusAsync(article.Id, ArticleSubmissionStatus.Published, "ed@test.local", null, null);

        var draft = await repository.UpsertDraftAsync(Draft(null, "Unpublished draft", body));

        var published = await repository.GetPublishedAsync();

        Assert.Single(published);
        Assert.Equal("Published article", published[0].Title);
        Assert.DoesNotContain(published, p => p.Id == draft.Id);
    }

    [Fact]
    public async Task GetDraftsForMemberAsync_ReturnsAllStatusesForMember()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        await repository.UpsertDraftAsync(Draft(null, "My Draft", "Short draft."));
        var submitted = await repository.UpsertDraftAsync(Draft(null, "My Submitted", body));
        await repository.SubmitForReviewAsync(submitted.Id, memberId);

        var all = await repository.GetDraftsForMemberAsync(memberId);

        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task GetPendingAsync_PagesNewestFirstWithoutEarlierRows()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var first = await repository.UpsertDraftAsync(Draft(null, "First submitted", body));
        await repository.SubmitForReviewAsync(first.Id, memberId);
        var second = await repository.UpsertDraftAsync(Draft(null, "Second submitted", body));
        await repository.SubmitForReviewAsync(second.Id, memberId);
        var third = await repository.UpsertDraftAsync(Draft(null, "Third submitted", body));
        await repository.SubmitForReviewAsync(third.Id, memberId);

        var page1 = await repository.GetPendingAsync(1, 2);
        var page2 = await repository.GetPendingAsync(2, 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal("Third submitted", page1[0].Title);
        Assert.Single(page2);
        Assert.Equal("First submitted", page2[0].Title);
        Assert.DoesNotContain(page2, item => item.Id == page1[0].Id || item.Id == page1[1].Id);
        Assert.All(page1.Concat(page2), item => Assert.True(item.WordCountEstimate >= 0));
    }

    [Fact]
    public async Task GetDraftsForMemberAsync_PagesWithoutEarlierRows()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var first = await repository.UpsertDraftAsync(Draft(null, "Draft one", body));
        await repository.SubmitForReviewAsync(first.Id, memberId);
        var second = await repository.UpsertDraftAsync(Draft(null, "Draft two", body));
        await repository.SubmitForReviewAsync(second.Id, memberId);
        var third = await repository.UpsertDraftAsync(Draft(null, "Draft three", body));
        await repository.SubmitForReviewAsync(third.Id, memberId);

        var page1 = await repository.GetDraftsForMemberAsync(memberId, page: 1, pageSize: 2);
        var page2 = await repository.GetDraftsForMemberAsync(memberId, page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.DoesNotContain(page2.Items, item => page1.Items.Any(firstPage => firstPage.Id == item.Id));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSubmissionWithAuthor()
    {
        var saved = await repository.UpsertDraftAsync(Draft(null, "Lookup article", "Body text."));

        var loaded = await repository.GetByIdAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Test Author", loaded!.AuthorDisplayName);
        Assert.Equal("article-author@example.com", loaded.AuthorEmail);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpsertDraftAsync_ThrowsWhenStatusIsNotEditable()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Locked article", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpsertDraftAsync(Draft(draft.Id, "Changed", body)));
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesSlugExcerptTagsAndRejectionReason()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Metadata article", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);

        var updated = await repository.UpdateStatusAsync(
            draft.Id,
            ArticleSubmissionStatus.Rejected,
            "editor@test.local",
            "Not suitable",
            "Off topic",
            slug: "custom-slug",
            excerpt: "Short summary",
            tags: "Queen");

        Assert.NotNull(updated);
        Assert.Equal(ArticleSubmissionStatus.Rejected, updated!.Status);
        Assert.Equal("custom-slug", updated.Slug);
        Assert.Equal("Short summary", updated.Excerpt);
        Assert.Equal("Queen", updated.Tags);
        Assert.Equal("Off topic", updated.RejectionReason);
    }

    [Fact]
    public async Task SubmitForReviewAsync_AllowsResubmitAfterRevisionRequested()
    {
        var body = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars);
        var draft = await repository.UpsertDraftAsync(Draft(null, "Revise me", body));
        await repository.SubmitForReviewAsync(draft.Id, memberId);
        await repository.UpdateStatusAsync(
            draft.Id,
            ArticleSubmissionStatus.RequiresRevision,
            "editor@test.local",
            "Fix intro",
            "Too short");

        var revised = await repository.UpsertDraftAsync(Draft(draft.Id, "Revised article", body + " extra"));
        var resubmitted = await repository.SubmitForReviewAsync(revised.Id, memberId);

        Assert.NotNull(resubmitted);
        Assert.Equal(ArticleSubmissionStatus.Submitted, resubmitted!.Status);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private ArticleSubmissionDraft Draft(Guid? id, string title, string body) =>
        new(id, memberId, title, null, body, null, null);
}

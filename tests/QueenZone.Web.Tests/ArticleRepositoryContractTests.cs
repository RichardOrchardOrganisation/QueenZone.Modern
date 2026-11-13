using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class ArticleRepositoryContractTests
{
    protected abstract IArticleRepository Repository { get; }
    protected abstract IEditorialArticleRepository Editorial { get; }
    protected abstract Task<Guid> SeedAsync(string title, DateTimeOffset date, string tags, bool published = true);
    private static readonly DateTimeOffset MiddleDate = new(2020, 6, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Published_feed_pages_filters_tags_and_omits_drafts_and_unknown_ids()
    {
        var old = await SeedAsync("Old article", MiddleDate.AddDays(-1), "queen,freddie");
        var middle = await SeedAsync("Middle article", MiddleDate, "brian,queen");
        var newest = await SeedAsync("Newest article", MiddleDate.AddDays(1), "brian");
        var hidden = await SeedAsync("Draft article", MiddleDate.AddDays(2), "queen", false);
        Assert.Equal(3, await Repository.GetCountAsync());
        Assert.Equal(new[] { newest, middle, old }, (await Repository.GetPageAsync(1, 10)).Select(x => x.Id));
        Assert.Equal(middle, Assert.Single(await Repository.GetPageAsync(2, 1)).Id);
        Assert.Equal(newest, Assert.Single(await Repository.GetPageAsync(0, 0)).Id);
        Assert.Equal(2, await Repository.GetCountAsync("QUEEN"));
        Assert.Equal(new[] { middle, old }, (await Repository.GetPageAsync(1, 10, "QUEEN")).Select(x => x.Id));
        Assert.Equal(0, await Repository.GetCountAsync("quee"));
        Assert.Empty(await Repository.GetPageAsync(1, 10, "quee"));
        Assert.Equal(new[] { old, middle, newest }.Order(), (await Repository.GetPublishedFeedKeysAsync()).Select(x => x.CommunityId).Order());
        Assert.Equal(new[] { old, middle }.Order(), (await Repository.GetPublishedFeedKeysAsync("QUEEN")).Select(x => x.CommunityId).Order());
        Assert.Equal(new[] { old, middle, newest }.Order(), (await Repository.GetSitemapEntriesAsync()).Select(x => x.Id).Order());
        Assert.Equal(new[] { old, middle }.Order(), (await Repository.GetPublishedByIdsAsync([old, old, middle, hidden, Guid.Empty])).Select(x => x.Id).Order());
        Assert.Empty(await Repository.GetPublishedByIdsAsync([]));
        Assert.Null(await Repository.GetBySlugAsync("draft-article"));
        Assert.Null(await Repository.GetBySlugAsync("missing"));
        Assert.Equal(middle, (await Repository.GetBySlugAsync("middle-article"))!.Id);
    }

    [Fact]
    public async Task Adjacent_articles_are_the_nearest_older_and_newer_published_items()
    {
        var old = await SeedAsync("Old", MiddleDate.AddDays(-1), "queen");
        await SeedAsync("Middle", MiddleDate, "queen");
        var newest = await SeedAsync("Newest", MiddleDate.AddDays(1), "queen");
        await SeedAsync("Hidden", MiddleDate.AddHours(1), "queen", false);
        var adjacent = await Repository.GetAdjacentAsync(MiddleDate);
        Assert.Equal(old, adjacent.Previous!.Id);
        Assert.Equal(newest, adjacent.Next!.Id);
        Assert.Null((await Repository.GetAdjacentAsync(MiddleDate.AddDays(-1))).Previous);
        Assert.Null((await Repository.GetAdjacentAsync(MiddleDate.AddDays(1))).Next);
    }

    [Fact]
    public async Task Standalone_editorial_appears_in_feed_and_promotion_replaces_source_submission()
    {
        var source = await SeedAsync("Source article", MiddleDate, "queen");
        var other = await SeedAsync("Other article", MiddleDate.AddDays(-1), "brian");
        var draft = await Editorial.SaveDraftAsync(EditorialArticleRepositoryContractTests.Draft("Standalone feature"), "editor");
        Assert.Equal(2, await Repository.GetCountAsync());
        await Editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "editor");
        Assert.Equal(draft.Id, (await Repository.GetBySlugAsync("standalone-feature"))!.Id);
        Assert.Equal(3, await Repository.GetCountAsync());
        Assert.Equal(draft.Id, (await Repository.GetPageAsync(1, 10))[0].Id);
        var promoted = await Editorial.SaveDraftAsync(EditorialArticleRepositoryContractTests.Draft("Promoted feature") with { SourceSubmissionId = source }, "editor");
        await Editorial.SetStatusAsync(promoted.Id, EditorialArticleStatus.Published, "editor");
        var expected = new[] { draft.Id, promoted.Id, other }.Order();
        Assert.Equal(3, await Repository.GetCountAsync());
        Assert.Equal(expected, (await Repository.GetPageAsync(1, 10)).Select(x => x.Id).Order());
        Assert.Equal(expected, (await Repository.GetPublishedFeedKeysAsync()).Select(x => x.CommunityId).Order());
        Assert.Equal(expected, (await Repository.GetSitemapEntriesAsync()).Select(x => x.Id).Order());
        Assert.Empty(await Repository.GetPublishedByIdsAsync([source]));
        Assert.Equal(promoted.Id, (await Repository.GetBySlugAsync("promoted-feature"))!.Id);
    }
}

public sealed class InMemoryArticleRepositoryContractTests : ArticleRepositoryContractTests
{
    private readonly FixedPublicationSubmissionSeed submissions = new();
    protected override IEditorialArticleRepository Editorial { get; } = new InMemoryEditorialArticleRepository(new NewsEditorialContractClock());
    protected override IArticleRepository Repository => new InMemoryArticleRepository(submissions, Editorial);
    protected override Task<Guid> SeedAsync(string title, DateTimeOffset date, string tags, bool published = true) =>
        submissions.SeedAsync(title, date, tags, published);
}

public sealed class EfArticleRepositoryContractTests : ArticleRepositoryContractTests, IAsyncDisposable
{
    private readonly NewsEditorialContractFixture fixture = new();
    private readonly Guid authorId = Guid.NewGuid();
    protected override IEditorialArticleRepository Editorial => fixture.Editorial;
    protected override IArticleRepository Repository => new EfArticleRepository(fixture.Context, Editorial);

    public EfArticleRepositoryContractTests()
    {
        fixture.Context.MemberAccounts.Add(new MemberAccount
        {
            Id = authorId,
            Email = "author@test.local",
            NormalizedEmail = "AUTHOR@TEST.LOCAL",
            DisplayName = "Author",
            CreatedAt = NewsEditorialContractClock.Now.UtcDateTime
        });
        fixture.Context.SaveChanges();
    }

    protected override async Task<Guid> SeedAsync(string title, DateTimeOffset date, string tags, bool published = true)
    {
        var id = Guid.NewGuid();
        fixture.Context.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = id,
            AuthorMemberId = authorId,
            Title = title,
            Slug = NewsSlug.Slugify(title),
            Excerpt = "Excerpt",
            Body = "Full community body",
            WordCount = 3,
            Tags = tags,
            Status = published ? ArticleSubmissionStatus.Published : ArticleSubmissionStatus.Draft,
            PublishedAt = published ? date : null
        });
        await fixture.Context.SaveChangesAsync();
        return id;
    }

    public ValueTask DisposeAsync() => fixture.DisposeAsync();
}

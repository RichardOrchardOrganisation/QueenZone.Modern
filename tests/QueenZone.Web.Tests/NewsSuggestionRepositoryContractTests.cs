using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class NewsSuggestionRepositoryContractTests
{
    protected abstract INewsSuggestionRepository Repository { get; }
    protected static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static MemberAccount Member(Guid id) => new() { Id = id, DisplayName = id == MemberId ? "Fan" : "Other", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST", CreatedAt = new DateTime(2026, 9, 1) };
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private Task<NewsSuggestion> Create(int day = 0, Guid? member = null) => Repository.CreateAsync(new(
        Guid.NewGuid(), member ?? MemberId, "https://example.test/story", Guid.NewGuid().ToString("N"), "Title", "Notes",
        NewsSuggestionStatus.Pending, Now.AddDays(day), null, null, null, null, null, null, null));

    [Fact]
    public async Task Create_Queues_Duplicates_AndMemberCounts()
    {
        var first = await Create(-1);
        var second = await Create();
        await Create(-2, OtherMemberId);
        Assert.Equal(NewsSuggestionStatus.Pending, second.Status);
        Assert.Equal("Fan", (await Repository.GetByIdAsync(second.Id))!.SubmitterDisplayName);
        Assert.True(await Repository.HasActiveDuplicateAsync(second.UrlHash));
        Assert.False(await Repository.HasActiveDuplicateAsync("unknown"));
        Assert.Equal(1, await Repository.CountBySubmitterSinceAsync(MemberId, Now));
        Assert.Equal(second.Id, Assert.Single(await Repository.GetPendingAsync(0, 0)).Id);
        Assert.Equal(first.Id, Assert.Single(await Repository.GetPendingAsync(2, 1)).Id);
        Assert.Equal(new[] { second.Id, first.Id }, (await Repository.GetBySubmitterAsync(MemberId)).Items.Select(row => row.Id));
        var page = await Repository.GetBySubmitterAsync(MemberId, 2, 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(new SubmissionTypeCounts(3, 1, 3, 0, 0, 3), await Repository.GetDashboardCountsAsync(Now));
        var contributor = Assert.Single(await Repository.GetTopContributorsThisMonthAsync(Now.AddDays(-10), 1));
        Assert.Equal(MemberId, contributor.MemberId);
        Assert.Equal(2, contributor.Count);
    }

    [Fact]
    public async Task Review_Promotion_Duplicate_AndAttributionPersist()
    {
        var row = await Create();
        await Repository.UpdateStatusAsync(row.Id, NewsSuggestionStatus.UnderReview, "editor", "checking");
        Assert.Equal(NewsSuggestionStatus.UnderReview, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.UpdateStatusAsync(row.Id, "invalid", "editor", null));
        Assert.Equal(NewsSuggestionStatus.UnderReview, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Repository.PromoteAsync(row.Id, 42, "editor", "approved");
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(NewsSuggestionStatus.Promoted, row.Status);
        Assert.Equal(42, row.PromotedNewsId);
        Assert.False(await Repository.HasActiveDuplicateAsync(row.UrlHash));
        Assert.Equal(new NewsSubmissionAttribution(42, MemberId, "Fan"), Assert.Single(await Repository.GetPromotedAttributionsAsync([42, 999])));
        Assert.Empty(await Repository.GetPromotedAttributionsAsync([]));
        Assert.Empty(await Repository.GetPromotedAttributionsAsync([999]));
        var duplicate = await Create();
        await Repository.MarkDuplicateAsync(duplicate.Id, 42, "editor", "same story");
        duplicate = (await Repository.GetByIdAsync(duplicate.Id))!;
        Assert.Equal(NewsSuggestionStatus.Duplicate, duplicate.Status);
        Assert.Equal(42, duplicate.DuplicateCandidateId);
        Assert.Empty(await Repository.GetPendingAsync(1, 10));
        Assert.Null(await Repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Null(await Repository.UpdateStatusAsync(Guid.NewGuid(), NewsSuggestionStatus.Rejected, "editor", null));
        Assert.Null(await Repository.PromoteAsync(Guid.NewGuid(), 42, "editor", null));
        Assert.Null(await Repository.MarkDuplicateAsync(Guid.NewGuid(), 42, "editor", null));
    }
    [Fact]
    public async Task Queues_CapPageSizeAtOneHundred()
    {
        for (var index = 0; index < 101; index++)
        {
            await Create();
        }
        Assert.Equal(100, (await Repository.GetPendingAsync(1, 999)).Count);
        Assert.Single(await Repository.GetPendingAsync(2, 999));
        var page = await Repository.GetBySubmitterAsync(MemberId, 1, 999);
        Assert.Equal(101, page.TotalCount);
        Assert.Equal(100, page.Items.Count);
        Assert.Single((await Repository.GetBySubmitterAsync(MemberId, 2, 999)).Items);
    }

    [Fact]
    public async Task EqualSubmissionDates_OrderById()
    {
        var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        foreach (var id in new[] { high, low })
        {
            await Repository.CreateAsync(new(id, MemberId, "https://example.test/story", id.ToString("N"), "Title", null,
                NewsSuggestionStatus.Pending, Now, null, null, null, null, null, null, null));
        }
        Assert.Equal(new[] { low, high }, (await Repository.GetPendingAsync(1, 10)).Select(row => row.Id));
        Assert.Equal(new[] { low, high }, (await Repository.GetBySubmitterAsync(MemberId)).Items.Select(row => row.Id));
    }

}

public sealed class InMemoryNewsSuggestionRepositoryContractTests : NewsSuggestionRepositoryContractTests
{
    protected override INewsSuggestionRepository Repository { get; } = new InMemoryNewsSuggestionRepository(id => Member(id));
}

public sealed class EfNewsSuggestionRepositoryContractTests : NewsSuggestionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override INewsSuggestionRepository Repository { get; }

    public EfNewsSuggestionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.MemberAccounts.AddRange(Member(MemberId), Member(OtherMemberId));
        context.SaveChanges();
        context.NewsDiscoverySources.Add(new NewsDiscoverySourceEntity
        {
            Id = 1,
            Key = "contract",
            DisplayName = "Contract source",
            HomepageUrl = "https://example.test/",
            SourceType = NewsDiscoverySourceType.Rss,
            TrustTier = NewsDiscoveryTrustTier.Primary,
            PollIntervalMinutes = 60,
            Enabled = true,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        });
        context.SaveChanges();
        context.NewsCandidates.Add(new NewsCandidateEntity
        {
            Id = 42,
            SourceId = 1,
            SourceUrl = "https://example.test/story",
            CanonicalUrl = "https://example.test/story",
            CanonicalUrlHash = "candidate-hash",
            SourceTitle = "Candidate",
            DiscoveredAt = Now.UtcDateTime,
            Status = NewsCandidateStatus.Discovered,
        });
        context.SaveChanges();
        Repository = new EfNewsSuggestionRepository(context);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class TriviaFactSubmissionRepositoryContractTests
{
    protected abstract ITriviaFactSubmissionRepository Repository { get; }
    protected static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static MemberAccount Member(Guid id) => new() { Id = id, DisplayName = id == MemberId ? "Fan" : "Other", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST", CreatedAt = new DateTime(2026, 9, 1) };
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private Task<TriviaFactSubmission> Create(Guid? member = null, Guid? id = null) =>
        Repository.CreateAsync(new(member ?? MemberId, " Fact ", "Albums", "Easy", "Book", id));

    [Fact]
    public async Task Create_RoundTrips_PreferredAndGeneratedIds()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, (await Create(id: id)).Id);
        var created = await Create();
        Assert.NotEqual(Guid.Empty, created.Id);
        var row = await Repository.GetByIdAsync(created.Id);
        Assert.Equal("Fact", row!.Text);
        Assert.Equal("Albums", row.Category);
        Assert.Equal("easy", row.Difficulty);
        Assert.Equal("Book", row.SourceNote);
        Assert.Equal("Fan", row.SubmitterDisplayName);
        Assert.Equal(TriviaFactSubmissionStatus.Pending, row.Status);
        Assert.NotEqual(default, row.SubmittedAt);
        Assert.Null(await Repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Queues_FilterOrderAndClamp()
    {
        var first = await Create();
        var second = await Create();
        var other = await Create(OtherMemberId);
        await Repository.ApproveAsync(other.Id, 12, "editor", null);
        var rejected = await Create();
        await Repository.RejectAsync(rejected.Id, "editor", "Incorrect", null);
        Assert.Equal(new[] { second.Id, first.Id }, (await Repository.GetPendingAsync(1, 10)).Select(row => row.Id));
        Assert.Equal(second.Id, Assert.Single(await Repository.GetPendingAsync(0, 0)).Id);
        Assert.Equal(first.Id, Assert.Single(await Repository.GetPendingAsync(2, 1)).Id);
        var owned = await Repository.GetBySubmitterAsync(MemberId, 2, 1);
        Assert.Equal(3, owned.TotalCount);
        Assert.Equal(second.Id, Assert.Single(owned.Items).Id);
        Assert.Empty((await Repository.GetBySubmitterAsync(Guid.NewGuid())).Items);
        Assert.Equal(2, (await Repository.GetDashboardCountsAsync(Now)).Pending);
    }

    [Fact]
    public async Task Reviews_ValidateBeforeMutation_AndAreFinal()
    {
        var row = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(row.Id, "editor", " ", null));
        Assert.Equal(TriviaFactSubmissionStatus.Pending, (await Repository.GetByIdAsync(row.Id))!.Status);
        var approved = await Repository.ApproveAsync(row.Id, 42, "editor", "checked");
        Assert.Equal(42, approved!.PromotedTriviaId);
        Assert.Equal(TriviaFactSubmissionStatus.Approved, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ApproveAsync(row.Id, 43, "editor", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(row.Id, "editor", "reason", null));
        var reject = await Create();
        await Repository.RejectAsync(reject.Id, "editor", "Incorrect", "notes");
        Assert.Equal("Incorrect", (await Repository.GetByIdAsync(reject.Id))!.RejectionReason);
        Assert.Null(await Repository.ApproveAsync(Guid.NewGuid(), 42, "editor", null));
        Assert.Null(await Repository.RejectAsync(Guid.NewGuid(), "editor", "reason", null));
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

}

public sealed class InMemoryTriviaFactSubmissionRepositoryContractTests : TriviaFactSubmissionRepositoryContractTests
{
    protected override ITriviaFactSubmissionRepository Repository { get; } = new InMemoryTriviaFactSubmissionRepository(id => Member(id));
}

public sealed class EfTriviaFactSubmissionRepositoryContractTests : TriviaFactSubmissionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override ITriviaFactSubmissionRepository Repository { get; }

    public EfTriviaFactSubmissionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.MemberAccounts.AddRange(Member(MemberId), Member(OtherMemberId));
        context.SaveChanges();
        Repository = new EfTriviaFactSubmissionRepository(context);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}

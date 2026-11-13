using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class ForumPostMemberIdCountContractTests
{
    protected abstract IForumWriteRepository Repository { get; }

    protected abstract Task SeedPostAsync(Guid memberId, Guid otherMemberId, DateTimeOffset postedAt);

    [Fact]
    public async Task CountMethods_CountRenamedMembersPostOnlyForItsMemberId()
    {
        var memberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();
        var postedAt = new DateTimeOffset(2026, 7, 10, 4, 0, 0, TimeSpan.Zero);
        await SeedPostAsync(memberId, otherMemberId, postedAt);

        Assert.Equal(1, await Repository.CountApprovedPostsByMemberAsync(memberId));
        Assert.Equal(1, await Repository.CountPostsByMemberSinceAsync(memberId, postedAt.AddSeconds(-1)));
        Assert.Equal(0, await Repository.CountApprovedPostsByMemberAsync(otherMemberId));
        Assert.Equal(0, await Repository.CountPostsByMemberSinceAsync(otherMemberId, postedAt.AddSeconds(-1)));
    }
}

public sealed class InMemoryForumPostMemberIdCountContractTests : ForumPostMemberIdCountContractTests
{
    protected override IForumWriteRepository Repository { get; } = new InMemoryForumWriteRepository();

    protected override async Task SeedPostAsync(Guid memberId, Guid otherMemberId, DateTimeOffset postedAt)
    {
        await Repository.CreatePostAsync(new NewForumPost(
            1002, memberId, "Former Name", "<p>Post</p>", postedAt));
    }
}

public sealed class EfForumPostMemberIdCountContractTests : ForumPostMemberIdCountContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;

    public EfForumPostMemberIdCountContractTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumPost
            (
                AuthorMemberId TEXT NULL,
                AuthorDisplayName TEXT NOT NULL,
                PostedAt TEXT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0
            );
            """);
        Repository = new EfForumWriteRepository(dbContext);
    }

    protected override IForumWriteRepository Repository { get; }

    protected override async Task SeedPostAsync(Guid memberId, Guid otherMemberId, DateTimeOffset postedAt)
    {
        dbContext.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = memberId,
                Email = "renamed@example.com",
                NormalizedEmail = "RENAMED@EXAMPLE.COM",
                DisplayName = "Current Name",
                CreatedAt = postedAt.UtcDateTime,
            },
            new MemberAccount
            {
                Id = otherMemberId,
                Email = "other@example.com",
                NormalizedEmail = "OTHER@EXAMPLE.COM",
                DisplayName = "Former Name",
                CreatedAt = postedAt.UtcDateTime,
            });
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ModernForumPost (AuthorMemberId, AuthorDisplayName, PostedAt, IsHidden)
            VALUES ({memberId}, 'Former Name', {postedAt.UtcDateTime}, 0);
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfForumPollRepositoryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-11T15:00:00Z");
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfForumWriteRepository writeRepository;
    private readonly EfForumPollRepository pollRepository;

    public EfForumPollRepositoryTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        writeRepository = new EfForumWriteRepository(dbContext);
        pollRepository = new EfForumPollRepository(dbContext, new FixedClock(Now));
    }

    [Fact]
    public async Task CreateThread_WithPoll_WritesPollAndOptions()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();

        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "Poll thread",
            "<p>Body</p>",
            Now,
            new NewForumPoll("Q?", false, null, null, ["One", "Two"], member.Id)));

        var results = await pollRepository.GetPollWithResultsAsync(created.TopicId, member.Id);
        Assert.NotNull(results);
        Assert.Equal("Q?", results!.Question);
        Assert.Equal(2, results.Options.Count);
        Assert.True(results.CanViewerVote);
        Assert.True(results.CanViewerClose);
    }

    [Fact]
    public async Task CreatePollAsync_ThrowsWhenThreadMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pollRepository.CreatePollAsync(
                404,
                new NewForumPoll("Q", false, null, null, ["A", "B"], Guid.NewGuid())));
    }

    [Fact]
    public async Task CreatePollAsync_ThrowsWhenPollAlreadyExists()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], member.Id)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pollRepository.CreatePollAsync(
                created.TopicId,
                new NewForumPoll("Again", false, null, null, ["X", "Y"], member.Id)));
    }

    [Fact]
    public async Task CastVote_And_GetResults_WorkOnSqlite()
    {
        var member = await SeedMemberAsync();
        var voter = await SeedMemberAsync("voter@example.com", "Voter");
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], member.Id)));

        var before = await pollRepository.GetPollWithResultsAsync(created.TopicId, voter.Id);
        Assert.True(before!.CanViewerVote);
        Assert.Equal(0, before.Options[0].VoteCount);
        await pollRepository.CastVoteAsync(before.PollId, voter.Id, [before.Options[0].OptionId]);

        var after = await pollRepository.GetPollWithResultsAsync(created.TopicId, voter.Id);
        Assert.True(after!.ViewerHasVoted);
        Assert.Equal(1, after.TotalVotes);
        Assert.Equal(100, after.Options[0].Percentage);
    }

    [Fact]
    public async Task GetPollWithResultsAsync_WhenViewerCanVote_OmitsOptionTalliesButReportsDistinctVoters()
    {
        var author = await SeedMemberAsync();
        var firstVoter = await SeedMemberAsync("first@example.com", "First");
        var secondVoter = await SeedMemberAsync("second@example.com", "Second");
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, author.Id, author.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], author.Id)));
        var poll = await pollRepository.GetPollWithResultsAsync(created.TopicId, firstVoter.Id);
        await pollRepository.CastVoteAsync(poll!.PollId, firstVoter.Id, [poll.Options[0].OptionId]);

        var openBallot = await pollRepository.GetPollWithResultsAsync(created.TopicId, secondVoter.Id);

        Assert.NotNull(openBallot);
        Assert.True(openBallot!.CanViewerVote);
        Assert.Equal(1, openBallot.DistinctVoters);
        Assert.Equal(0, openBallot.TotalVotes);
        Assert.All(openBallot.Options, option => Assert.Equal(0, option.VoteCount));
    }

    [Fact]
    public async Task CastVote_RejectsSecondVote()
    {
        var member = await SeedMemberAsync();
        var voter = await SeedMemberAsync("v2@example.com", "V2");
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], member.Id)));
        var poll = await pollRepository.GetPollWithResultsAsync(created.TopicId, voter.Id);
        await pollRepository.CastVoteAsync(poll!.PollId, voter.Id, [poll.Options[0].OptionId]);

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            pollRepository.CastVoteAsync(poll.PollId, voter.Id, [poll.Options[1].OptionId]));
        Assert.Equal(ForumPollVoteException.AlreadyVoted, ex.Code);
    }

    [Fact]
    public async Task ClosePoll_ByAuthor_ClosesPoll()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], member.Id)));
        var poll = await pollRepository.GetPollWithResultsAsync(created.TopicId, member.Id);

        await pollRepository.ClosePollAsync(poll!.PollId, member.Id, isAdmin: false);
        var after = await pollRepository.GetPollWithResultsAsync(created.TopicId, member.Id);
        Assert.True(after!.IsClosed);
        Assert.False(after.CanViewerVote);
    }

    [Fact]
    public async Task ClosePoll_RejectsNonAuthorNonAdmin()
    {
        var member = await SeedMemberAsync();
        var other = await SeedMemberAsync("other@example.com", "Other");
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "T", "<p>B</p>", Now,
            new NewForumPoll("Q", false, null, null, ["A", "B"], member.Id)));
        var poll = await pollRepository.GetPollWithResultsAsync(created.TopicId, other.Id);

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            pollRepository.ClosePollAsync(poll!.PollId, other.Id, isAdmin: false));
        Assert.Equal(ForumPollVoteException.Forbidden, ex.Code);
    }

    [Fact]
    public async Task GetPollWithResultsAsync_ReturnsNullWhenMissing()
    {
        Assert.Null(await pollRepository.GetPollWithResultsAsync(999, null));
    }

    [Fact]
    public void ValidateNewPoll_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() =>
            EfForumPollRepository.ValidateNewPoll(new NewForumPoll("", false, null, null, ["A", "B"], Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() =>
            EfForumPollRepository.ValidateNewPoll(new NewForumPoll("Q", false, null, null, ["A"], Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() =>
            EfForumPollRepository.ValidateNewPoll(new NewForumPoll("Q", true, 0, null, ["A", "B"], Guid.NewGuid())));
    }

    [Fact]
    public void ForumPollForm_ToNewPoll_Validates()
    {
        var form = new ForumPollForm
        {
            Enabled = true,
            Question = "Q?",
            Options = ["A", "B"],
        };
        var errors = new List<string>();
        var poll = form.ToNewPoll(Guid.NewGuid(), errors);
        Assert.NotNull(poll);
        Assert.Empty(errors);

        form = new ForumPollForm { Enabled = true, Question = "", Options = ["A"] };
        errors = [];
        Assert.Null(form.ToNewPoll(Guid.NewGuid(), errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task AdminWithoutMemberAccount_CanClosePoll()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "Admin close",
            "<p>Body</p>",
            Now,
            new NewForumPoll("Q?", false, null, null, ["One", "Two"], member.Id)));

        var open = await pollRepository.GetPollWithResultsAsync(created.TopicId, null, viewerIsAdmin: true);
        Assert.NotNull(open);
        Assert.True(open!.CanViewerClose);

        await pollRepository.ClosePollAsync(open.PollId, Guid.Empty, isAdmin: true);
        var closed = await pollRepository.GetPollWithResultsAsync(created.TopicId, null, viewerIsAdmin: true);
        Assert.True(closed!.IsClosed);
        Assert.False(closed.CanViewerClose);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<MemberAccount> SeedMemberAsync(
        string email = "author@example.com",
        string displayName = "Author")
    {
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(member);
        await dbContext.SaveChangesAsync();
        return member;
    }

    private async Task SeedCategoryAsync()
    {
        dbContext.ModernForumCategories.Add(new ModernForumCategoryEntity
        {
            LegacyForumId = 1,
            Name = "The Music",
            SortOrder = 1,
            LegacyPostCount = 0,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private void CreateModernForumTables()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ModernForumCategory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL,
                LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ModernForumThread
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyTopicId INTEGER NOT NULL UNIQUE,
                LegacyForumId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL,
                StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL,
                LastActivityAt TEXT NULL,
                ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL,
                IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL,
                StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL,
                StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES ModernForumCategory (Id)
            );

            CREATE TABLE IF NOT EXISTS ModernForumPost
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyPostId INTEGER NOT NULL UNIQUE,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                AuthorMemberId TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread (Id)
            );
            """);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

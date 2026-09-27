using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumPollRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-11T12:00:00Z");

    [Fact]
    public async Task CastVoteAsync_RejectsSecondVoteOnSingleChoicePoll()
    {
        var (repo, pollId, options) = await CreatePollAsync(isMulti: false);
        var member = Guid.NewGuid();

        await repo.CastVoteAsync(pollId, member, [options[0]]);
        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repo.CastVoteAsync(pollId, member, [options[1]]));

        Assert.Equal(ForumPollVoteException.AlreadyVoted, ex.Code);
    }

    [Fact]
    public async Task CastVoteAsync_RejectsVotesOnClosedPoll()
    {
        var (repo, pollId, options) = await CreatePollAsync(
            isMulti: false,
            closesAt: Now.AddMinutes(-5));
        var member = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repo.CastVoteAsync(pollId, member, [options[0]]));

        Assert.Equal(ForumPollVoteException.Closed, ex.Code);
    }

    [Fact]
    public async Task CastVoteAsync_RejectsMoreOptionsThanMaxChoices()
    {
        var (repo, pollId, options) = await CreatePollAsync(isMulti: true, maxChoices: 1);
        var member = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repo.CastVoteAsync(pollId, member, [options[0], options[1]]));

        Assert.Equal(ForumPollVoteException.MaxChoices, ex.Code);
    }

    [Fact]
    public async Task GetPollWithResultsAsync_ReturnsCorrectPercentages()
    {
        var (repo, pollId, options) = await CreatePollAsync(isMulti: false);
        await repo.CastVoteAsync(pollId, Guid.NewGuid(), [options[0]]);
        await repo.CastVoteAsync(pollId, Guid.NewGuid(), [options[0]]);
        await repo.CastVoteAsync(pollId, Guid.NewGuid(), [options[1]]);

        var results = await repo.GetPollWithResultsAsync(1001, null);
        Assert.NotNull(results);
        Assert.Equal(3, results!.TotalVotes);
        Assert.Equal(3, results.DistinctVoters);
        Assert.Equal(2, results.Options[0].VoteCount);
        Assert.Equal(66.7, results.Options[0].Percentage);
        Assert.Equal(1, results.Options[1].VoteCount);
        Assert.Equal(33.3, results.Options[1].Percentage);
    }

    [Fact]
    public async Task GetPollWithResultsAsync_AdminWithoutMember_CanClose()
    {
        var (repo, _, _) = await CreatePollAsync(isMulti: false);
        var results = await repo.GetPollWithResultsAsync(1001, viewerMemberId: null, viewerIsAdmin: true);
        Assert.NotNull(results);
        Assert.True(results!.CanViewerClose);

        var stranger = await repo.GetPollWithResultsAsync(1001, Guid.NewGuid(), viewerIsAdmin: false);
        Assert.False(stranger!.CanViewerClose);
    }

    [Fact]
    public async Task ClosePollAsync_AdminWithoutMember_ClosesPoll()
    {
        var (repo, pollId, _) = await CreatePollAsync(isMulti: false);
        await repo.ClosePollAsync(pollId, Guid.Empty, isAdmin: true);
        var results = await repo.GetPollWithResultsAsync(1001, null, viewerIsAdmin: true);
        Assert.True(results!.IsClosed);
        Assert.False(results.CanViewerClose);
    }

    [Fact]
    public async Task ClosePollAsync_AllowsAuthor()
    {
        var author = Guid.NewGuid();
        var (repo, pollId, _) = await CreatePollAsync(isMulti: false, authorId: author);

        await repo.ClosePollAsync(pollId, author, isAdmin: false);
        var results = await repo.GetPollWithResultsAsync(1001, author);
        Assert.True(results!.IsClosed);
        Assert.False(results.CanViewerVote);
    }

    [Fact]
    public async Task CreateThread_WithPoll_PersistsPoll()
    {
        var write = new InMemoryForumWriteRepository();
        var polls = new InMemoryForumPollRepository(new FixedClock(Now));
        write.AttachPollRepository(polls);

        var member = Guid.NewGuid();
        var created = await write.CreateThreadAsync(new NewForumThread(
            1,
            member,
            "Author",
            "Poll topic",
            "<p>Body</p>",
            Now,
            new NewForumPoll(
                "Best album?",
                false,
                null,
                null,
                ["A", "B", "C"],
                member)));

        var results = await polls.GetPollWithResultsAsync(created.TopicId, member);
        Assert.NotNull(results);
        Assert.Equal("Best album?", results!.Question);
        Assert.Equal(3, results.Options.Count);
        Assert.True(results.CanViewerVote);
    }

    private static async Task<(InMemoryForumPollRepository Repo, Guid PollId, Guid[] Options)> CreatePollAsync(
        bool isMulti,
        int? maxChoices = null,
        DateTimeOffset? closesAt = null,
        Guid? authorId = null)
    {
        var clock = new FixedClock(Now);
        var repo = new InMemoryForumPollRepository(clock);
        var author = authorId ?? Guid.NewGuid();
        repo.RegisterTopic(1001);
        var pollId = await repo.CreatePollAsync(
            1001,
            new NewForumPoll(
                "Pick one",
                isMulti,
                maxChoices,
                closesAt,
                ["Alpha", "Beta", "Gamma"],
                author));

        var results = await repo.GetPollWithResultsAsync(1001, null);
        var options = results!.Options.Select(option => option.OptionId).ToArray();
        return (repo, pollId, options);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

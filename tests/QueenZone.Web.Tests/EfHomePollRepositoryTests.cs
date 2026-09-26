using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class EfHomePollRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfHomePollRepository repository;

    public EfHomePollRepositoryTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        repository = new EfHomePollRepository(dbContext, TimeProvider.System);
    }

    [Fact]
    public async Task Filtered_unique_index_allows_only_one_current_poll()
    {
        var first = await repository.CreateAsync(new AdminHomePollDraft("First?", ["A", "B"]), Guid.NewGuid());
        var second = await repository.CreateAsync(new AdminHomePollDraft("Second?", ["C", "D"]), Guid.NewGuid());
        await repository.PublishAsync(first);
        await repository.PublishAsync(second);

        var current = await repository.GetCurrentAsync(null);
        Assert.Equal(second, current!.PollId);
        Assert.False((await repository.GetByIdAsync(first))!.IsCurrent);
        Assert.Equal(1, await dbContext.HomePolls.CountAsync(poll => poll.IsCurrent));
    }

    [Fact]
    public async Task Hide_current_then_publish_other_makes_only_the_new_poll_current()
    {
        var first = await repository.CreateAsync(new AdminHomePollDraft("First?", ["A", "B"]), Guid.NewGuid());
        var second = await repository.CreateAsync(new AdminHomePollDraft("Second?", ["C", "D"]), Guid.NewGuid());
        await repository.PublishAsync(first);
        await repository.HideAsync(first);
        await repository.PublishAsync(second);

        var current = await repository.GetCurrentAsync(null);
        Assert.Equal(second, current!.PollId);
        Assert.False((await repository.GetByIdAsync(first))!.IsCurrent);
        Assert.Equal(1, await dbContext.HomePolls.CountAsync(poll => poll.IsCurrent));
    }

    [Fact]
    public async Task Republish_of_the_current_poll_is_idempotent_success()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Again?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var firstPublishedAt = (await repository.GetByIdAsync(pollId))!.PublishedAt;

        await repository.PublishAsync(pollId);

        var again = await repository.GetByIdAsync(pollId);
        Assert.True(again!.IsCurrent);
        Assert.Equal(firstPublishedAt, again.PublishedAt);
        Assert.Equal(pollId, (await repository.GetCurrentAsync(null))!.PollId);
        Assert.Equal(1, await dbContext.HomePolls.CountAsync(poll => poll.IsCurrent));
    }

    [Fact]
    public async Task Publish_uses_configured_retrying_execution_strategy_for_its_transaction()
    {
        await using var retryConnection = new SqliteConnection("DataSource=:memory:");
        await retryConnection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(retryConnection)
            .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>()
            .Options;
        await using var retryContext = new QueenZoneDbContext(options);
        await retryContext.Database.EnsureCreatedAsync();
        var retryRepository = new EfHomePollRepository(retryContext, TimeProvider.System);
        var pollId = await retryRepository.CreateAsync(
            new AdminHomePollDraft("Retry-safe?", ["Yes", "No"]),
            Guid.NewGuid());

        await retryRepository.PublishAsync(pollId);

        Assert.Equal(pollId, (await retryRepository.GetCurrentAsync(null))!.PollId);
    }

    [Fact]
    public async Task CastVote_persists_one_ballot_and_rejects_the_second()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["Yes", "No"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var current = await repository.GetCurrentAsync(null);
        var member = Guid.NewGuid();

        await repository.CastVoteAsync(current!.Options[0].OptionId, member);
        var voted = await repository.GetCurrentAsync(member);
        Assert.True(voted!.ViewerHasVoted);
        Assert.Equal(1, voted.TotalVotes);
        Assert.Equal(100, voted.Options[0].Percentage);

        var second = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repository.CastVoteAsync(current.Options[1].OptionId, member));
        Assert.Equal(ForumPollVoteException.AlreadyVoted, second.Code);
        Assert.Equal(1, (await repository.GetCurrentAsync(null))!.TotalVotes);
    }

    [Fact]
    public async Task Existing_ballot_row_is_treated_as_already_voted()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var current = await repository.GetCurrentAsync(null);
        var member = Guid.NewGuid();
        dbContext.HomePollVotes.Add(new QueenZone.Data.Entities.HomePollVoteEntity
        {
            Id = Guid.NewGuid(),
            PollId = pollId,
            OptionId = current!.Options[0].OptionId,
            MemberAccountId = member,
            VotedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repository.CastVoteAsync(current.Options[1].OptionId, member));
        Assert.Equal(ForumPollVoteException.AlreadyVoted, ex.Code);
        Assert.Equal(1, (await repository.GetCurrentAsync(null))!.TotalVotes);
    }

    [Fact]
    public async Task Update_after_vote_is_rejected()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var current = await repository.GetCurrentAsync(null);
        await repository.CastVoteAsync(current!.Options[0].OptionId, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<HomePollException>(() =>
            repository.UpdateAsync(pollId, new AdminHomePollDraft("Nope", ["X", "Y"])));
        Assert.Equal(HomePollException.HasVotes, ex.Code);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class TestRetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new TestRetryingExecutionStrategy(dependencies);
    }

    private sealed class TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}

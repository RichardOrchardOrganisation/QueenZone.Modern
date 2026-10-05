using QueenZone.Data;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAgentRunRequestRepositoryTests
{
    [Fact]
    public async Task Queue_deduplicates_active_request_and_allows_next_after_completion()
    {
        var repository = CreateRepository();

        var first = await repository.QueueAsync(new QueenZone.Data.NewsAgentRunRequestCreate("editor@example.com"));
        var duplicate = await repository.QueueAsync(new QueenZone.Data.NewsAgentRunRequestCreate("other@example.com"));

        Assert.True(first.WasCreated);
        Assert.False(duplicate.WasCreated);
        Assert.Equal(first.Request.Id, duplicate.Request.Id);

        var claimed = await repository.ClaimNextAsync("news-pc");
        Assert.NotNull(claimed);
        Assert.Equal(NewsAgentRunRequestStatus.Running, claimed.Status);
        Assert.True(await repository.CompleteAsync(claimed.Id, "Done"));

        var next = await repository.QueueAsync(new QueenZone.Data.NewsAgentRunRequestCreate("editor@example.com"));
        Assert.True(next.WasCreated);
        Assert.NotEqual(first.Request.Id, next.Request.Id);
    }

    [Fact]
    public async Task Claim_is_single_consumer_and_records_heartbeat()
    {
        var clock = new FixedTimeProvider();
        var repository = new InMemoryNewsAgentRunRequestRepository(new SharedNewsAgentRunRequestStore(clock));
        await repository.QueueAsync(new QueenZone.Data.NewsAgentRunRequestCreate("editor@example.com"));

        var first = await repository.ClaimNextAsync("news-pc");
        var second = await repository.ClaimNextAsync("news-pc-2");
        var heartbeat = await repository.GetLatestHeartbeatAsync();

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.NotNull(heartbeat);
        Assert.Equal("news-pc-2", heartbeat.RunnerId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, heartbeat.LastSeenAtUtc);
    }

    [Fact]
    public async Task Return_to_pending_allows_a_later_poll_to_retry()
    {
        var repository = CreateRepository();
        await repository.QueueAsync(new QueenZone.Data.NewsAgentRunRequestCreate("editor@example.com"));
        var claimed = await repository.ClaimNextAsync("news-pc");

        Assert.True(await repository.ReturnToPendingAsync(claimed!.Id));

        var retried = await repository.ClaimNextAsync("news-pc");
        Assert.Equal(claimed.Id, retried?.Id);
    }

    private static InMemoryNewsAgentRunRequestRepository CreateRepository() =>
        new(new SharedNewsAgentRunRequestStore());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    }

}

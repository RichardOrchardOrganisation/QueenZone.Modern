using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintPoolCacheTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly FakeTimeProvider time = new();
    private readonly QuizSprintPoolCache cache;
    private readonly EfQuizRepository cached;
    private readonly EfQuizRepository uncached;
    private readonly Guid memberId = Guid.NewGuid();

    public QuizSprintPoolCacheTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();

        cache = new QuizSprintPoolCache(time);
        cached = new EfQuizRepository(dbContext, time, cache);
        // Shares the database but never invalidates, so it stands in for an out-of-band writer.
        uncached = new EfQuizRepository(dbContext, time);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static AdminQuizDraft Draft(string title) =>
        new(
            title,
            null,
            [
                new QuizQuestionDraft(
                    $"{title}: who sang?",
                    2,
                    [new QuizOptionDraft("Freddie Mercury", true), new QuizOptionDraft("Brian May", false)]),
            ]);

    [Fact]
    public async Task Pool_is_served_from_cache_until_it_is_invalidated_or_expires()
    {
        var id = await cached.CreateAsync(Draft("Quiz"), memberId);
        await cached.PublishAsync(id);
        Assert.Single(await cached.GetPublishedSprintQuestionsAsync());

        // An out-of-band publish does not invalidate: the cached pool is still served.
        var second = await uncached.CreateAsync(Draft("Second"), memberId);
        await uncached.PublishAsync(second);
        Assert.Single(await cached.GetPublishedSprintQuestionsAsync());

        time.Advance(QuizSprintPoolCache.Ttl + TimeSpan.FromSeconds(1));
        Assert.Equal(2, (await cached.GetPublishedSprintQuestionsAsync()).Count);
    }

    [Fact]
    public async Task Publish_unpublish_update_and_delete_each_invalidate_the_pool()
    {
        var id = await cached.CreateAsync(Draft("Quiz"), memberId);
        Assert.Empty(await cached.GetPublishedSprintQuestionsAsync());
        Assert.False(await cached.HasPublishedSprintQuestionsAsync());

        await cached.PublishAsync(id);
        Assert.Single(await cached.GetPublishedSprintQuestionsAsync());
        Assert.True(await cached.HasPublishedSprintQuestionsAsync());

        await cached.UpdateAsync(id, Draft("Renamed"));
        Assert.Equal("Renamed: who sang?", Assert.Single(await cached.GetPublishedSprintQuestionsAsync()).Text);

        await cached.UnpublishAsync(id);
        Assert.Empty(await cached.GetPublishedSprintQuestionsAsync());

        await cached.PublishAsync(id);
        Assert.Single(await cached.GetPublishedSprintQuestionsAsync());
        await cached.UnpublishAsync(id);
        await cached.DeleteAsync(id);
        Assert.False(await cached.HasPublishedSprintQuestionsAsync());
    }

    [Fact]
    public async Task Create_invalidates_so_a_cached_empty_pool_is_not_kept()
    {
        Assert.Empty(await cached.GetPublishedSprintQuestionsAsync());

        var id = await uncached.CreateAsync(Draft("Quiz"), memberId);
        await uncached.PublishAsync(id);
        Assert.Empty(await cached.GetPublishedSprintQuestionsAsync());

        await cached.CreateAsync(Draft("Another"), memberId);
        Assert.Single(await cached.GetPublishedSprintQuestionsAsync());
    }

    [Fact]
    public async Task Invalidation_during_a_load_prevents_storing_the_stale_result()
    {
        var loads = 0;
        var invalidateDuringFirstLoad = true;

        async Task<IReadOnlyList<QuizSprintQuestion>> Load(CancellationToken token)
        {
            loads++;
            await Task.Yield();
            if (invalidateDuringFirstLoad)
            {
                invalidateDuringFirstLoad = false;
                cache.Invalidate();
            }

            return [new QuizSprintQuestion(Guid.NewGuid(), $"load {loads}", [])];
        }

        var first = await cache.GetAsync(Load);
        var second = await cache.GetAsync(Load);
        var third = await cache.GetAsync(Load);

        Assert.Equal("load 1", Assert.Single(first).Text);
        Assert.Equal("load 2", Assert.Single(second).Text);
        Assert.Same(second, third);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Concurrent_cold_callers_share_one_load()
    {
        var loads = 0;
        var release = new TaskCompletionSource();

        async Task<IReadOnlyList<QuizSprintQuestion>> Load(CancellationToken token)
        {
            Interlocked.Increment(ref loads);
            await release.Task;
            return [new QuizSprintQuestion(Guid.NewGuid(), "q", [])];
        }

        var callers = Enumerable.Range(0, 8).Select(_ => cache.GetAsync(Load)).ToArray();
        release.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, loads);
        Assert.All(results, result => Assert.Same(results[0], result));
    }
}

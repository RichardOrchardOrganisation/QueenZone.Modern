using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class SearchReindexJobServiceTests
{
    [Fact]
    public async Task Run_retries_sql_deadlock_then_succeeds()
    {
        var flaky = CreateFlakyIndex(SqlExceptionFactory.Create(
            SearchReindexSqlRetry.DeadlockNumber,
            "Transaction was deadlocked on lock resources."));
        flaky.FailuresRemaining = 1;
        var (job, logger) = CreateJob(flaky);

        Assert.True(job.TryStart());
        await job.WaitForCurrentRunAsync();

        Assert.Equal(SearchReindexJobPhase.Succeeded, job.GetSnapshot().Phase);
        Assert.Equal("Search index rebuilt.", job.GetSnapshot().Message);
        Assert.Equal(1, flaky.TransientFailuresThrown);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("transient SQL fault", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Run_retries_sql_timeout_then_succeeds()
    {
        var flaky = CreateFlakyIndex(SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding."));
        flaky.FailuresRemaining = 1;
        var (job, _) = CreateJob(flaky);

        Assert.True(job.TryStart());
        await job.WaitForCurrentRunAsync();

        Assert.Equal(SearchReindexJobPhase.Succeeded, job.GetSnapshot().Phase);
        Assert.Equal(1, flaky.TransientFailuresThrown);
    }

    [Fact]
    public async Task Run_surfaces_sql_details_when_failure_is_not_transient()
    {
        var flaky = CreateFlakyIndex(SqlExceptionFactory.Create(208, "Invalid object name 'SearchDocument'."));
        flaky.FailuresRemaining = 3;
        var (job, logger) = CreateJob(flaky);

        Assert.True(job.TryStart());
        await job.WaitForCurrentRunAsync();

        var snapshot = job.GetSnapshot();
        Assert.Equal(SearchReindexJobPhase.Failed, snapshot.Phase);
        Assert.Contains("SqlException (208)", snapshot.Message, StringComparison.Ordinal);
        Assert.Contains("SearchDocument", snapshot.Message, StringComparison.Ordinal);
        Assert.Equal(1, flaky.TransientFailuresThrown);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Background search reindex failed", StringComparison.Ordinal));
    }

    private static FlakySearchIndexService CreateFlakyIndex(Exception exception)
    {
        var store = new SharedSearchIndexStore();
        return new FlakySearchIndexService
        {
            Inner = new InMemorySearchIndexService(store, new SearchIndexRevision()),
            Exception = exception,
        };
    }

    private static (SearchReindexJobService Job, CollectingLogger<SearchReindexJobService> Logger) CreateJob(
        FlakySearchIndexService index)
    {
        var builder = CreateBuilder(index);
        var services = new ServiceCollection();
        services.AddScoped(_ => builder);
        var provider = services.BuildServiceProvider();
        var logger = new CollectingLogger<SearchReindexJobService>();
        var job = new SearchReindexJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubHostLifetime(),
            TimeProvider.System,
            logger);
        return (job, logger);
    }

    private static SearchReindexBuilder CreateBuilder(ISearchIndexService indexService)
    {
        var newsStore = new SharedNewsStore(SampleNewsData.CreateSeedArticles());
        var forumWriteRepository = new InMemoryForumWriteRepository();
        var articleSubmissionRepository = new InMemoryArticleSubmissionRepository();

        return new SearchReindexBuilder(
            indexService,
            new InMemoryNewsRepository(newsStore),
            new InMemoryForumRepository(
                SampleForumData.CreateSeedCategories(),
                SampleForumData.CreateSeedStats(),
                forumWriteRepository,
                new InMemoryForumAttachmentRepository()),
            new InMemoryArticleRepository(articleSubmissionRepository),
            new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
            new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
            new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()),
            new InMemoryQueenHistoryRepository(SampleQueenHistoryData.CreateSeedEvents()),
            new InMemoryFanPerformanceRepository(SampleFanPerformanceData.CreateSeedPerformances()));
    }

    /// <summary>
    /// Throws <see cref="Exception"/> from the first <see cref="FailuresRemaining"/>
    /// <see cref="ReplaceContentTypeAsync"/> calls, then delegates. Reindex retries
    /// recreate the same scoped builder, so the remaining count persists across attempts.
    /// </summary>
    private sealed class FlakySearchIndexService : ISearchIndexService
    {
        public ISearchIndexService Inner { get; set; } = null!;

        public Exception Exception { get; set; } = new InvalidOperationException("unset");

        public int FailuresRemaining { get; set; }

        public int TransientFailuresThrown { get; private set; }

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                TransientFailuresThrown++;
                throw Exception;
            }

            return Inner.ReplaceContentTypeAsync(contentType, documents, cancellationToken);
        }

        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Inner.UpsertAsync(document, cancellationToken);

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Inner.RemoveAsync(sourceKey, cancellationToken);

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default) =>
            Inner.GetContentTypeCountsAsync(cancellationToken);
    }

    private sealed class StubHostLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}

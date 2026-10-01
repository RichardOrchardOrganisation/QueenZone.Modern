using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class ForumSearchIndexSynchronizerTests
{
    [Fact]
    public async Task UpsertThreadAsync_WritesMappedForumDocument()
    {
        var store = new SharedSearchIndexStore();
        var lastActivity = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var synchronizer = new ForumSearchIndexSynchronizer(
            new InMemorySearchIndexService(store, new SearchIndexRevision()),
            NullLogger<ForumSearchIndexSynchronizer>.Instance);

        await synchronizer.UpsertThreadAsync(4521, "  Live forum search title  ", lastActivity);

        var document = Assert.Single(store.GetAll());
        var expected = SearchReindexBuilder.MapForumThread(
            new ForumTopicSitemapItem(4521, "Live forum search title", lastActivity.UtcDateTime));
        Assert.Equal(expected.SourceKey, document.SourceKey);
        Assert.Equal(expected.ContentType, document.ContentType);
        Assert.Equal(expected.Title, document.Title);
        Assert.Equal(expected.Body, document.Body);
        Assert.Equal(expected.Summary, document.Summary);
        Assert.Equal(expected.Url, document.Url);
        Assert.Equal(expected.PublishedAt, document.PublishedAt);
        Assert.Equal("/forum/topic/4521/live-forum-search-title", document.Url);
    }

    [Fact]
    public async Task UpsertThreadAsync_RemovesDocument_WhenTitleIsBlank()
    {
        var store = new SharedSearchIndexStore();
        var index = new InMemorySearchIndexService(store, new SearchIndexRevision());
        await index.UpsertAsync(SearchReindexBuilder.MapForumThread(
            new ForumTopicSitemapItem(9, "Keep me", DateTime.UtcNow)));
        var synchronizer = new ForumSearchIndexSynchronizer(
            index,
            NullLogger<ForumSearchIndexSynchronizer>.Instance);

        await synchronizer.UpsertThreadAsync(9, "   ", DateTimeOffset.UtcNow);

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task UpsertThreadAsync_LogsAndSwallows_WhenIndexThrows()
    {
        var logger = new RecordingLogger<ForumSearchIndexSynchronizer>();
        var synchronizer = new ForumSearchIndexSynchronizer(new ThrowingSearchIndexService(), logger);

        await synchronizer.UpsertThreadAsync(12, "Should not fail the write", DateTimeOffset.UtcNow);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("forum thread 12", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public async Task RemoveThreadAsync_DeletesBySourceKey()
    {
        var store = new SharedSearchIndexStore();
        var index = new InMemorySearchIndexService(store, new SearchIndexRevision());
        await index.UpsertAsync(SearchReindexBuilder.MapForumThread(
            new ForumTopicSitemapItem(44, "Remove me", DateTime.UtcNow)));
        var synchronizer = new ForumSearchIndexSynchronizer(
            index,
            NullLogger<ForumSearchIndexSynchronizer>.Instance);

        await synchronizer.RemoveThreadAsync(44);

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task RemoveThreadAsync_LogsAndSwallows_WhenIndexThrows()
    {
        var logger = new RecordingLogger<ForumSearchIndexSynchronizer>();
        var synchronizer = new ForumSearchIndexSynchronizer(new ThrowingSearchIndexService(), logger);

        await synchronizer.RemoveThreadAsync(88);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("forum thread 88", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public async Task UpsertThreadAsync_DoesNotSwallowCancellation()
    {
        var synchronizer = new ForumSearchIndexSynchronizer(
            new ThrowingSearchIndexService(new OperationCanceledException()),
            NullLogger<ForumSearchIndexSynchronizer>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => synchronizer.UpsertThreadAsync(1, "Cancelled", DateTimeOffset.UtcNow));
    }

    private sealed class ThrowingSearchIndexService(Exception? exception = null) : ISearchIndexService
    {
        private readonly Exception exception = exception ?? new InvalidOperationException("Simulated index failure.");

        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Task.FromException(exception);

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Task.FromException(exception);

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            Task.FromException(exception);

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyDictionary<string, int>>(exception);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

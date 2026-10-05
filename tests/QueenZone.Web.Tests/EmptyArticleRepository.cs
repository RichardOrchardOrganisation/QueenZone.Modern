using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// No-op IArticleRepository used by test harnesses that remove IMemberAccountRepository
/// from DI (which would otherwise break the InMemoryArticleRepository singleton factory).
/// </summary>
internal sealed class EmptyArticleRepository : IArticleRepository
{
    public Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
        string? tag = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ArticleFeedKey>>([]);

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>([]);

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page, int pageSize, string? tag = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(Array.Empty<PublishedArticleSubmission>());

    public Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult<PublishedArticleSubmission?>(null);

    public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt, CancellationToken ct = default) =>
        Task.FromResult<(PublishedArticleSubmission?, PublishedArticleSubmission?)>((null, null));

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(Array.Empty<PublishedArticleSubmission>());
}

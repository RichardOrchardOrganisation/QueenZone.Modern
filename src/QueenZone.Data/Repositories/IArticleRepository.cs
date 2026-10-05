namespace QueenZone.Data;

public interface IArticleRepository
{
    Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default);
    Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(int page, int pageSize, string? tag = null, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(string? tag = null, CancellationToken ct = default);
    Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default);
    Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(DateTimeOffset publishedAt, CancellationToken ct = default);
    Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default);
}

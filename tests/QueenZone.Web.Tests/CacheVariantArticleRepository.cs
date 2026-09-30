using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>Two community pages with disjoint tags for production output-cache tests.</summary>
internal sealed class CacheVariantArticleRepository : IArticleRepository
{
    private readonly IReadOnlyList<PublishedArticleSubmission> articles = Enumerable.Range(1, 24)
        .Select(index => new PublishedArticleSubmission(
            Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
            $"Community cache article {index:D2}",
            $"community-cache-{index}",
            "A community article for cache variation tests.",
            "Article body.",
            null,
            index <= 12 ? "music" : "live",
            new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero).AddDays(-index),
            "Cache test author",
            200))
        .ToList();

    public Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default) =>
        Task.FromResult(Filter(tag).Count());

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page, int pageSize, string? tag = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(
            Filter(tag).Skip((page - 1) * pageSize).Take(pageSize).ToList());

    public Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(articles.FirstOrDefault(article => article.Slug == slug));

    public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt, CancellationToken ct = default) =>
        Task.FromResult((
            articles.FirstOrDefault(article => article.PublishedAt < publishedAt),
            articles.LastOrDefault(article => article.PublishedAt > publishedAt)));

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default) =>
        Task.FromResult(articles);

    private IEnumerable<PublishedArticleSubmission> Filter(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? articles : articles.Where(article => article.Tags == tag);
}

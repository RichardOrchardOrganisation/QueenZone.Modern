namespace QueenZone.Data;

public sealed class InMemoryArticleRepository(IArticleSubmissionRepository submissionRepository, IEditorialArticleRepository? editorialArticles = null) : IArticleRepository
{
    public async Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return string.IsNullOrWhiteSpace(tag)
            ? all.Count
            : all.Count(a => HasTag(a.Tags, tag));
    }

    public async Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
        string? tag = null, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        IEnumerable<PublishedArticleSubmission> filtered = string.IsNullOrWhiteSpace(tag)
            ? all
            : all.Where(a => HasTag(a.Tags, tag));
        return filtered.Select(a => ArticleFeedKey.Community(a.Id, a.PublishedAt.UtcDateTime)).ToList();
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var set = ids.ToHashSet();
        var all = await GetAllAsync(ct);
        return all.Where(a => set.Contains(a.Id)).ToList();
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page, int pageSize, string? tag = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var all = await GetAllAsync(ct);
        IEnumerable<PublishedArticleSubmission> filtered = string.IsNullOrWhiteSpace(tag)
            ? all
            : all.Where(a => HasTag(a.Tags, tag));

        return filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public async Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        // all is ordered descending by PublishedAt
        var prev = all.FirstOrDefault(a => a.PublishedAt < publishedAt);
        var next = all.LastOrDefault(a => a.PublishedAt > publishedAt);
        return (prev, next);
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default) =>
        await GetAllAsync(ct);

    private async Task<IReadOnlyList<PublishedArticleSubmission>> GetAllAsync(CancellationToken ct)
    {
        var all = (await submissionRepository.GetPublishedAsync(ct)).ToList();
        if (editorialArticles is not null)
        {
            var editorials = await editorialArticles.GetPublishedStandaloneAsync(ct);
            var promotedIds = editorials.Where(x => x.SourceSubmissionId is not null).Select(x => x.SourceSubmissionId!.Value).ToHashSet();
            all.RemoveAll(x => promotedIds.Contains(x.Id));
            all.AddRange(editorials.Select(x => new PublishedArticleSubmission(
                x.Id, x.Title, x.Slug, x.Excerpt, x.Body, x.ImageBlobKey, x.Tags, x.PublishedAt,
                x.AuthorName, EfArticleSubmissionRepository.EstimateWordCount(x.Body), Category: x.Category, Source: x.Source)));
        }
        return all.OrderByDescending(x => x.PublishedAt).ToList();
    }

    private static bool HasTag(string? tags, string tag) =>
        !string.IsNullOrWhiteSpace(tags) &&
        ("," + tags + ",").Contains("," + tag + ",", StringComparison.OrdinalIgnoreCase);
}

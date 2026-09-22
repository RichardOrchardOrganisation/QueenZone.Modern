namespace QueenZone.Data;

public sealed class InMemoryEditorialArticleRepository(TimeProvider? timeProvider = null) : IEditorialArticleRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, EditorialArticle> rows = [];
    private readonly Dictionary<Guid, EditorialArticle> live = [];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<EditorialArticle>> GetAllAsync(CancellationToken ct = default) { lock (gate) return Task.FromResult<IReadOnlyList<EditorialArticle>>(rows.Values.OrderByDescending(x => x.UpdatedAt).ToList()); }
    public Task<EditorialArticle?> GetAsync(Guid id, CancellationToken ct = default) { lock (gate) return Task.FromResult(rows.GetValueOrDefault(id)); }
    public Task<EditorialArticle?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default) { lock (gate) return Task.FromResult(live.Values.SingleOrDefault(x => x.LegacyArticleId is null && rows[x.Id].Status != EditorialArticleStatus.Unpublished && x.Slug == slug)); }
    public Task<IReadOnlyList<EditorialArticle>> GetPublishedStandaloneAsync(CancellationToken ct = default) { lock (gate) return Task.FromResult<IReadOnlyList<EditorialArticle>>(live.Values.Where(x => x.LegacyArticleId is null && rows[x.Id].Status != EditorialArticleStatus.Unpublished).ToList()); }
    public Task<int> GetUnpublishedLegacyOverlayCountAsync(CancellationToken ct = default) { lock (gate) return Task.FromResult(live.Values.Count(x => x.LegacyArticleId is not null && rows[x.Id].Status == EditorialArticleStatus.Unpublished)); }
    public Task<IReadOnlyDictionary<int, EditorialArticle>> GetPublishedLegacyOverlaysAsync(IEnumerable<int> ids, CancellationToken ct = default) { lock (gate) { var set = ids.ToHashSet(); return Task.FromResult<IReadOnlyDictionary<int, EditorialArticle>>(live.Values.Where(x => x.LegacyArticleId is int id && set.Contains(id)).Select(x => x with { Status = rows[x.Id].Status }).ToDictionary(x => x.LegacyArticleId!.Value)); } }
    public Task<EditorialArticle> SaveDraftAsync(EditorialArticleDraft draft, string editor, CancellationToken ct = default)
    {
        lock (gate)
        {
            var id = draft.Id ?? Guid.NewGuid();
            var slug = NewsSlug.Resolve(draft.Title, draft.Slug);
            if (rows.Values.Any(x => x.Id != id && x.Slug == slug) || live.Values.Any(x => x.Id != id && x.Slug == slug)) throw new InvalidOperationException("That article slug is already in use.");
            var existing = rows.GetValueOrDefault(id);
            // Visibility SoT is Status. Unpublished stays sticky until explicit Publish.
            // Edit-while-live may return to Draft. Live* snapshot in `live` is not cleared.
            var status = existing?.Status == EditorialArticleStatus.Unpublished
                ? EditorialArticleStatus.Unpublished
                : EditorialArticleStatus.Draft;
            var row = new EditorialArticle(id, draft.LegacyArticleId, draft.SourceSubmissionId, draft.Title.Trim(), slug, draft.Excerpt.Trim(), draft.Body, draft.AuthorName.Trim(), draft.Category.Trim(), draft.Tags, draft.Source, draft.ImageBlobKey, status, draft.PublishedAt, clock.GetUtcNow(), editor, live.GetValueOrDefault(id)?.ImageBlobKey, live.ContainsKey(id));
            rows[id] = row; return Task.FromResult(row);
        }
    }
    public Task<EditorialArticle?> SetStatusAsync(Guid id, string status, string editor, CancellationToken ct = default) { lock (gate) { if (!rows.TryGetValue(id, out var row)) return Task.FromResult<EditorialArticle?>(null); row = row with { Status = status, UpdatedAt = clock.GetUtcNow(), UpdatedBy = editor, PublishedImageBlobKey = status == EditorialArticleStatus.Published ? row.ImageBlobKey : row.PublishedImageBlobKey, HasPublishedVersion = status == EditorialArticleStatus.Published || row.HasPublishedVersion, WordCount = status == EditorialArticleStatus.Published ? EfArticleSubmissionRepository.EstimateWordCount(row.Body) : row.WordCount }; rows[id] = row; if (status == EditorialArticleStatus.Published) live[id] = row; return Task.FromResult<EditorialArticle?>(row); } }
}

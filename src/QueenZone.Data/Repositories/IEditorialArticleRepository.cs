namespace QueenZone.Data;

public interface IEditorialArticleRepository
{
    Task<IReadOnlyList<EditorialArticle>> GetAllAsync(CancellationToken ct = default);
    Task<EditorialArticle?> GetAsync(Guid id, CancellationToken ct = default);
    Task<EditorialArticle?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<EditorialArticle>> GetPublishedStandaloneAsync(CancellationToken ct = default);
    Task<int> GetUnpublishedLegacyOverlayCountAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, EditorialArticle>> GetPublishedLegacyOverlaysAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task<EditorialArticle> SaveDraftAsync(EditorialArticleDraft draft, string editor, CancellationToken ct = default);
    Task<EditorialArticle?> SetStatusAsync(Guid id, string status, string editor, CancellationToken ct = default);
}

using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>Private catalog data contains solutions. Public endpoints must explicitly project safe DTOs.</summary>
public sealed record CrosswordCatalogItem(
    Guid Id,
    CrosswordSeed Seed,
    CrosswordStatus Status,
    DateTimeOffset? PublishAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByMemberId,
    DateTimeOffset UpdatedAt,
    string UpdatedByEmail,
    byte[] RowVersion);

public sealed record CrosswordImportResult(IReadOnlyList<string> Imported, IReadOnlyList<string> Skipped);

public sealed record CrosswordAuditItem(Guid Id, string Actor, string Action, DateTimeOffset CreatedAt, string Summary);

public interface ICrosswordCatalogRepository
{
    Task<IReadOnlyList<CrosswordCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CrosswordCatalogItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrosswordAuditItem>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> CreateDraftAsync(CrosswordSeed draft, Guid creatorId, string actor,
        CancellationToken cancellationToken = default);
    Task SaveDraftAsync(Guid id, CrosswordSeed draft, byte[] expectedRowVersion, string actor,
        CancellationToken cancellationToken = default);
    Task SetPublicationAsync(Guid id, CrosswordStatus status, DateTimeOffset? publishAt,
        byte[] expectedRowVersion, string actor, CancellationToken cancellationToken = default);
    Task<CrosswordImportResult> ImportAsync(IReadOnlyList<CrosswordSeed> seeds, Guid creatorId, string actor,
        bool publish = false, CancellationToken cancellationToken = default);
}

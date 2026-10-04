using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryCrosswordCatalogRepository(TimeProvider clock) : ICrosswordCatalogRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, CrosswordEntity> puzzles = [];
    private readonly Dictionary<Guid, List<CrosswordAuditItem>> audit = [];

    public Task<IReadOnlyList<CrosswordCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CrosswordCatalogItem> result = puzzles.Values.Select(CrosswordCatalogMapping.Read)
                .OrderBy(item => item.Seed.Slug, StringComparer.Ordinal).ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<CrosswordCatalogItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(puzzles.TryGetValue(id, out var puzzle) ? CrosswordCatalogMapping.Read(puzzle) : null);
        }
    }

    public Task<Guid> CreateDraftAsync(CrosswordSeed draft, Guid creatorId, string actor,
        CancellationToken cancellationToken = default)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            EnsureAvailableSlug(draft.Slug, null);
            var entity = CrosswordCatalogMapping.Create(draft, creatorId, actor, clock.GetUtcNow(), publish: false);
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            puzzles.Add(entity.Id, entity);
            AddAudit(entity.Id, actor, "Created", "Draft created.");
            return Task.FromResult(entity.Id);
        }
    }

    public Task SaveDraftAsync(Guid id, CrosswordSeed draft, byte[] expectedRowVersion, string actor,
        CancellationToken cancellationToken = default)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            var entity = puzzles[id];
            QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, expectedRowVersion);
            if (entity.Status != CrosswordStatus.Draft)
            {
                throw new InvalidOperationException("Use the published/scheduled editorial workflow to edit a live crossword.");
            }
            EnsureAvailableSlug(draft.Slug, id);
            CrosswordCatalogMapping.Apply(entity, draft, actor, clock.GetUtcNow());
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            AddAudit(id, actor, "Edited", "Draft grid and clues edited.");
            return Task.CompletedTask;
        }
    }

    public Task SetPublicationAsync(Guid id, CrosswordStatus status, DateTimeOffset? publishAt,
        byte[] expectedRowVersion, string actor, CancellationToken cancellationToken = default)
    {
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            var entity = puzzles[id];
            QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, expectedRowVersion);
            var action = CrosswordCatalogMapping.SetPublication(entity, status, publishAt, actor, clock.GetUtcNow());
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            AddAudit(id, actor, action, "Publication status changed to " + status + ".");
            return Task.CompletedTask;
        }
    }

    public Task<CrosswordImportResult> ImportAsync(IReadOnlyList<CrosswordSeed> seeds, Guid creatorId, string actor,
        bool publish = false, CancellationToken cancellationToken = default)
    {
        seeds = CrosswordCatalogMapping.NormalizeBatch(seeds);
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            var existing = puzzles.Values.Select(puzzle => puzzle.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var skipped = seeds.Where(seed => existing.Contains(seed.Slug)).Select(seed => seed.Slug).ToArray();
            var pending = seeds.Where(seed => !existing.Contains(seed.Slug)).Select(seed =>
                CrosswordCatalogMapping.Create(seed, creatorId, actor, clock.GetUtcNow(), publish)).ToArray();
            foreach (var entity in pending)
            {
                entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
                puzzles.Add(entity.Id, entity);
                AddAudit(entity.Id, actor, "Imported", publish ? "Imported as Published." : "Imported as Draft.");
            }
            return Task.FromResult(new CrosswordImportResult(pending.Select(puzzle => puzzle.Slug).ToArray(), skipped));
        }
    }

    public Task<IReadOnlyList<CrosswordAuditItem>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CrosswordAuditItem> result = audit.TryGetValue(id, out var rows)
                ? rows.OrderByDescending(log => log.CreatedAt).ToArray() : [];
            return Task.FromResult(result);
        }
    }

    private void AddAudit(Guid id, string actor, string action, string summary)
    {
        if (!audit.TryGetValue(id, out var rows))
        {
            rows = [];
            audit.Add(id, rows);
        }
        rows.Add(new(Guid.NewGuid(), actor, action, clock.GetUtcNow(), summary));
    }

    private void EnsureAvailableSlug(string slug, Guid? ownId)
    {
        if (puzzles.Values.Any(puzzle => puzzle.Id != ownId && string.Equals(puzzle.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A crossword with this slug already exists.");
        }
    }
}

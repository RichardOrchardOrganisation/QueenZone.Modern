using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryCrosswordCatalogRepository(TimeProvider clock) : ICrosswordCatalogRepository
{
    private readonly object gate = new();
    internal event Action<CrosswordCatalogItem>? ResetInProgress;
    private readonly Dictionary<Guid, CrosswordEntity> puzzles = [];
    private readonly Dictionary<Guid, List<CrosswordAuditItem>> audit = [];

    internal T WithPlayablePuzzle<T>(Guid id, DateTimeOffset now, Func<CrosswordCatalogItem, T> action)
    {
        lock (gate)
        {
            if (!puzzles.TryGetValue(id, out var entity))
            {
                throw new KeyNotFoundException("No playable crossword with that id.");
            }
            var puzzle = CrosswordCatalogMapping.Read(entity);
            if (!CrosswordVisibility.IsPlayable(puzzle, now))
            {
                throw new KeyNotFoundException("No playable crossword with that id.");
            }
            return action(puzzle);
        }
    }

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
        CancellationToken cancellationToken = default) =>
        CreateDraftCoreAsync(draft, creatorId, actor, "Created");

    public async Task<Guid> DuplicateAsync(Guid id, string newSlug, Guid creatorId, string actor,
        CancellationToken cancellationToken = default)
    {
        var source = await GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Crossword not found.");
        var title = "Copy of " + source.Seed.Title;
        var draft = source.Seed with { Slug = newSlug, Title = title[..Math.Min(200, title.Length)] };
        return await CreateDraftCoreAsync(draft, creatorId, actor, "Duplicated");
    }

    private Task<Guid> CreateDraftCoreAsync(CrosswordSeed draft, Guid creatorId, string actor, string auditAction)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            EnsureAvailableSlug(draft.Slug, null);
            var entity = CrosswordCatalogMapping.Create(draft, creatorId, actor, clock.GetUtcNow(), publish: false);
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            puzzles.Add(entity.Id, entity);
            AddAudit(entity.Id, actor, auditAction, auditAction == "Duplicated" ? "Grid and clues copied into a new Draft; no attempts copied." : "Draft created.");
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

    public Task SaveEditorialAsync(Guid id, CrosswordSeed draft, byte[] expectedRowVersion, string actor,
        bool confirmProgressReset = false, CancellationToken cancellationToken = default)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        lock (gate)
        {
            var entity = puzzles[id];
            QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, expectedRowVersion);
            EnsureAvailableSlug(draft.Slug, id);
            var changed = CrosswordCatalogMapping.PrepareEditorial(entity, draft, confirmProgressReset);
            CrosswordCatalogMapping.Apply(entity, draft, actor, clock.GetUtcNow());
            if (changed) ResetInProgress?.Invoke(CrosswordCatalogMapping.Read(entity));
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            AddAudit(id, actor, "Edited", changed ? "Grid/answers edited; saved grids reset; completed attempts preserved." : "Editorial metadata and clues edited.");
            return Task.CompletedTask;
        }
    }

    public Task PublishSelectedAsync(IReadOnlyList<CrosswordPublishSelection> selection, string actor, CancellationToken cancellationToken = default)
    {
        CrosswordCatalogMapping.ValidateActor(actor);
        if (selection.Count > 100 || selection.Select(row => row.Id).Distinct().Count() != selection.Count)
            throw new ArgumentException("Select up to 100 distinct puzzles.", nameof(selection));
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var row in selection)
            {
                var entity = puzzles[row.Id];
                QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, row.RowVersion);
                CrosswordCatalogMapping.ValidatePublication(entity, CrosswordStatus.Published, null, now);
            }
            foreach (var row in selection)
            {
                var entity = puzzles[row.Id];
                CrosswordCatalogMapping.SetPublication(entity, CrosswordStatus.Published, null, actor, now);
                entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
                AddAudit(row.Id, actor, "Published", "Published from admin selection.");
            }
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

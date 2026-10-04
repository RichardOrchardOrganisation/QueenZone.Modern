using System.Data;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

public sealed class EfCrosswordCatalogRepository(QueenZoneDbContext db, TimeProvider clock) : ICrosswordCatalogRepository
{
    public async Task<IReadOnlyList<CrosswordCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.Crosswords.AsNoTracking().Include(puzzle => puzzle.Entries).ToListAsync(cancellationToken);
        return entities.Select(CrosswordCatalogMapping.Read).OrderBy(item => item.Seed.Slug, StringComparer.Ordinal).ToArray();
    }

    public async Task<CrosswordCatalogItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Crosswords.AsNoTracking().Include(puzzle => puzzle.Entries)
            .SingleOrDefaultAsync(puzzle => puzzle.Id == id, cancellationToken);
        return entity is null ? null : CrosswordCatalogMapping.Read(entity);
    }

    public async Task<Guid> CreateDraftAsync(CrosswordSeed draft, Guid creatorId, string actor,
        CancellationToken cancellationToken = default)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        return await QueenZoneDbTransactions.ExecuteAsync(db, async token =>
        {
            var entity = CrosswordCatalogMapping.Create(draft, creatorId, actor, clock.GetUtcNow(), publish: false);
            if (!db.Database.IsSqlServer())
            {
                entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            }
            db.Crosswords.Add(entity);
            AddAudit(entity.Id, actor, "Created", "Draft created.");
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, cancellationToken);
    }

    public async Task SaveDraftAsync(Guid id, CrosswordSeed draft, byte[] expectedRowVersion, string actor,
        CancellationToken cancellationToken = default)
    {
        draft = CrosswordCatalogMapping.Normalize(draft, playable: false);
        CrosswordCatalogMapping.ValidateActor(actor);
        await QueenZoneDbTransactions.ExecuteAsync(db, async token =>
        {
            var entity = await db.Crosswords.Include(puzzle => puzzle.Entries).SingleAsync(puzzle => puzzle.Id == id, token);
            QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, expectedRowVersion);
            if (entity.Status != Entities.CrosswordStatus.Draft)
            {
                throw new InvalidOperationException("Use the published/scheduled editorial workflow to edit a live crossword.");
            }
            db.CrosswordEntries.RemoveRange(entity.Entries);
            await db.SaveChangesAsync(token);
            CrosswordCatalogMapping.Apply(entity, draft, actor, clock.GetUtcNow());
            db.CrosswordEntries.AddRange(entity.Entries);
            if (!db.Database.IsSqlServer())
            {
                entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            }
            await QueenZoneConcurrency.SaveChangesAsync(db, token);
            AddAudit(id, actor, "Edited", "Draft grid and clues edited.");
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task SetPublicationAsync(Guid id, Entities.CrosswordStatus status, DateTimeOffset? publishAt,
        byte[] expectedRowVersion, string actor, CancellationToken cancellationToken = default)
    {
        CrosswordCatalogMapping.ValidateActor(actor);
        await QueenZoneDbTransactions.ExecuteAsync(db, async token =>
        {
            var entity = await db.Crosswords.Include(puzzle => puzzle.Entries).SingleAsync(puzzle => puzzle.Id == id, token);
            QueenZoneConcurrency.EnsureRequiredRowVersion<OptimisticConcurrencyException>(entity.RowVersion, expectedRowVersion);
            var action = CrosswordCatalogMapping.SetPublication(entity, status, publishAt, actor, clock.GetUtcNow());
            if (!db.Database.IsSqlServer())
            {
                entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
            }
            AddAudit(id, actor, action, "Publication status changed to " + status + ".");
            await QueenZoneConcurrency.SaveChangesAsync(db, token);
            return true;
        }, cancellationToken);
    }

    public Task<CrosswordImportResult> ImportAsync(IReadOnlyList<CrosswordSeed> seeds, Guid creatorId, string actor,
        bool publish = false, CancellationToken cancellationToken = default)
    {
        seeds = CrosswordCatalogMapping.NormalizeBatch(seeds);
        CrosswordCatalogMapping.ValidateActor(actor);
        return QueenZoneDbTransactions.ExecuteAsync(db, IsolationLevel.Serializable, async token =>
        {
            var existing = (await db.Crosswords.Select(puzzle => puzzle.Slug).ToListAsync(token))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var imported = new List<string>();
            var skipped = new List<string>();
            foreach (var seed in seeds)
            {
                if (existing.Contains(seed.Slug))
                {
                    skipped.Add(seed.Slug);
                    continue;
                }
                var entity = CrosswordCatalogMapping.Create(seed, creatorId, actor, clock.GetUtcNow(), publish);
                if (!db.Database.IsSqlServer())
                {
                    entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
                }
                db.Crosswords.Add(entity);
                AddAudit(entity.Id, actor, "Imported", publish ? "Imported as Published." : "Imported as Draft.");
                imported.Add(seed.Slug);
            }
            await db.SaveChangesAsync(token);
            return new CrosswordImportResult(imported, skipped);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CrosswordAuditItem>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await db.CrosswordAuditLogs.AsNoTracking().Where(log => log.CrosswordId == id).ToListAsync(cancellationToken);
        return rows.OrderByDescending(log => log.CreatedAt).Select(log =>
            new CrosswordAuditItem(log.Id, log.Actor, log.Action, log.CreatedAt, log.Summary)).ToArray();
    }

    private void AddAudit(Guid id, string actor, string action, string summary) => db.CrosswordAuditLogs.Add(new()
    {
        Id = Guid.NewGuid(),
        CrosswordId = id,
        Actor = actor,
        Action = action,
        Summary = summary,
        CreatedAt = clock.GetUtcNow()
    });
}

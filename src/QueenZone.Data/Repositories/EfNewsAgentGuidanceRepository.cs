using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfNewsAgentGuidanceRepository(QueenZoneDbContext dbContext) : INewsAgentGuidanceRepository
{
    public async Task<NewsAgentGuidanceRevision?> GetPublishedAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.NewsAgentGuidanceRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Published,
                cancellationToken);
        return Map(entity);
    }

    public async Task<NewsAgentGuidanceRevision?> GetDraftAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.NewsAgentGuidanceRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft,
                cancellationToken);
        return Map(entity);
    }

    public async Task<NewsAgentGuidanceRevision?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.NewsAgentGuidanceRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<NewsAgentGuidanceRevision>> ListHistoryAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.NewsAgentGuidanceRevisions
            .AsNoTracking()
            .Where(item => item.Type == type)
            .OrderByDescending(item => item.RevisionNumber)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(item => Map(item)!).ToList();
    }

    public async Task<NewsAgentGuidanceRevision> SaveDraftAsync(
        NewsAgentGuidanceType type,
        string content,
        string editorEmail,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!NewsAgentGuidanceText.TryValidate(content, out var sanitized, out var error))
        {
            throw new NewsAgentGuidanceValidationException(error!);
        }

        var email = NormalizeEmail(editorEmail);
        var hash = NewsAgentGuidanceText.ComputeContentHash(sanitized);
        var draft = await dbContext.NewsAgentGuidanceRevisions
            .SingleOrDefaultAsync(
                item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft,
                cancellationToken);

        if (draft is null)
        {
            draft = new NewsAgentGuidanceRevisionEntity
            {
                Type = type,
                RevisionNumber = await NextRevisionNumberAsync(type, cancellationToken),
                Content = sanitized,
                ContentHash = hash,
                Status = NewsAgentGuidanceStatus.Draft,
                CreatedAt = DateTime.UtcNow,
                CreatedByEmail = email
            };
            AssignRowVersionIfNeeded(draft);
            dbContext.NewsAgentGuidanceRevisions.Add(draft);
        }
        else
        {
            QueenZoneConcurrency.EnsureRequiredRowVersion<NewsAgentGuidanceConcurrencyException>(draft.RowVersion, expectedRowVersion);
            draft.Content = sanitized;
            draft.ContentHash = hash;
            draft.CreatedByEmail = email;
            AssignRowVersionIfNeeded(draft);
        }

        await SaveChangesAsync(cancellationToken);
        return Map(draft)!;
    }

    public async Task<NewsAgentGuidanceRevision> PublishDraftAsync(
        NewsAgentGuidanceType type,
        string publisherEmail,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(publisherEmail);
        // Explicit transactions must run through the configured execution strategy so Azure SQL
        // can retry the entire unit of work rather than rejecting a user-started transaction.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var draft = await dbContext.NewsAgentGuidanceRevisions
                    .SingleOrDefaultAsync(
                        item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft,
                        cancellationToken)
                    ?? throw new InvalidOperationException($"No draft guidance exists for {NewsAgentGuidanceText.ToStorageType(type)}.");

                QueenZoneConcurrency.EnsureRequiredRowVersion<NewsAgentGuidanceConcurrencyException>(draft.RowVersion, expectedRowVersion);

                var published = await dbContext.NewsAgentGuidanceRevisions
                    .SingleOrDefaultAsync(
                        item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Published,
                        cancellationToken);
                if (published is not null)
                {
                    published.Status = NewsAgentGuidanceStatus.Superseded;
                }

                var now = DateTime.UtcNow;
                draft.Status = NewsAgentGuidanceStatus.Published;
                draft.PublishedAt = now;
                draft.PublishedByEmail = email;
                AssignRowVersionIfNeeded(draft);

                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Map(draft)!;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<NewsAgentGuidanceRevision> RollbackAsync(
        NewsAgentGuidanceType type,
        int sourceRevisionId,
        string publisherEmail,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.NewsAgentGuidanceRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sourceRevisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Guidance revision {sourceRevisionId} was not found.");
        if (source.Type != type)
        {
            throw new InvalidOperationException("The selected revision does not match the guidance type.");
        }

        return await PublishNewRevisionAsync(
            type,
            source.Content,
            source.ContentHash,
            publisherEmail,
            cancellationToken);
    }

    public Task<NewsAgentGuidanceRevision> RestoreCompiledDefaultAsync(
        NewsAgentGuidanceType type,
        string publisherEmail,
        CancellationToken cancellationToken = default)
    {
        var content = string.Empty;
        return PublishNewRevisionAsync(
            type,
            content,
            NewsAgentGuidanceText.ComputeContentHash(content),
            publisherEmail,
            cancellationToken);
    }

    private async Task<NewsAgentGuidanceRevision> PublishNewRevisionAsync(
        NewsAgentGuidanceType type,
        string sanitizedContent,
        string contentHash,
        string publisherEmail,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(publisherEmail);
        // Explicit transactions must run through the configured execution strategy so Azure SQL
        // can retry the entire unit of work rather than rejecting a user-started transaction.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var published = await dbContext.NewsAgentGuidanceRevisions
                    .SingleOrDefaultAsync(
                        item => item.Type == type && item.Status == NewsAgentGuidanceStatus.Published,
                        cancellationToken);
                if (published is not null)
                {
                    published.Status = NewsAgentGuidanceStatus.Superseded;
                }

                var now = DateTime.UtcNow;
                var created = new NewsAgentGuidanceRevisionEntity
                {
                    Type = type,
                    RevisionNumber = await NextRevisionNumberAsync(type, cancellationToken),
                    Content = sanitizedContent,
                    ContentHash = contentHash,
                    Status = NewsAgentGuidanceStatus.Published,
                    CreatedAt = now,
                    CreatedByEmail = email,
                    PublishedAt = now,
                    PublishedByEmail = email
                };
                AssignRowVersionIfNeeded(created);
                dbContext.NewsAgentGuidanceRevisions.Add(created);

                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Map(created)!;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task<int> NextRevisionNumberAsync(NewsAgentGuidanceType type, CancellationToken cancellationToken)
    {
        var currentMax = await dbContext.NewsAgentGuidanceRevisions
            .Where(item => item.Type == type)
            .Select(item => (int?)item.RevisionNumber)
            .MaxAsync(cancellationToken);
        return (currentMax ?? 0) + 1;
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new NewsAgentGuidanceConcurrencyException();
        }
    }

    private void AssignRowVersionIfNeeded(NewsAgentGuidanceRevisionEntity entity)
    {
        if (!dbContext.Database.IsSqlServer())
        {
            entity.RowVersion = Guid.NewGuid().ToByteArray();
        }
    }

    private static string NormalizeEmail(string email)
    {
        var trimmed = email.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new NewsAgentGuidanceValidationException("Editor email is required.");
        }

        return trimmed;
    }

    private static NewsAgentGuidanceRevision? Map(NewsAgentGuidanceRevisionEntity? entity) =>
        entity is null
            ? null
            : new NewsAgentGuidanceRevision(
                entity.Id,
                entity.Type,
                entity.RevisionNumber,
                entity.Content,
                entity.ContentHash,
                entity.Status,
                entity.CreatedAt,
                entity.CreatedByEmail,
                entity.PublishedAt,
                entity.PublishedByEmail,
                entity.RowVersion);
}

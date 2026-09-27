using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class SharedNewsAgentGuidanceStore
{
    private readonly object sync = new();
    private readonly List<NewsAgentGuidanceRevisionEntity> revisions = [];
    private int nextId = 1;

    public NewsAgentGuidanceRevisionEntity? GetPublished(NewsAgentGuidanceType type)
    {
        lock (sync)
        {
            return revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Published);
        }
    }

    public NewsAgentGuidanceRevisionEntity? GetDraft(NewsAgentGuidanceType type)
    {
        lock (sync)
        {
            return revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft);
        }
    }

    public NewsAgentGuidanceRevisionEntity? GetById(int id)
    {
        lock (sync)
        {
            return revisions.SingleOrDefault(item => item.Id == id);
        }
    }

    public IReadOnlyList<NewsAgentGuidanceRevisionEntity> ListHistory(NewsAgentGuidanceType type)
    {
        lock (sync)
        {
            return revisions
                .Where(item => item.Type == type)
                .OrderByDescending(item => item.RevisionNumber)
                .ThenByDescending(item => item.Id)
                .Select(Clone)
                .ToList();
        }
    }

    public NewsAgentGuidanceRevisionEntity SaveDraft(
        NewsAgentGuidanceType type,
        string sanitizedContent,
        string contentHash,
        string editorEmail,
        byte[]? expectedRowVersion)
    {
        lock (sync)
        {
            var draft = revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft);
            if (draft is null)
            {
                draft = new NewsAgentGuidanceRevisionEntity
                {
                    Id = nextId++,
                    Type = type,
                    RevisionNumber = NextRevisionNumber(type),
                    Content = sanitizedContent,
                    ContentHash = contentHash,
                    Status = NewsAgentGuidanceStatus.Draft,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByEmail = editorEmail,
                    RowVersion = NextRowVersion()
                };
                revisions.Add(draft);
                return Clone(draft);
            }

            QueenZoneConcurrency.EnsureRequiredRowVersion<NewsAgentGuidanceConcurrencyException>(draft.RowVersion, expectedRowVersion);
            draft.Content = sanitizedContent;
            draft.ContentHash = contentHash;
            draft.CreatedByEmail = editorEmail;
            draft.RowVersion = NextRowVersion();
            return Clone(draft);
        }
    }

    public NewsAgentGuidanceRevisionEntity PublishDraft(
        NewsAgentGuidanceType type,
        string publisherEmail,
        byte[] expectedRowVersion)
    {
        lock (sync)
        {
            var draft = revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Draft)
                ?? throw new InvalidOperationException($"No draft guidance exists for {NewsAgentGuidanceText.ToStorageType(type)}.");

            QueenZoneConcurrency.EnsureRequiredRowVersion<NewsAgentGuidanceConcurrencyException>(draft.RowVersion, expectedRowVersion);

            var published = revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Published);
            if (published is not null)
            {
                published.Status = NewsAgentGuidanceStatus.Superseded;
            }

            var now = DateTime.UtcNow;
            draft.Status = NewsAgentGuidanceStatus.Published;
            draft.PublishedAt = now;
            draft.PublishedByEmail = publisherEmail;
            draft.RowVersion = NextRowVersion();
            return Clone(draft);
        }
    }

    public NewsAgentGuidanceRevisionEntity PublishNewRevision(
        NewsAgentGuidanceType type,
        string sanitizedContent,
        string contentHash,
        string publisherEmail)
    {
        lock (sync)
        {
            var published = revisions.SingleOrDefault(item =>
                item.Type == type && item.Status == NewsAgentGuidanceStatus.Published);
            if (published is not null)
            {
                published.Status = NewsAgentGuidanceStatus.Superseded;
            }

            var now = DateTime.UtcNow;
            var created = new NewsAgentGuidanceRevisionEntity
            {
                Id = nextId++,
                Type = type,
                RevisionNumber = NextRevisionNumber(type),
                Content = sanitizedContent,
                ContentHash = contentHash,
                Status = NewsAgentGuidanceStatus.Published,
                CreatedAt = now,
                CreatedByEmail = publisherEmail,
                PublishedAt = now,
                PublishedByEmail = publisherEmail,
                RowVersion = NextRowVersion()
            };
            revisions.Add(created);
            return Clone(created);
        }
    }

    private int NextRevisionNumber(NewsAgentGuidanceType type) =>
        revisions.Where(item => item.Type == type).Select(item => item.RevisionNumber).DefaultIfEmpty(0).Max() + 1;

    private static byte[] NextRowVersion() => Guid.NewGuid().ToByteArray();

    private static NewsAgentGuidanceRevisionEntity Clone(NewsAgentGuidanceRevisionEntity entity) =>
        new()
        {
            Id = entity.Id,
            Type = entity.Type,
            RevisionNumber = entity.RevisionNumber,
            Content = entity.Content,
            ContentHash = entity.ContentHash,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            CreatedByEmail = entity.CreatedByEmail,
            PublishedAt = entity.PublishedAt,
            PublishedByEmail = entity.PublishedByEmail,
            RowVersion = [.. entity.RowVersion]
        };
}

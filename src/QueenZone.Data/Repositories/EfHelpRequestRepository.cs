using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfHelpRequestRepository(QueenZoneDbContext dbContext) : IHelpRequestRepository
{
    public async Task<HelpRequest> CreateAsync(
        HelpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = new HelpRequestEntity
        {
            Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            Topic = HelpRequestTopic.Normalize(request.Topic),
            Subject = RequireTrimmed(request.Subject, 200),
            Message = RequireTrimmed(request.Message, 4000),
            Name = RequireTrimmed(request.Name, 100),
            Email = RequireTrimmed(request.Email, 256),
            NormalizedEmail = NormalizeEmail(request.NormalizedEmail, request.Email),
            MemberId = request.MemberId,
            Status = HelpRequestStatus.Open,
            SubmittedAt = request.SubmittedAt == default ? DateTimeOffset.UtcNow : request.SubmittedAt,
        };

        dbContext.HelpRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<HelpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HelpRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<HelpRequestListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var statusFilter = NormalizeOptionalStatus(status);

        var query = dbContext.HelpRequests.AsNoTracking();
        if (statusFilter is not null)
        {
            query = query.Where(row => row.Status == statusFilter);
        }

        if (IsSqliteDatabase())
        {
            var allRows = await query
                .Select(row => new
                {
                    row.Id,
                    row.Topic,
                    row.Subject,
                    row.Name,
                    row.Email,
                    row.MemberId,
                    row.Status,
                    row.SubmittedAt,
                })
                .ToListAsync(cancellationToken);

            var ordered = allRows.OrderByDescending(row => row.SubmittedAt).ThenBy(row => row.Id).ToList();
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new HelpRequestListItem(
                    row.Id,
                    row.Topic,
                    row.Subject,
                    row.Name,
                    row.Email,
                    row.MemberId,
                    row.Status,
                    row.SubmittedAt))
                .ToList();

            return new HelpRequestListPage(items, ordered.Count, statusFilter);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageItems = await query
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new HelpRequestListItem(
                row.Id,
                row.Topic,
                row.Subject,
                row.Name,
                row.Email,
                row.MemberId,
                row.Status,
                row.SubmittedAt))
            .ToListAsync(cancellationToken);

        return new HelpRequestListPage(pageItems, totalCount, statusFilter);
    }

    public async Task<HelpRequest?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HelpRequests
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Status = HelpRequestStatus.Normalize(status);
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(notes, 500);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<int> CountByEmailSinceAsync(
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeEmail(normalizedEmail, normalizedEmail);
        var rows = await dbContext.HelpRequests
            .AsNoTracking()
            .Where(row => row.NormalizedEmail == key)
            .Select(row => row.SubmittedAt)
            .ToListAsync(cancellationToken);

        return rows.Count(submittedAt => submittedAt >= sinceUtc);
    }

    public async Task<int> CountByMemberSinceAsync(
        Guid memberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.HelpRequests
            .AsNoTracking()
            .Where(row => row.MemberId == memberId)
            .Select(row => row.SubmittedAt)
            .ToListAsync(cancellationToken);

        return rows.Count(submittedAt => submittedAt >= sinceUtc);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.HelpRequests
            .AsNoTracking()
            .CountAsync(
                row => row.Status == HelpRequestStatus.Open
                    || row.Status == HelpRequestStatus.InProgress,
                cancellationToken);
    }

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);

    internal static string NormalizeEmail(string? normalizedEmail, string email)
    {
        var source = string.IsNullOrWhiteSpace(normalizedEmail) ? email : normalizedEmail;
        return source.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return HelpRequestStatus.Normalize(status);
    }

    private static string RequireTrimmed(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static HelpRequest Map(HelpRequestEntity entity) =>
        new(
            entity.Id,
            entity.Topic,
            entity.Subject,
            entity.Message,
            entity.Name,
            entity.Email,
            entity.NormalizedEmail,
            entity.MemberId,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes);
}

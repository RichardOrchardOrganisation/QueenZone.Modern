using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryHelpRequestRepository : IHelpRequestRepository
{
    private readonly object sync = new();
    private readonly List<HelpRequestEntity> requests = [];

    public Task<HelpRequest> CreateAsync(
        HelpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (sync)
        {
            var entity = new HelpRequestEntity
            {
                Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
                Topic = HelpRequestTopic.Normalize(request.Topic),
                Subject = RequireTrimmed(request.Subject, 200),
                Message = RequireTrimmed(request.Message, 4000),
                Name = RequireTrimmed(request.Name, 100),
                Email = RequireTrimmed(request.Email, 256),
                NormalizedEmail = EfHelpRequestRepository.NormalizeEmail(request.NormalizedEmail, request.Email),
                MemberId = request.MemberId,
                Status = HelpRequestStatus.Open,
                SubmittedAt = request.SubmittedAt == default ? DateTimeOffset.UtcNow : request.SubmittedAt,
            };

            requests.Add(entity);
            return Task.FromResult(Map(entity));
        }
    }

    public Task<HelpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = requests.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<HelpRequestListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var statusFilter = NormalizeOptionalStatus(status);

        lock (sync)
        {
            var filtered = requests
                .Where(row => statusFilter is null || row.Status == statusFilter)
                .OrderByDescending(row => row.SubmittedAt)
                .ToList();

            IReadOnlyList<HelpRequestListItem> items = filtered
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

            return Task.FromResult(new HelpRequestListPage(items, filtered.Count, statusFilter));
        }
    }

    public Task<HelpRequest?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = requests.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<HelpRequest?>(null);
            }

            entity.Status = HelpRequestStatus.Normalize(status);
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
            entity.ReviewNotes = SubmissionInput.NormalizeOptional(notes, 500);

            return Task.FromResult<HelpRequest?>(Map(entity));
        }
    }

    public Task<int> CountByEmailSinceAsync(
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var key = EfHelpRequestRepository.NormalizeEmail(normalizedEmail, normalizedEmail);
        lock (sync)
        {
            var count = requests.Count(row =>
                row.NormalizedEmail == key && row.SubmittedAt >= sinceUtc);
            return Task.FromResult(count);
        }
    }

    public Task<int> CountByMemberSinceAsync(
        Guid memberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var count = requests.Count(row =>
                row.MemberId == memberId && row.SubmittedAt >= sinceUtc);
            return Task.FromResult(count);
        }
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var count = requests.Count(row => HelpRequestStatus.IsOpenQueue(row.Status));
            return Task.FromResult(count);
        }
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

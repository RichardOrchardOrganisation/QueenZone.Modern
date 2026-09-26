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
            var entity = HelpRequestRecords.NewEntity(request);

            requests.Add(entity);
            return Task.FromResult(HelpRequestRecords.Map(entity));
        }
    }

    public Task<HelpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = requests.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : HelpRequestRecords.Map(entity));
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
        var statusFilter = HelpRequestRecords.NormalizeStatusFilter(status);

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

            HelpRequestRecords.ApplyStatus(entity, status, reviewerEmail, notes);

            return Task.FromResult<HelpRequest?>(HelpRequestRecords.Map(entity));
        }
    }

    public Task<int> CountByEmailSinceAsync(
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var key = HelpRequestRecords.NormalizeEmail(normalizedEmail, normalizedEmail);
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
}

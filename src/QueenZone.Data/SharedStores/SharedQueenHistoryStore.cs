namespace QueenZone.Data;

public sealed class SharedQueenHistoryStore
{
    private readonly object sync = new();
    private readonly List<QueenHistoryEvent> events = [];
    private int nextId = 1;

    public SharedQueenHistoryStore()
    {
    }

    public SharedQueenHistoryStore(IEnumerable<QueenHistoryEvent> seedEvents)
    {
        lock (sync)
        {
            events.AddRange(seedEvents);
            nextId = events.Count == 0 ? 1 : events.Max(item => item.Id) + 1;
        }
    }

    public IReadOnlyList<QueenHistoryEvent> GetAll()
    {
        lock (sync)
        {
            return events.ToList();
        }
    }

    public AdminQueenHistoryPage GetPage(AdminQueenHistoryListFilter filter, int page, int pageSize)
    {
        lock (sync)
        {
            IEnumerable<QueenHistoryEvent> query = events;
            if (filter.IsPublished is bool isPublished)
            {
                query = query.Where(item => item.IsPublished == isPublished);
            }

            if (!string.IsNullOrWhiteSpace(filter.Query))
            {
                var needle = filter.Query.Trim();
                query = query.Where(item =>
                    item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || item.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            var sorted = query
                .OrderByDescending(item => item.EventDate)
                .ThenByDescending(item => item.Importance)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var safePage = Math.Max(page, 1);
            var safePageSize = Math.Clamp(pageSize, 1, 100);
            var items = sorted
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToList();

            return new AdminQueenHistoryPage(items, sorted.Count, safePage, safePageSize);
        }
    }

    public QueenHistoryEvent? GetById(int id)
    {
        lock (sync)
        {
            return events.SingleOrDefault(item => item.Id == id);
        }
    }

    public int Create(AdminQueenHistoryDraft draft)
    {
        lock (sync)
        {
            var id = nextId++;
            events.Add(new QueenHistoryEvent(
                id,
                draft.Title,
                draft.Summary,
                draft.EventDate,
                draft.DatePrecision,
                draft.Category,
                draft.Importance,
                QueenHistoryEventSourceType.Curated,
                $"curated:{Guid.NewGuid():N}",
                draft.SourceUrl,
                draft.IsPublished,
                QueenZoneConcurrency.NewClientRowVersion()));
            return id;
        }
    }

    public bool Update(int id, AdminQueenHistoryDraft draft, byte[]? expectedRowVersion = null)
    {
        lock (sync)
        {
            var index = events.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            QueenZoneConcurrency.EnsureRowVersion(events[index].RowVersion, expectedRowVersion);
            events[index] = events[index] with
            {
                Title = draft.Title,
                Summary = draft.Summary,
                EventDate = draft.EventDate,
                DatePrecision = draft.DatePrecision,
                Category = draft.Category,
                Importance = draft.Importance,
                SourceUrl = draft.SourceUrl,
                IsPublished = draft.IsPublished,
                RowVersion = QueenZoneConcurrency.NewClientRowVersion(),
            };
            return true;
        }
    }

    public bool Delete(int id, byte[]? expectedRowVersion = null)
    {
        lock (sync)
        {
            var existing = events.SingleOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return false;
            }

            QueenZoneConcurrency.EnsureRowVersion(existing.RowVersion, expectedRowVersion);
            events.Remove(existing);
            return true;
        }
    }

    public bool SetPublished(int id, bool isPublished, byte[]? expectedRowVersion = null)
    {
        lock (sync)
        {
            var index = events.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            QueenZoneConcurrency.EnsureRowVersion(events[index].RowVersion, expectedRowVersion);
            events[index] = events[index] with
            {
                IsPublished = isPublished,
                RowVersion = QueenZoneConcurrency.NewClientRowVersion(),
            };
            return true;
        }
    }
}

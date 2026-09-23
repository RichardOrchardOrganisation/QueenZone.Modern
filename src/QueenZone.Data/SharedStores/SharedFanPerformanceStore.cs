namespace QueenZone.Data;

public sealed class SharedFanPerformanceStore
{
    private readonly object sync = new();
    private readonly List<MutablePerformance> performances = [];
    private int nextId = 1;

    public SharedFanPerformanceStore()
    {
    }

    public SharedFanPerformanceStore(IEnumerable<FanPerformance> seedPerformances)
    {
        lock (sync)
        {
            foreach (var performance in seedPerformances)
            {
                performances.Add(ToMutable(performance, isVisible: true));
            }

            if (performances.Count > 0)
            {
                nextId = performances.Max(performance => performance.Id) + 1;
            }
        }
    }

    public IReadOnlyList<FanPerformance> GetVisible()
    {
        lock (sync)
        {
            return performances
                .Where(performance => performance.IsVisible)
                .OrderByDescending(performance => performance.DateAdded)
                .ThenByDescending(performance => performance.Id)
                .Select(ToPublic)
                .ToList();
        }
    }

    public FanPerformance? GetVisibleById(int id)
    {
        lock (sync)
        {
            var performance = performances.SingleOrDefault(item => item.Id == id && item.IsVisible);
            return performance is null ? null : ToPublic(performance);
        }
    }

    public IReadOnlyList<AdminFanPerformanceItem> GetAdminItems(AdminFanPerformanceListFilter filter)
    {
        lock (sync)
        {
            return performances
                .Select(ToAdminItem)
                .Where(item => Matches(item, filter))
                .OrderByDescending(item => item.DateAdded)
                .ThenByDescending(item => item.Id)
                .ToList();
        }
    }

    public AdminFanPerformanceItem? GetAdminItem(int id)
    {
        lock (sync)
        {
            var performance = performances.SingleOrDefault(item => item.Id == id);
            return performance is null ? null : ToAdminItem(performance);
        }
    }

    public int Create(AdminFanPerformanceCreateRequest request, string editorEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        lock (sync)
        {
            var id = nextId++;
            performances.Add(new MutablePerformance(
                id,
                request.Title.Trim(),
                request.PerformedBy.Trim(),
                NormalizeDescription(request.Description),
                request.AudioFileName.Trim(),
                request.FileSizeBytes,
                request.DateAdded,
                request.IsVisible,
                request.DurationSeconds));
            return id;
        }
    }

    public bool Update(
        int id,
        AdminFanPerformanceUpdateRequest request,
        string editorEmail,
        AdminFanPerformanceConcurrencyToken? expected = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        lock (sync)
        {
            var index = performances.FindIndex(performance => performance.Id == id);
            if (index < 0)
            {
                return false;
            }

            var existing = performances[index];
            if (expected is not null && !Matches(existing, expected))
            {
                throw new OptimisticConcurrencyException();
            }

            performances[index] = existing with
            {
                Title = request.Title.Trim(),
                PerformedBy = request.PerformedBy.Trim(),
                Description = NormalizeDescription(request.Description),
                DateAdded = request.DateAdded,
            };
            return true;
        }
    }

    public bool SetVisibility(int id, bool isVisible, string editorEmail, bool? expectedIsVisible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        lock (sync)
        {
            var index = performances.FindIndex(performance => performance.Id == id);
            if (index < 0)
            {
                return false;
            }

            if (expectedIsVisible is bool expected && performances[index].IsVisible != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            performances[index] = performances[index] with { IsVisible = isVisible };
            return true;
        }
    }

    private static bool Matches(MutablePerformance existing, AdminFanPerformanceConcurrencyToken expected) =>
        string.Equals(existing.Title, expected.Title.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.PerformedBy, expected.PerformedBy.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.Description, NormalizeDescription(expected.Description), StringComparison.Ordinal)
        && existing.DateAdded == expected.DateAdded
        && existing.IsVisible == expected.IsVisible;

    private static bool Matches(AdminFanPerformanceItem item, AdminFanPerformanceListFilter filter)
    {
        if (filter.IsVisible is bool isVisible && item.IsVisible != isVisible)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var inTitle = item.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var inPerformedBy = item.PerformedBy.Contains(term, StringComparison.OrdinalIgnoreCase);
            var inDescription = item.Description.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (!inTitle && !inPerformedBy && !inDescription)
            {
                return false;
            }
        }

        return true;
    }

    private static AdminFanPerformanceItem ToAdminItem(MutablePerformance performance) =>
        new(
            performance.Id,
            performance.Title,
            performance.PerformedBy,
            performance.Description,
            performance.AudioFileName,
            performance.FileSizeBytes,
            performance.DateAdded,
            performance.IsVisible,
            performance.DurationSeconds);

    private static FanPerformance ToPublic(MutablePerformance performance) =>
        new(
            performance.Id,
            performance.Title,
            performance.PerformedBy,
            performance.Description,
            performance.AudioFileName,
            performance.FileSizeBytes,
            performance.DateAdded,
            performance.DurationSeconds);

    private static MutablePerformance ToMutable(FanPerformance performance, bool isVisible) =>
        new(
            performance.Id,
            performance.Title,
            performance.PerformedBy,
            performance.Description,
            performance.AudioFileName,
            performance.FileSizeBytes,
            performance.DateAdded,
            isVisible,
            performance.DurationSeconds);

    private static string NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();

    private sealed record MutablePerformance(
        int Id,
        string Title,
        string PerformedBy,
        string Description,
        string AudioFileName,
        long FileSizeBytes,
        DateTime DateAdded,
        bool IsVisible,
        int? DurationSeconds);
}

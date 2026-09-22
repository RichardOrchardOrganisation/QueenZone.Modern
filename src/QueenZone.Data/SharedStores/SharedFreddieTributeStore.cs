namespace QueenZone.Data;

public sealed class SharedFreddieTributeStore(IEnumerable<FreddieTribute> seedTributes)
{
    private readonly object gate = new();
    private readonly List<TributeState> tributes = seedTributes
        .Select(tribute => new TributeState(tribute, true))
        .ToList();

    public FreddieTributePage GetPublicPage(int page, int pageSize)
    {
        var items = GetVisibleTributes();
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var pageItems = items
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return new FreddieTributePage(pageItems, items.Count);
    }

    public FreddieTribute? GetRandomPublicTribute()
    {
        var id = PickRandomVisibleId();
        return id is int value ? GetVisibleById(value) : null;
    }

    public int? PickRandomVisibleId()
    {
        var items = GetVisibleTributes();
        return items.Count == 0 ? null : items[Random.Shared.Next(items.Count)].Id;
    }

    public FreddieTribute? GetVisibleById(int id)
    {
        lock (gate)
        {
            var state = tributes.FirstOrDefault(item => item.Tribute.Id == id && item.IsVisible);
            if (state is null || string.IsNullOrWhiteSpace(state.Tribute.Thought))
            {
                return null;
            }

            return state.Tribute;
        }
    }

    public AdminFreddieTributePage GetAdminPage(
        AdminFreddieTributeListFilter filter,
        int page,
        int pageSize)
    {
        var items = GetAdminItems(filter);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var pageItems = items
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return new AdminFreddieTributePage(pageItems, items.Count, safePage, safePageSize);
    }

    public AdminFreddieTributeItem? GetById(int id)
    {
        lock (gate)
        {
            return BuildAdminItems(tributes).FirstOrDefault(tribute => tribute.Id == id);
        }
    }

    public bool SetVisibility(int id, bool isVisible, bool? expectedIsVisible = null)
    {
        lock (gate)
        {
            var tribute = tributes.FirstOrDefault(item => item.Tribute.Id == id);
            if (tribute is null)
            {
                return false;
            }

            if (expectedIsVisible is bool expected && tribute.IsVisible != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            tribute.IsVisible = isVisible;
            return true;
        }
    }

    public bool Delete(int id, bool? expectedIsVisible = null)
    {
        lock (gate)
        {
            var index = tributes.FindIndex(item => item.Tribute.Id == id);
            if (index < 0)
            {
                return false;
            }

            if (expectedIsVisible is bool expected && tributes[index].IsVisible != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            tributes.RemoveAt(index);
            return true;
        }
    }

    private IReadOnlyList<FreddieTribute> GetVisibleTributes()
    {
        lock (gate)
        {
            return tributes
                .Where(item => item.IsVisible && !string.IsNullOrWhiteSpace(item.Tribute.Thought))
                .Select(item => item.Tribute)
                .OrderByDescending(tribute => tribute.Id)
                .ToList();
        }
    }

    private IReadOnlyList<AdminFreddieTributeItem> GetAdminItems(AdminFreddieTributeListFilter filter)
    {
        lock (gate)
        {
            var items = BuildAdminItems(tributes).AsEnumerable();
            if (filter.IsVisible is bool isVisible)
            {
                items = items.Where(item => item.IsVisible == isVisible);
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                items = items.Where(item =>
                    item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.Thought.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (item.Country?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (filter.DuplicatesOnly)
            {
                items = items.Where(item => item.DuplicateCount > 1);
            }

            return items.OrderByDescending(item => item.Id).ToList();
        }
    }

    private static IReadOnlyList<AdminFreddieTributeItem> BuildAdminItems(IEnumerable<TributeState> states)
    {
        var stateList = states.ToList();
        var duplicateCounts = stateList
            .GroupBy(item => DuplicateKey(item.Tribute), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return stateList
            .Select(item => new AdminFreddieTributeItem(
                item.Tribute.Id,
                item.Tribute.Name,
                item.Tribute.Thought,
                item.Tribute.Country,
                item.Tribute.DateText,
                item.Tribute.TimeText,
                item.IsVisible,
                duplicateCounts[DuplicateKey(item.Tribute)]))
            .ToList();
    }

    private static string DuplicateKey(FreddieTribute tribute) =>
        $"{tribute.Name.Trim().ToUpperInvariant()}\u001f{tribute.Thought.Trim().ToUpperInvariant()}";

    private sealed class TributeState(FreddieTribute tribute, bool isVisible)
    {
        public FreddieTribute Tribute { get; } = tribute;

        public bool IsVisible { get; set; } = isVisible;
    }
}


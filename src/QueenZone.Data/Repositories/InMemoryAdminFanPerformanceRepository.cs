namespace QueenZone.Data;

public sealed class InMemoryAdminFanPerformanceRepository(SharedFanPerformanceStore store)
    : IAdminFanPerformanceRepository
{
    public Task<AdminFanPerformancePage> GetPageAsync(
        AdminFanPerformanceListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = store.GetAdminItems(filter);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var items = all
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return Task.FromResult(new AdminFanPerformancePage(items, all.Count, safePage, safePageSize));
    }

    public Task<AdminFanPerformanceItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetAdminItem(id));

    public Task<int> CreateAsync(
        AdminFanPerformanceCreateRequest request,
        string editorEmail,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Create(request, editorEmail));

    public Task UpdateAsync(
        int id,
        AdminFanPerformanceUpdateRequest request,
        string editorEmail,
        AdminFanPerformanceConcurrencyToken? expected = null,
        CancellationToken cancellationToken = default)
    {
        if (!store.Update(id, request, editorEmail, expected))
        {
            throw new InvalidOperationException($"Fan performance {id} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task SetVisibilityAsync(
        int id,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        if (!store.SetVisibility(id, isVisible, editorEmail, expectedIsVisible))
        {
            throw new InvalidOperationException($"Fan performance {id} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, string editorEmail, CancellationToken cancellationToken = default)
    {
        store.Delete(id, editorEmail);
        return Task.CompletedTask;
    }
}

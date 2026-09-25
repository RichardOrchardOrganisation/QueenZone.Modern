namespace QueenZone.Data;

public interface IAdminFanPerformanceRepository
{
    Task<AdminFanPerformancePage> GetPageAsync(
        AdminFanPerformanceListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AdminFanPerformanceItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<int> CreateAsync(
        AdminFanPerformanceCreateRequest request,
        string editorEmail,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        int id,
        AdminFanPerformanceUpdateRequest request,
        string editorEmail,
        AdminFanPerformanceConcurrencyToken? expected = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>DISPLAY</c> on <c>Q_STAGE_T</c>. Hide (<paramref name="isVisible"/> = false)
    /// does not delete the <c>songfiles</c> blob.
    /// </summary>
    Task SetVisibilityAsync(
        int id,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, string editorEmail, CancellationToken cancellationToken = default);
}

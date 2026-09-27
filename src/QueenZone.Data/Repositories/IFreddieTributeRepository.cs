namespace QueenZone.Data;

public interface IFreddieTributeRepository
{
    Task<FreddieTributePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<FreddieTribute?> GetRandomAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks one visible tribute id by seeking a random id in the indexed min/max range.
    /// </summary>
    Task<int?> PickRandomVisibleIdAsync(CancellationToken cancellationToken = default);

    Task<FreddieTribute?> GetVisibleByIdAsync(int id, CancellationToken cancellationToken = default);
}


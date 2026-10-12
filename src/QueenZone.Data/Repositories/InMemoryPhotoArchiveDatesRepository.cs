namespace QueenZone.Data;

public sealed class InMemoryPhotoArchiveDatesRepository(SharedPhotoStore store) : IPhotoArchiveDatesRepository
{
    private readonly object sync = new();
    private readonly Dictionary<int, PhotoArchiveDates> stored = [];

    public Task<PhotoArchiveDates> GetAsync(int picId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(stored.GetValueOrDefault(picId, PhotoArchiveDates.Unknown));
        }
    }

    public Task SetAsync(
        int picId,
        PhotoArchiveDates dates,
        PhotoArchiveDates expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(expected);

        lock (sync)
        {
            if (store.GetPhoto(picId) is null)
            {
                throw new InvalidOperationException($"Photo {picId} was not found.");
            }

            if (stored.GetValueOrDefault(picId, PhotoArchiveDates.Unknown) != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            stored[picId] = dates;
            return Task.CompletedTask;
        }
    }
}

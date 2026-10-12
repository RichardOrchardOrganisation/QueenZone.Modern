namespace QueenZone.Data;

public sealed class InMemoryPhotoArchiveDatesRepository(SharedPhotoStore store) : IPhotoArchiveDatesRepository
{
    private readonly object sync = new();
    private readonly Dictionary<int, PhotoArchiveDates> dates = [];

    public Task<PhotoArchiveDates> GetAsync(int picId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(dates.GetValueOrDefault(picId, PhotoArchiveDates.Unknown));
        }
    }

    public Task SetAsync(
        int picId,
        PhotoArchiveDates newDates,
        PhotoArchiveDates expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newDates);
        ArgumentNullException.ThrowIfNull(expected);

        lock (sync)
        {
            if (store.GetPhoto(picId) is null)
            {
                throw new InvalidOperationException($"Photo {picId} was not found.");
            }

            if (dates.GetValueOrDefault(picId, PhotoArchiveDates.Unknown) != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            dates[picId] = newDates;
            return Task.CompletedTask;
        }
    }
}

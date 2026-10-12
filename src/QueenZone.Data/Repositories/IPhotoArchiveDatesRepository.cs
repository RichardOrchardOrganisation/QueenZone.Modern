namespace QueenZone.Data;

/// <summary>
/// Event date and photograph date of an archive photo, stored in extra columns on the legacy
/// <c>PIC_FILES_T</c> table. The added-to-archive date is the existing upload timestamp.
/// </summary>
public interface IPhotoArchiveDatesRepository
{
    /// <summary>Both dates for a photo; <see cref="PhotoArchiveDates.Unknown"/> when none are recorded.</summary>
    Task<PhotoArchiveDates> GetAsync(int picId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the dates when they still equal <paramref name="expected"/> (compare-and-swap);
    /// throws <see cref="OptimisticConcurrencyException"/> if someone changed them, and
    /// <see cref="InvalidOperationException"/> if the photo does not exist.
    /// </summary>
    Task SetAsync(
        int picId,
        PhotoArchiveDates dates,
        PhotoArchiveDates expected,
        CancellationToken cancellationToken = default);
}

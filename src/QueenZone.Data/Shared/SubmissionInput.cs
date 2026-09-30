namespace QueenZone.Data;

/// <summary>
/// Input normalisation shared by the EF submission repositories when they create or update a
/// submission row.
/// </summary>
internal static class SubmissionInput
{
    /// <summary>
    /// Trims <paramref name="value"/> and truncates it to <paramref name="maxLength"/>; blank or
    /// whitespace-only input becomes <see langword="null"/>.
    /// </summary>
    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>
    /// The caller's preferred id when one was supplied, otherwise a new one. Lets upload flows name
    /// blobs after the submission before the row exists.
    /// </summary>
    internal static Guid IdOrNew(Guid? preferredId) =>
        preferredId is { } id && id != Guid.Empty ? id : Guid.NewGuid();
}

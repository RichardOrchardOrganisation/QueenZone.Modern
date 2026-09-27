namespace QueenZone.Data;

/// <summary>
/// Legacy SQL Server <c>datetime</c> columns are UTC but come back with an unspecified kind.
/// </summary>
internal static class LegacyDateTime
{
    /// <summary>
    /// Treats <paramref name="value"/> as UTC (never local time, which would shift the timestamp).
    /// A <see langword="null"/> value becomes <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    internal static DateTimeOffset ToOffset(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc));
}

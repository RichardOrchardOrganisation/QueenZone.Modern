namespace QueenZone.Application;

/// <summary>
/// Defense-in-depth periodic sweep for gallery blobs left behind by the narrow crash window
/// between a photo promotion's blob upload and its compensating delete (#590 Option A covers
/// the common failure path; this covers the residual gap, per #651 Option C).
/// </summary>
public sealed class GalleryOrphanSweepOptions
{
    public const string SectionName = "GalleryOrphanSweep";

    /// <summary>When false, the sweep hosted service does nothing each tick.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>When true (the default), orphans are logged but not deleted.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>
    /// Blobs newer than this are never treated as orphans, to avoid racing an in-flight
    /// promotion whose DB write hasn't committed yet.
    /// </summary>
    public int GracePeriodMinutes { get; init; } = 60;
}

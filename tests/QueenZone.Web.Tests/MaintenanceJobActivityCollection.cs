namespace QueenZone.Web.Tests;

/// <summary>
/// Maintenance job tests start the same named activities that the hosted-service tests assert on
/// through a process-wide <see cref="QueenZoneActivityTestListener"/>, so they must not overlap.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MaintenanceJobActivityCollection
{
    public const string Name = "Maintenance job activities";
}

using System.Diagnostics;

namespace QueenZone.Application;

public static class QueenZoneTelemetry
{
    // Preserve the existing source name so deployed telemetry subscriptions keep working.
    public const string ActivitySourceName = "QueenZone.Web";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}

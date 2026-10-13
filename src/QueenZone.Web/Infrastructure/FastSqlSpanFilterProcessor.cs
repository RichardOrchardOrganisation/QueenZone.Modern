using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web;

/// <summary>
/// Drops successful SQL client spans faster than a threshold so they are not exported to
/// Application Insights. Slow and failed queries are kept; slow ones are also logged with
/// their command text by <c>SlowQueryLoggingInterceptor</c>. Must be registered before the
/// exporter processors, because it works by clearing the Recorded trace flag.
/// </summary>
internal sealed class FastSqlSpanFilterProcessor(TimeSpan minDuration) : BaseProcessor<Activity>
{
    internal static TracerProviderBuilder AddTo(TracerProviderBuilder builder, TimeSpan minDuration) =>
        minDuration <= TimeSpan.Zero ? builder : builder.AddProcessor(new FastSqlSpanFilterProcessor(minDuration));

    public override void OnEnd(Activity data)
    {
        if (data.Kind == ActivityKind.Client
            && data.Status != ActivityStatusCode.Error
            && data.Duration < minDuration
            && IsSqlServerSpan(data))
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    private static bool IsSqlServerSpan(Activity activity) =>
        activity.GetTagItem("db.system.name") is "microsoft.sql_server"
        || activity.GetTagItem("db.system") is "mssql";
}

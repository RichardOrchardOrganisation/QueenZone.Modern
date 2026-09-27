using System.Diagnostics;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

internal sealed class QueenZoneActivityTestListener : IDisposable
{
    private readonly ActivityListener listener;

    public List<Activity> Started { get; } = [];

    public List<Activity> Stopped { get; } = [];

    private QueenZoneActivityTestListener(ActivityListener listener)
    {
        this.listener = listener;
    }

    public static QueenZoneActivityTestListener Listen()
    {
        var wrapper = new QueenZoneActivityTestListener(new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QueenZoneTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        });
        wrapper.listener.ActivityStarted = activity => wrapper.Started.Add(activity);
        wrapper.listener.ActivityStopped = activity => wrapper.Stopped.Add(activity);
        ActivitySource.AddActivityListener(wrapper.listener);
        return wrapper;
    }

    public void Dispose() => listener.Dispose();
}

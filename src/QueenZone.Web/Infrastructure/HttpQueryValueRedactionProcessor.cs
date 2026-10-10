using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web;

internal sealed class HttpQueryValueRedactionProcessor : BaseProcessor<Activity>
{
    internal static TracerProviderBuilder AddTo(TracerProviderBuilder builder) =>
        builder.AddProcessor(new HttpQueryValueRedactionProcessor());

    public override void OnEnd(Activity data)
    {
        RedactTag(data, "url.query", queryOnly: true);
        RedactTag(data, "url.full", queryOnly: false);
        RedactTag(data, "http.url", queryOnly: false);
        RedactTag(data, "http.target", queryOnly: false);
    }

    private static void RedactTag(Activity activity, string name, bool queryOnly)
    {
        if (activity.GetTagItem(name) is not string value)
        {
            return;
        }

        var start = queryOnly ? (value.StartsWith('?') ? 1 : 0) : value.IndexOf('?') + 1;
        if (!queryOnly && start == 0)
        {
            return;
        }

        var fragment = value.IndexOf('#', start);
        var end = fragment < 0 ? value.Length : fragment;
        var query = value[start..end];
        var parts = query.Split('&');
        for (var index = 0; index < parts.Length; index++)
        {
            var equals = parts[index].IndexOf('=');
            if (equals >= 0)
            {
                parts[index] = parts[index][..(equals + 1)] + "Redacted";
            }
        }

        activity.SetTag(name, value[..start] + string.Join('&', parts) + value[end..]);
    }
}

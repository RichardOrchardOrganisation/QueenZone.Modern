using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web.Tests;

public sealed class FastSqlSpanFilterProcessorTests
{
    [Theory]
    [InlineData("db.system", "mssql", ActivityKind.Client, false, 0, false)]
    [InlineData("db.system.name", "microsoft.sql_server", ActivityKind.Client, false, 0, false)]
    [InlineData("db.system", "mssql", ActivityKind.Client, false, 300, true)]
    [InlineData("db.system", "mssql", ActivityKind.Client, true, 0, true)]
    [InlineData("db.system", "postgresql", ActivityKind.Client, false, 0, true)]
    [InlineData("http.request.method", "GET", ActivityKind.Client, false, 0, true)]
    [InlineData("db.system", "mssql", ActivityKind.Server, false, 0, true)]
    public void ExportsOnlySlowFailedOrNonSqlSpans(
        string tag, string value, ActivityKind kind, bool fail, int delayMs, bool expectedExported)
    {
        var sourceName = "synthetic-sql-filter-" + Guid.NewGuid();
        using var source = new ActivitySource(sourceName);
        var exported = new List<Activity>();
        using var provider = FastSqlSpanFilterProcessor
            .AddTo(Sdk.CreateTracerProviderBuilder(), TimeSpan.FromMilliseconds(100))
            .AddSource(sourceName)
            .AddProcessor(new SimpleActivityExportProcessor(new ListExporter(exported)))
            .Build();

        using (var activity = source.StartActivity("span", kind))
        {
            Assert.NotNull(activity);
            activity.SetTag(tag, value);
            if (fail)
            {
                activity.SetStatus(ActivityStatusCode.Error);
            }

            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        Assert.Equal(expectedExported, exported.Count == 1);
    }

    private sealed class ListExporter(List<Activity> sink) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                sink.Add(activity);
            }

            return ExportResult.Success;
        }
    }
}

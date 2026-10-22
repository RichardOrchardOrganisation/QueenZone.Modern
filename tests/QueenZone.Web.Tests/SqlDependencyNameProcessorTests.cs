using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web.Tests;

public sealed class SqlDependencyNameProcessorTests
{
    [Theory]
    [InlineData("EXEC dbo.GetNews @id = 42", "dbo.GetNews")]
    [InlineData(" execute @rc = [dbo].[GetNews] @password = 'secret'", "dbo.GetNews")]
    [InlineData("EXEC [GetNews];", "GetNews")]
    [InlineData("dbo.GetNews", "dbo.GetNews")]
    [InlineData("[dbo].[GetNews]", "dbo.GetNews")]
    [InlineData("SELECT * FROM [dbo].[News] WHERE Id = 42", "SELECT dbo.News")]
    [InlineData("select 'FROM secret' FROM News", "SELECT News")]
    [InlineData("SELECT 1 -- FROM secret", "SELECT")]
    [InlineData("SELECT /* FROM secret */ * FROM News", "SELECT News")]
    [InlineData("INSERT INTO News (Title) VALUES ('secret')", "INSERT News")]
    [InlineData("UPDATE dbo.News SET Title = 'secret'", "UPDATE dbo.News")]
    [InlineData("DELETE FROM News WHERE Id = 42", "DELETE News")]
    [InlineData("MERGE INTO News USING Updates ON News.Id = Updates.Id", "MERGE News")]
    [InlineData("EXEC('SELECT secret')", "SQL: queenzone-db")]
    [InlineData("EXEC @command", "SQL: queenzone-db")]
    [InlineData("", "SQL: queenzone-db")]
    public void NamesSqlCommandsWithoutParameterValues(string command, string expected)
    {
        var activity = Export(command);

        Assert.Equal(expected, activity.DisplayName);
        Assert.Equal(expected == "SQL: queenzone-db" ? null : expected, activity.GetTagItem("db.operation.name"));
    }

    [Fact]
    public void StoredProcedureTagTakesPrecedence()
    {
        var activity = Export("EXEC dbo.Other @id = 42", procedure: "[dbo].[GetNews]");

        Assert.Equal("dbo.GetNews", activity.DisplayName);
        Assert.Equal("dbo.GetNews", activity.GetTagItem("db.operation.name"));
    }

    [Fact]
    public void SupportsLegacyTagsAndPreservesExistingOperation()
    {
        var activity = Export("EXEC dbo.GetNews", legacyTags: true, operation: "EXECUTE");

        Assert.Equal("dbo.GetNews", activity.DisplayName);
        Assert.Equal("EXECUTE", activity.GetTagItem("db.operation.name"));
    }

    [Fact]
    public void QueryTextTakesPrecedenceOverStatement()
    {
        var activity = Export("EXEC dbo.GetNews", statement: "EXEC dbo.Other");

        Assert.Equal("dbo.GetNews", activity.DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapsNamesAt128Characters(bool useProcedureTag)
    {
        var name = new string('a', 129);
        var activity = Export(useProcedureTag ? null : name, procedure: useProcedureTag ? name : null);

        Assert.Equal(name[..128], activity.DisplayName);
        Assert.Equal(name[..128], activity.GetTagItem("db.operation.name"));
    }

    [Theory]
    [InlineData("postgresql", ActivityKind.Client)]
    [InlineData(null, ActivityKind.Client)]
    [InlineData("microsoft.sql_server", ActivityKind.Server)]
    [InlineData("microsoft.sql_server", ActivityKind.Internal)]
    public void LeavesOtherSpansUntouched(string? system, ActivityKind kind)
    {
        var activity = Export("EXEC dbo.GetNews", system: system, kind: kind);

        Assert.Equal("SQL: queenzone-db", activity.DisplayName);
        Assert.Null(activity.GetTagItem("db.operation.name"));
    }

    [Fact]
    public void LeavesSqlWithoutCommandUntouched()
    {
        var activity = Export(null);

        Assert.Equal("SQL: queenzone-db", activity.DisplayName);
        Assert.Null(activity.GetTagItem("db.operation.name"));
    }

    private static Activity Export(
        string? command,
        string? procedure = null,
        bool legacyTags = false,
        string? operation = null,
        string? statement = null,
        string? system = "microsoft.sql_server",
        ActivityKind kind = ActivityKind.Client)
    {
        var sourceName = "synthetic-sql-name-" + Guid.NewGuid();
        using var source = new ActivitySource(sourceName);
        var exported = new List<Activity>();
        using var provider = SqlDependencyNameProcessor.AddTo(Sdk.CreateTracerProviderBuilder())
            .AddSource(sourceName)
            .AddProcessor(new SimpleActivityExportProcessor(new ListExporter(exported)))
            .Build();

        using (var activity = source.StartActivity("SQL: queenzone-db", kind))
        {
            Assert.NotNull(activity);
            activity.SetTag(legacyTags ? "db.system" : "db.system.name", legacyTags ? "mssql" : system);
            activity.SetTag("db.statement", statement);
            activity.SetTag(legacyTags ? "db.statement" : "db.query.text", command);
            activity.SetTag("db.stored_procedure.name", procedure);
            activity.SetTag("db.operation.name", operation);
        }

        return Assert.Single(exported);
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

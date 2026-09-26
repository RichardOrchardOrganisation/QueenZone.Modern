using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class ImportCommandTests
{
    [Theory]
    [InlineData("import-quotes")]
    [InlineData("import-trivia")]
    [InlineData("import-history")]
    public async Task ToolsApp_PrintsUsage_WhenImportOptionsAreInvalid(string command)
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync([command]);

            Assert.Equal(2, exitCode);
            Assert.Contains("--csv", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("import-quotes")]
    [InlineData("import-trivia")]
    [InlineData("import-history")]
    public async Task ToolsApp_ReportsMissingCsv_WithoutOpeningADatabase(string command)
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"qz-missing-{Guid.NewGuid():N}.csv");
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                command,
                "--csv",
                missingPath,
                "--dry-run",
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("CSV file was not found", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ToolsApp_DryRunImportQuotes_CountsRows()
    {
        var csvPath = WriteTempCsv("""
            Text,WhoSaid,Context,SourceType,SourceKey
            A quote with context,Brian May,On discovering Buddy Holly,AsItBeganBook,a-quote-with-context-brian-may
            """);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-quotes",
                "--csv",
                csvPath,
                "--dry-run",
            ]);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Contains("Rows read: 1", text, StringComparison.Ordinal);
            Assert.Contains("Dry run only", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task ToolsApp_DryRunImportTrivia_CountsRows()
    {
        var csvPath = WriteTempCsv("""
            Text,Category,Difficulty,Source
            Freddie wrote Crazy Little Thing Called Love as a tribute to Elvis.,Music,easy,Queen FAQ
            """);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-trivia",
                "--csv",
                csvPath,
                "--dry-run",
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Rows read: 1", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task ToolsApp_DryRunImportHistory_CountsRows()
    {
        var csvPath = WriteTempCsv("""
            Title,Summary,EventDate,DatePrecision,Category,Importance,SourceType,SourceKey,SourceUrl
            Live Aid,Queen play Wembley.,1985-07-13,ExactDate,Concert,100,Wikipedia,live-aid-1985,
            """);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-history",
                "--csv",
                csvPath,
                "--dry-run",
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Rows read: 1", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(csvPath);
        }
    }

    private static string WriteTempCsv(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qz-import-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contents);
        return path;
    }
}

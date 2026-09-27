using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ToolsAppCommandDispatchTests
{
    [Theory]
    [InlineData("backfill-photo-dimensions")]
    [InlineData("convert-legacy-bbcode")]
    public async Task RunAsync_RoutesLegacySqlCommands_ToUsageErrorWithoutConnectionString(string command)
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var exitCode = await ToolsApp.RunAsync([command]);

            Assert.Equal(2, exitCode);
            Assert.Contains(command, error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_RoutesCheckPhotos_ToUsageErrorWithoutConnectionString()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, """{ "ConnectionStrings": {} }""");
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["check-photos", "--settings-file", settingsPath]);

            Assert.Equal(2, exitCode);
            Assert.Contains("check-photos", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
            Console.SetError(originalError);
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task RunAsync_RoutesBackfillFanPerformanceDurations_ToUsageErrorWithoutSecrets()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        var previousLegacy = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        var previousBlob = Environment.GetEnvironmentVariable("ConnectionStrings__BlobStorage");
        var previousAzure = Environment.GetEnvironmentVariable("AzureStorage__ConnectionString");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__BlobStorage", null);
        Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", null);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["backfill-fan-performance-durations"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("backfill-fan-performance-durations", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previousLegacy);
            Environment.SetEnvironmentVariable("ConnectionStrings__BlobStorage", previousBlob);
            Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", previousAzure);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_RoutesMinifyCss_ToUsageErrorWhenNoPathsAreGiven()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["minify-css"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("minify-css", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_RoutesBundleCss_ToUsageErrorWhenOutputAndInputsAreMissing()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["bundle-css"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("bundle-css", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_PrintsUsage_ForUnknownCommand()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["not-a-real-command"]);

            Assert.Equal(2, exitCode);
            var text = error.ToString();
            Assert.Contains("Unknown command 'not-a-real-command'.", text, StringComparison.Ordinal);
            Assert.Contains("import-quiz-questions", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_RoutesDevSnapshot_AndSurfacesParseError()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ToolsApp.RunAsync(["dev-snapshot"]));
        Assert.Contains("copy or verify", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PrintsUsage_WhenQuizImportOptionsAreInvalid()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["import-quiz-questions"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("--csv", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsMissingQuizCsv_WithoutOpeningADatabase()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"qz-missing-quiz-{Guid.NewGuid():N}.csv");
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-quiz-questions",
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
    public async Task RunAsync_DryRunImportQuizQuestions_CountsQuizzesAndQuestions()
    {
        var csvPath = WriteTempCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Freddie Mercury,true
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Brian May,false
            """);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-quiz-questions",
                "--csv",
                csvPath,
                "--dry-run",
            ]);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Contains("Quizzes parsed: 1", text, StringComparison.Ordinal);
            Assert.Contains("Questions parsed: 1", text, StringComparison.Ordinal);
            Assert.Contains("Dry run only", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task RunAsync_DryRunImportQuizQuestions_ReportsInvalidCsv()
    {
        var csvPath = WriteTempCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Quiz A,,Question 1,,,1,Option 1,true
            Quiz A,,Question 1,,,1,Option 2,false
            Quiz B,,Question 1,,,1,Option 1,true
            Quiz B,,Question 1,,,1,Option 2,false
            Quiz A,,Question 2,,,1,Option 1,true
            Quiz A,,Question 2,,,1,Option 2,false
            """);

        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(
            [
                "import-quiz-questions",
                "--csv",
                csvPath,
                "--dry-run",
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("not contiguous", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(csvPath);
        }
    }

    private static string WriteTempCsv(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qz-quiz-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contents);
        return path;
    }
}

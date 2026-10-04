using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ImportCrosswordsCommandTests
{
    [Fact]
    public async Task Dry_run_reports_ten_puzzles_zero_errors_without_a_connection_string()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
            Console.SetOut(output);
            var code = await ToolsApp.RunAsync(["import-crosswords", "--dir", SeedsDirectory, "--dry-run"]);
            Assert.Equal(0, code);
            Assert.Contains("Puzzles: 10; errors: 0.", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("No database changes", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(original);
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", source);
        }
    }

    [Fact]
    public async Task Corrupt_file_aborts_the_entire_batch_with_no_writes()
    {
        var directory = CopySeeds();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{");
            var batch = await ImportCrosswordsCommand.ReadBatchAsync(directory);
            Assert.Equal(10, batch.Seeds.Count);
            Assert.Contains(batch.Errors, issue => issue.File == "broken.json");
            var repository = new InMemoryCrosswordCatalogRepository(TimeProvider.System);
            await Assert.ThrowsAsync<ArgumentException>(() => ImportCrosswordsCommand.ApplyAsync(batch, repository));
            Assert.Empty(await repository.GetAllAsync());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Cli_reports_invalid_options_and_corrupt_batch_without_attempting_database_access()
    {
        var directory = CopySeeds();
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(errors);
            Assert.Equal(2, await ToolsApp.RunAsync(["import-crosswords", "--dry-run"]));
            await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{");
            Assert.Equal(2, await ToolsApp.RunAsync(["import-crosswords", "--dir", directory, "--dry-run"]));
            Assert.Contains("--dir is required", errors.ToString(), StringComparison.Ordinal);
            Assert.Contains("broken.json", errors.ToString(), StringComparison.Ordinal);
            Assert.Contains("No database changes", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Second_run_skips_ten_existing_slugs_without_overwriting_edits()
    {
        var batch = await ImportCrosswordsCommand.ReadBatchAsync(SeedsDirectory);
        Assert.Empty(batch.Errors);
        var repository = new InMemoryCrosswordCatalogRepository(TimeProvider.System);
        var first = await ImportCrosswordsCommand.ApplyAsync(batch, repository);
        Assert.Equal(10, first.Imported.Count);
        var item = (await repository.GetAllAsync())[0];
        await repository.SaveDraftAsync(item.Id, item.Seed with { Title = "Edited by admin" }, item.RowVersion, "admin@test.local");
        var second = await ImportCrosswordsCommand.ApplyAsync(batch, repository);
        Assert.Empty(second.Imported);
        Assert.Equal(10, second.Skipped.Count);
        Assert.Equal("Edited by admin", (await repository.GetByIdAsync(item.Id))!.Seed.Title);
        Assert.Equal("import-crosswords", (await repository.GetAuditAsync(item.Id)).Single(log => log.Action == "Imported").Actor);
    }

    [Fact]
    public async Task Duplicate_slugs_and_oversized_files_are_rejected_before_writes()
    {
        var directory = CopySeeds();
        try
        {
            File.Copy(Path.Combine(directory, "meet-the-band.json"), Path.Combine(directory, "duplicate.json"));
            await File.WriteAllBytesAsync(Path.Combine(directory, "oversized.json"), new byte[CrosswordSeedJson.MaxBytes + 1]);
            var batch = await ImportCrosswordsCommand.ReadBatchAsync(directory);
            Assert.Contains(batch.Errors, issue => issue.Code == "slug");
            Assert.Contains(batch.Errors, issue => issue.File == "oversized.json");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Missing_and_empty_directories_are_reported()
    {
        var directory = Path.Combine(Path.GetTempPath(), "crossword-tests-" + Guid.NewGuid().ToString("N"));
        Assert.Contains((await ImportCrosswordsCommand.ReadBatchAsync(directory)).Errors, issue => issue.Code == "directory");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Contains((await ImportCrosswordsCommand.ReadBatchAsync(directory)).Errors, issue => issue.Code == "directory");
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Options_require_a_directory_and_support_explicit_publish_without_echoing_connection_values()
    {
        Assert.NotNull(ImportCrosswordsCommand.ParseOptions(["--dry-run"]).Error);
        Assert.NotNull(ImportCrosswordsCommand.ParseOptions(["--dir"]).Error);
        Assert.NotNull(ImportCrosswordsCommand.ParseOptions(["--unexpected", "sensitive-value"]).Error);
        var parsed = ImportCrosswordsCommand.ParseOptions(["--dir", "seeds", "--publish", "--connection-string", "fake-connection"]);
        Assert.Null(parsed.Error);
        Assert.Equal("seeds", parsed.Directory);
        Assert.Equal("fake-connection", parsed.ConnectionString);
        Assert.True(parsed.Publish);
        Assert.False(parsed.DryRun);
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
            Assert.NotNull(ImportCrosswordsCommand.ParseOptions(["--dir", "seeds"]).Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", source);
        }
    }

    private static string SeedsDirectory => Path.Combine(AppContext.BaseDirectory, "data", "crosswords");

    private static string CopySeeds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "crossword-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(SeedsDirectory, "*.json"))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
        return directory;
    }
}

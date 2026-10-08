using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

internal static class ToolsApp
{
    private static readonly IReadOnlyDictionary<string, Func<string[], Task<int>>> Commands =
        new Dictionary<string, Func<string[], Task<int>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["import-crosswords"] = args => ImportCrosswordsCommand.RunAsync(args[1..]),
            ["backfill-fan-performance-durations"] = args => BackfillFanPerformanceDurationsCommand.RunAsync(args[1..]),
            ["check-photos"] = args => CheckPhotosCommand.RunAsync(args[1..]),
            ["generate-photo-thumbs"] = args => GeneratePhotoThumbsCommand.RunAsync(args[1..]),
            ["check-links"] = args => CheckLinksCommand.RunAsync(args[1..]),
            ["suggest-streaming-links"] = args => SuggestStreamingLinksCommand.RunAsync(args[1..]),
            ["photo-dim-inventory"] = args => PhotoDimInventoryCommand.RunAsync(args[1..]),
            ["backfill-photo-dimensions"] = args => BackfillPhotoDimensionsCommand.RunAsync(args[1..]),
            ["convert-legacy-bbcode"] = args => ConvertLegacyBbCodeCommand.RunAsync(args[1..]),
            ["create-reviewer-account"] = CreateReviewerAccountCommand.RunAsync,
            ["dev-snapshot"] = args => DevSnapshotCommand.RunAsync(args[1..]),
            ["minify-css"] = args => MinifyCssCommand.RunAsync(args[1..]),
            ["bundle-css"] = args => BundleCssCommand.RunAsync(args[1..]),
            ["import-quotes"] = RunImportQuotesAsync,
            ["import-trivia"] = RunImportTriviaAsync,
            ["import-quiz-questions"] = RunImportQuizQuestionsAsync,
            ["import-history"] = RunImportHistoryAsync
        };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return await RunImportHistoryAsync(args);
        }
        if (Commands.TryGetValue(args[0], out var command))
        {
            return await command(args);
        }
        PrintUsage($"Unknown command '{args[0]}'.");
        return 2;
    }

    private static async Task<int> RunCsvUpsertImportAsync(
        string[] args,
        string commandName,
        Func<string, int> countRows,
        Func<QueenZoneDbContext, string, Task<(int RowsRead, int Created, int Updated, int Unchanged)>> import)
    {
        var options = ImportOptions.Parse(args, commandName);
        if (!options.IsValid)
        {
            PrintUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.CsvPath))
        {
            await Console.Error.WriteLineAsync($"CSV file was not found: {options.CsvPath}");
            return 2;
        }

        if (options.DryRun)
        {
            Console.WriteLine($"Rows read: {countRows(options.CsvPath)}");
            Console.WriteLine("Dry run only. No database changes were made.");
            return 0;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;

        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var result = await import(dbContext, options.CsvPath);

        Console.WriteLine($"Rows read: {result.RowsRead}");
        Console.WriteLine($"Created: {result.Created}");
        Console.WriteLine($"Updated: {result.Updated}");
        Console.WriteLine($"Unchanged: {result.Unchanged}");
        return 0;
    }

    private static Task<int> RunImportHistoryAsync(string[] args) =>
        RunCsvUpsertImportAsync(
            args,
            "import-history",
            csvPath => QueenHistoryCsvImporter.ReadRows(csvPath).Count,
            async (dbContext, csvPath) =>
            {
                var result = await new QueenHistoryCsvImporter(dbContext).ImportAsync(csvPath, DateTime.UtcNow);
                return (result.RowsRead, result.Created, result.Updated, result.Unchanged);
            });

    private static Task<int> RunImportQuotesAsync(string[] args) =>
        RunCsvUpsertImportAsync(
            args,
            "import-quotes",
            csvPath => QuoteCsvImporter.ReadRows(csvPath).Count,
            async (dbContext, csvPath) =>
            {
                var result = await new QuoteCsvImporter(dbContext).ImportAsync(csvPath, DateTime.UtcNow);
                return (result.RowsRead, result.Created, result.Updated, result.Unchanged);
            });

    private static Task<int> RunImportTriviaAsync(string[] args) =>
        RunCsvUpsertImportAsync(
            args,
            "import-trivia",
            csvPath => TriviaFactCsvImporter.ReadRows(csvPath).Count,
            async (dbContext, csvPath) =>
            {
                var result = await new TriviaFactCsvImporter(dbContext).ImportAsync(csvPath, DateTime.UtcNow);
                return (result.RowsRead, result.Created, result.Updated, result.Unchanged);
            });

    private static async Task<int> RunImportQuizQuestionsAsync(string[] args)
    {
        var options = ImportOptions.Parse(args, "import-quiz-questions");
        if (!options.IsValid)
        {
            PrintUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.CsvPath))
        {
            await Console.Error.WriteLineAsync($"CSV file was not found: {options.CsvPath}");
            return 2;
        }

        if (options.DryRun)
        {
            try
            {
                var drafts = QuizCsvImporter.ReadDrafts(options.CsvPath);
                Console.WriteLine($"Quizzes parsed: {drafts.Count}");
                Console.WriteLine($"Questions parsed: {drafts.Sum(draft => draft.Questions.Count)}");
                Console.WriteLine("Dry run only. No database changes were made.");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                return 2;
            }
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;

        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var importer = new QuizCsvImporter(repository);

        try
        {
            var result = await importer.ImportAsync(options.CsvPath);
            Console.WriteLine($"Options read: {result.RowsRead}");
            Console.WriteLine($"Quizzes created: {result.QuizzesCreated}");
            Console.WriteLine($"Questions created: {result.QuestionsCreated}");
            Console.WriteLine("Every imported quiz is unpublished. Review and publish it from Admin > Quizzes.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 2;
        }
    }

    private static void PrintUsage(string errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- import-crosswords --dir data/crosswords [--dry-run] [--publish] [--connection-string <connection-string>]",
            "  dotnet run --project src/QueenZone.Tools -- import-history --csv <path> --connection-string <connection-string>",
            "  dotnet run --project src/QueenZone.Tools -- import-history --csv <path> --dry-run",
            "  dotnet run --project src/QueenZone.Tools -- import-quotes --csv <path> --connection-string <connection-string>",
            "  dotnet run --project src/QueenZone.Tools -- import-quotes --csv <path> --dry-run",
            "  dotnet run --project src/QueenZone.Tools -- import-trivia --csv <path> --connection-string <connection-string>",
            "  dotnet run --project src/QueenZone.Tools -- import-trivia --csv <path> --dry-run",
            "  dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv <path> --connection-string <connection-string>",
            "  dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv <path> --dry-run",
            "  dotnet run --project src/QueenZone.Tools -- check-photos [options]",
            "  dotnet run --project src/QueenZone.Tools -- generate-photo-thumbs [options]",
            "  dotnet run --project src/QueenZone.Tools -- check-links [options]",
            "  dotnet run --project src/QueenZone.Tools -- suggest-streaming-links --out <file.csv> [options]",
            "  dotnet run --project src/QueenZone.Tools -- photo-dim-inventory [options]",
            "  dotnet run --project src/QueenZone.Tools -- backfill-photo-dimensions [options]",
            "  dotnet run --project src/QueenZone.Tools -- convert-legacy-bbcode [options]",
            "  dotnet run --project src/QueenZone.Tools -- create-reviewer-account --email <email> --password <password> --display-name <name> --connection-string <connection-string>",
            "  dotnet run --project src/QueenZone.Tools -- dev-snapshot <copy|verify> --config <path> [--manifest <path>] [--summary <path>]",
            "  dotnet run --project src/QueenZone.Tools -- minify-css <path.css> [more paths...]",
            "",
            "Connection string can also be supplied with ConnectionStrings__QueenZoneLegacy.");
}

internal sealed class ImportOptions
{
    private ImportOptions()
    {
    }

    public string CsvPath { get; private init; } = string.Empty;

    public string ConnectionString { get; private init; } = string.Empty;

    public bool IsValid { get; private init; }

    public bool DryRun { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public static ImportOptions Parse(string[] args, string commandName)
    {
        if (args.Length == 0 || !string.Equals(args[0], commandName, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Command is required.");
        }

        string? csvPath = null;
        string? connectionString = null;
        var dryRun = false;
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (ToolArgs.TryReadValue(args, ref index, "--csv", out var csvArgument))
            {
                csvPath = csvArgument;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--connection-string", out var connectionStringArgument))
            {
                connectionString = connectionStringArgument;
                continue;
            }

            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            return Invalid("--csv is required.");
        }

        if (!dryRun && string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string or ConnectionStrings__QueenZoneLegacy is required.");
        }

        return new ImportOptions
        {
            CsvPath = csvPath,
            ConnectionString = connectionString ?? string.Empty,
            DryRun = dryRun,
            IsValid = true,
        };
    }

    private static ImportOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

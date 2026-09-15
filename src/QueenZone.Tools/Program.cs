using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Tools;

return await ToolsApp.RunAsync(args);

internal static class ToolsApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "check-photos", StringComparison.OrdinalIgnoreCase))
        {
            return await CheckPhotosCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "generate-photo-thumbs", StringComparison.OrdinalIgnoreCase))
        {
            return await GeneratePhotoThumbsCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "check-links", StringComparison.OrdinalIgnoreCase))
        {
            return await CheckLinksCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "photo-dim-inventory", StringComparison.OrdinalIgnoreCase))
        {
            return await PhotoDimInventoryCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "backfill-photo-dimensions", StringComparison.OrdinalIgnoreCase))
        {
            return await BackfillPhotoDimensionsCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "convert-legacy-bbcode", StringComparison.OrdinalIgnoreCase))
        {
            return await ConvertLegacyBbCodeCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "create-reviewer-account", StringComparison.OrdinalIgnoreCase))
        {
            return await CreateReviewerAccountCommand.RunAsync(args);
        }

        if (args.Length > 0 && string.Equals(args[0], "dev-snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return await DevSnapshotCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "minify-css", StringComparison.OrdinalIgnoreCase))
        {
            return await MinifyCssCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "bundle-css", StringComparison.OrdinalIgnoreCase))
        {
            return await BundleCssCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "import-quotes", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportQuotesAsync(args);
        }

        if (args.Length > 0 && string.Equals(args[0], "import-trivia", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportTriviaAsync(args);
        }

        if (args.Length == 0 || string.Equals(args[0], "import-history", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportHistoryAsync(args);
        }

        PrintUsage($"Unknown command '{args[0]}'.");
        return 2;
    }

    private static async Task<int> RunImportHistoryAsync(string[] args)
    {
        var options = ImportOptions.Parse(args, "import-history");
        if (!options.IsValid)
        {
            PrintUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.CsvPath))
        {
            Console.Error.WriteLine($"CSV file was not found: {options.CsvPath}");
            return 2;
        }

        if (options.DryRun)
        {
            var rows = QueenHistoryCsvImporter.ReadRows(options.CsvPath);
            Console.WriteLine($"Rows read: {rows.Count}");
            Console.WriteLine("Dry run only. No database changes were made.");
            return 0;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;

        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var importer = new QueenHistoryCsvImporter(dbContext);
        var result = await importer.ImportAsync(options.CsvPath, DateTime.UtcNow);

        Console.WriteLine($"Rows read: {result.RowsRead}");
        Console.WriteLine($"Created: {result.Created}");
        Console.WriteLine($"Updated: {result.Updated}");
        Console.WriteLine($"Unchanged: {result.Unchanged}");
        return 0;
    }

    private static async Task<int> RunImportQuotesAsync(string[] args)
    {
        var options = ImportOptions.Parse(args, "import-quotes");
        if (!options.IsValid)
        {
            PrintUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.CsvPath))
        {
            Console.Error.WriteLine($"CSV file was not found: {options.CsvPath}");
            return 2;
        }

        if (options.DryRun)
        {
            var rows = QuoteCsvImporter.ReadRows(options.CsvPath);
            Console.WriteLine($"Rows read: {rows.Count}");
            Console.WriteLine("Dry run only. No database changes were made.");
            return 0;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;

        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var importer = new QuoteCsvImporter(dbContext);
        var result = await importer.ImportAsync(options.CsvPath, DateTime.UtcNow);

        Console.WriteLine($"Rows read: {result.RowsRead}");
        Console.WriteLine($"Created: {result.Created}");
        Console.WriteLine($"Updated: {result.Updated}");
        Console.WriteLine($"Unchanged: {result.Unchanged}");
        return 0;
    }

    private static async Task<int> RunImportTriviaAsync(string[] args)
    {
        var options = ImportOptions.Parse(args, "import-trivia");
        if (!options.IsValid)
        {
            PrintUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.CsvPath))
        {
            Console.Error.WriteLine($"CSV file was not found: {options.CsvPath}");
            return 2;
        }

        if (options.DryRun)
        {
            var rows = TriviaFactCsvImporter.ReadRows(options.CsvPath);
            Console.WriteLine($"Rows read: {rows.Count}");
            Console.WriteLine("Dry run only. No database changes were made.");
            return 0;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;

        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var importer = new TriviaFactCsvImporter(dbContext);
        var result = await importer.ImportAsync(options.CsvPath, DateTime.UtcNow);

        Console.WriteLine($"Rows read: {result.RowsRead}");
        Console.WriteLine($"Created: {result.Created}");
        Console.WriteLine($"Updated: {result.Updated}");
        Console.WriteLine($"Unchanged: {result.Unchanged}");
        return 0;
    }

    private static void PrintUsage(string errorMessage)
    {
        Console.Error.WriteLine(errorMessage);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-history --csv <path> --connection-string <connection-string>");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-history --csv <path> --dry-run");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-quotes --csv <path> --connection-string <connection-string>");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-quotes --csv <path> --dry-run");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-trivia --csv <path> --connection-string <connection-string>");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- import-trivia --csv <path> --dry-run");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- check-photos [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- generate-photo-thumbs [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- check-links [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- photo-dim-inventory [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- backfill-photo-dimensions [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- convert-legacy-bbcode [options]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- create-reviewer-account --email <email> --password <password> --display-name <name> --connection-string <connection-string>");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- dev-snapshot <copy|verify> --config <path> [--manifest <path>] [--summary <path>]");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- minify-css <path.css> [more paths...]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Connection string can also be supplied with ConnectionStrings__QueenZoneLegacy.");
    }
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
            if (string.Equals(arg, "--csv", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                csvPath = args[++index];
                continue;
            }

            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
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

using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

internal sealed record CrosswordBatchIssue(string File, string Code, string Message);
internal sealed record CrosswordBatch(IReadOnlyList<CrosswordSeed> Seeds, IReadOnlyList<CrosswordBatchIssue> Errors);

internal static class ImportCrosswordsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseOptions(args);
        if (options.Error is not null)
        {
            ToolArgs.WriteUsage(options.Error,
                "QueenZone.Tools import-crosswords --dir data/crosswords [--dry-run] [--publish] [--connection-string <value>]");
            return 2;
        }
        var batch = await ReadBatchAsync(options.Directory!);
        if (batch.Errors.Count > 0)
        {
            foreach (var error in batch.Errors)
            {
                await Console.Error.WriteLineAsync($"{error.File}: {error.Code}: {error.Message}");
            }
            Console.WriteLine($"Puzzles: {batch.Seeds.Count}; errors: {batch.Errors.Count}. No database changes were made.");
            return 2;
        }
        Console.WriteLine($"Puzzles: {batch.Seeds.Count}; errors: 0.");
        if (options.DryRun)
        {
            Console.WriteLine("Dry run only. No database changes were made.");
            return 0;
        }
        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(options.ConnectionString,
            sql => sql.EnableRetryOnFailure()).Options;
        await using var db = new QueenZoneDbContext(dbOptions);
        var result = await ApplyAsync(batch, new EfCrosswordCatalogRepository(db, TimeProvider.System), options.Publish);
        Console.WriteLine($"Imported: {result.Imported.Count}; skipped: {result.Skipped.Count}.");
        foreach (var slug in result.Skipped)
        {
            Console.WriteLine($"Skipped existing slug: {slug}");
        }
        return 0;
    }

    internal static async Task<CrosswordBatch> ReadBatchAsync(string directory, CancellationToken cancellationToken = default)
    {
        var seeds = new List<CrosswordSeed>();
        var errors = new List<CrosswordBatchIssue>();
        if (!System.IO.Directory.Exists(directory))
        {
            return new([], [new(directory, "directory", "Seed directory was not found.")]);
        }
        var files = System.IO.Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            return new([], [new(directory, "directory", "No JSON puzzle files were found.")]);
        }
        foreach (var file in files)
        {
            if (new FileInfo(file).Length > CrosswordSeedJson.MaxBytes)
            {
                errors.Add(new(Path.GetFileName(file), "file", "Crossword JSON must not exceed 256 KB."));
                continue;
            }
            var result = CrosswordSeedJson.Parse(await File.ReadAllBytesAsync(file, cancellationToken));
            errors.AddRange(result.Errors.Select(issue => new CrosswordBatchIssue(Path.GetFileName(file), issue.Code, issue.Message)));
            if (result.IsValid)
            {
                seeds.Add(result.Seed!);
            }
        }
        foreach (var duplicate in seeds.GroupBy(seed => seed.Slug, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add(new(directory, "slug", $"Duplicate slug '{duplicate.Key}' in the batch."));
        }
        return new(seeds, errors);
    }

    internal static Task<CrosswordImportResult> ApplyAsync(CrosswordBatch batch, ICrosswordCatalogRepository repository,
        bool publish = false, CancellationToken cancellationToken = default)
    {
        if (batch.Errors.Count > 0)
        {
            throw new ArgumentException("The entire batch must validate before any write.", nameof(batch));
        }
        return repository.ImportAsync(batch.Seeds, Guid.Empty, "import-crosswords", publish, cancellationToken);
    }

    internal static CrosswordImportOptions ParseOptions(string[] args)
    {
        string? directory = null;
        string? connectionString = null;
        var dryRun = false;
        var publish = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (ToolArgs.TryReadValue(args, ref index, "--dir", out var value))
            {
                directory = value;
            }
            else if (ToolArgs.TryReadValue(args, ref index, "--connection-string", out value))
            {
                connectionString = value;
            }
            else if (args[index] == "--dry-run")
            {
                dryRun = true;
            }
            else if (args[index] == "--publish")
            {
                publish = true;
            }
            else
            {
                return new(null, null, false, false, "Unexpected or incomplete option.");
            }
        }
        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new(null, null, dryRun, publish, "--dir is required.");
        }
        if (!dryRun && string.IsNullOrWhiteSpace(connectionString))
        {
            return new(directory, null, dryRun, publish, "--connection-string or ConnectionStrings__QueenZoneLegacy is required unless --dry-run is used.");
        }
        return new(directory, connectionString, dryRun, publish, null);
    }
}

internal sealed record CrosswordImportOptions(string? Directory, string? ConnectionString, bool DryRun, bool Publish, string? Error);

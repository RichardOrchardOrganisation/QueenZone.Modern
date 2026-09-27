using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>
/// Read-only original-dimension coverage report for public photos (issue #435).
/// </summary>
internal static class PhotoDimInventoryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = PhotoDimInventoryOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var report = await BuildReportAsync(options);
        var text = PhotoDimensionInventory.FormatText(report);
        Console.WriteLine(text);

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            await File.WriteAllTextAsync(options.OutputPath, text + Environment.NewLine);
            Console.WriteLine();
            Console.WriteLine($"Report written to: {options.OutputPath}");
        }

        return 0;
    }

    internal static async Task<PhotoDimensionInventoryReport> BuildReportAsync(
        PhotoDimInventoryOptions options,
        IPhotoRepository? repositoryOverride = null)
    {
        if (repositoryOverride is not null)
        {
            var photos = await LoadFromRepositoryAsync(repositoryOverride, options);
            return PhotoDimensionInventory.FromPhotos(photos);
        }

        var dimensions = await LoadDimensionsFromSqlAsync(options);
        return PhotoDimensionInventory.FromDimensions(dimensions);
    }

    /// <summary>
    /// Loads only original width/height pairs (no image URLs/titles) so legacy null strings
    /// cannot break the inventory path.
    /// </summary>
    internal static async Task<IReadOnlyList<(int Width, int Height)>> LoadDimensionsFromSqlAsync(
        PhotoDimInventoryOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);

        int? catId = options.CategoryId;
        if (catId is null && !string.IsNullOrWhiteSpace(options.CategorySlug))
        {
            var categories = await new EfPhotoRepository(dbContext)
                .GetCategoriesAsync(options.CancellationToken);
            catId = categories
                .FirstOrDefault(category =>
                    string.Equals(category.Slug, options.CategorySlug, StringComparison.OrdinalIgnoreCase))
                ?.CatId;
            if (catId is null)
            {
                return [];
            }
        }

        // CAST smallint PIC_* columns to int for EF SqlQueryRaw mapping.
        var sql = catId is null
            ? """
              SELECT
                  CAST(ISNULL(PIC_WIDTH, 0) AS int) AS Width,
                  CAST(ISNULL(PIC_HEIGHT, 0) AS int) AS Height
              FROM dbo.PIC_FILES_T
              WHERE DISPLAY = 1
              """
            : """
              SELECT
                  CAST(ISNULL(PIC_WIDTH, 0) AS int) AS Width,
                  CAST(ISNULL(PIC_HEIGHT, 0) AS int) AS Height
              FROM dbo.PIC_FILES_T
              WHERE DISPLAY = 1 AND Cat_ID = {0}
              """;

        List<DimensionRow> rows = catId is null
            ? await dbContext.Database.SqlQueryRaw<DimensionRow>(sql).ToListAsync(options.CancellationToken)
            : await dbContext.Database.SqlQueryRaw<DimensionRow>(sql, catId.Value).ToListAsync(options.CancellationToken);

        IEnumerable<DimensionRow> limited = options.Limit is int limit
            ? rows.Take(limit)
            : rows;

        return limited.Select(row => (row.Width, row.Height)).ToList();
    }

    /// <summary>Test helper: load full photo items via repository (sample / in-memory).</summary>
    internal static async Task<IReadOnlyList<PhotoItem>> LoadPublicPhotosAsync(
        PhotoDimInventoryOptions options,
        IPhotoRepository? repositoryOverride = null)
    {
        if (repositoryOverride is not null)
        {
            return await LoadFromRepositoryAsync(repositoryOverride, options);
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var repository = new EfPhotoRepository(dbContext);
        return await LoadFromRepositoryAsync(repository, options);
    }

    private static async Task<IReadOnlyList<PhotoItem>> LoadFromRepositoryAsync(
        IPhotoRepository repository,
        PhotoDimInventoryOptions options)
    {
        return await PhotoCategoryScan.LoadPhotosAsync(
            repository,
            options.CategoryId,
            options.CategorySlug,
            options.Limit,
            options.CancellationToken);
    }

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- photo-dim-inventory [options]",
            "",
            "Options:",
            "  --connection-string <cs>   SQL Server connection (or ConnectionStrings__QueenZoneLegacy)",
            "  --category-id <id>         Limit to one category id",
            "  --category-slug <slug>     Limit to one category slug",
            "  --limit <n>                Cap number of photos counted",
            "  --output <path>            Write report text to a file",
            "",
            "Read-only. Does not update PIC_WIDTH / PIC_HEIGHT.",
            "SQL-only variant: docs/sql/009-photo-dimension-inventory.sql",
            "Never log connection strings.");

    private sealed class DimensionRow
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }
}

internal sealed class PhotoDimInventoryOptions
{
    private PhotoDimInventoryOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public int? CategoryId { get; private init; }

    public string? CategorySlug { get; private init; }

    public int? Limit { get; private init; }

    public string? OutputPath { get; private init; }

    public CancellationToken CancellationToken { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public static PhotoDimInventoryOptions Parse(string[] args)
    {
        string? connectionString = null;
        int? categoryId = null;
        string? categorySlug = null;
        int? limit = null;
        string? outputPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--category-id", null, out var id, out var idError))
            {
                if (idError is not null)
                {
                    return Invalid(idError);
                }

                categoryId = id;
                continue;
            }

            if (string.Equals(arg, "--category-slug", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                categorySlug = args[++index];
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--limit", 1, out var parsedLimit, out var parsedLimitError))
            {
                if (parsedLimitError is not null)
                {
                    return Invalid(parsedLimitError);
                }

                limit = parsedLimit;
                continue;
            }

            if (string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string or ConnectionStrings__QueenZoneLegacy is required.");
        }

        return new PhotoDimInventoryOptions
        {
            ConnectionString = connectionString,
            CategoryId = categoryId,
            CategorySlug = categorySlug,
            Limit = limit,
            OutputPath = outputPath,
            CancellationToken = CancellationToken.None,
            IsValid = true,
        };
    }

    private static PhotoDimInventoryOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

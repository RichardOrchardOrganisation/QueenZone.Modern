using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using QueenZone.Data;
using SixLabors.ImageSharp;

namespace QueenZone.Tools;

/// <summary>
/// Opt-in backfill of zero/missing PIC_WIDTH / PIC_HEIGHT from image bytes (issue #438).
/// Dry-run by default; use --apply to write.
/// </summary>
internal static class BackfillPhotoDimensionsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = BackfillPhotoDimensionsOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var candidates = await LoadCandidatesAsync(options);
        return await RunCoreAsync(options, candidates);
    }

    internal static async Task<int> RunCoreAsync(
        BackfillPhotoDimensionsOptions options,
        IReadOnlyList<BackfillPhotoRow> candidates,
        IPhotoDimensionProbe? probeOverride = null)
    {
        Console.WriteLine("Backfill photo original dimensions (PIC_WIDTH / PIC_HEIGHT)");
        Console.WriteLine("===========================================================");
        Console.WriteLine($"Candidates: {candidates.Count}");
        Console.WriteLine($"Mode: {(options.Apply ? "APPLY (writes enabled)" : "dry-run (no writes)")}");
        Console.WriteLine($"Force overwrite non-zero: {options.Force}");
        Console.WriteLine($"Delay between items: {options.DelayMs}ms");
        Console.WriteLine();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No matching PIC_FILES_T rows.");
            return 0;
        }

        var probe = probeOverride ?? CreateProbe(options);
        await using var disposable = probe as IAsyncDisposable;

        var wouldUpdate = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var row in candidates)
        {
            try
            {
                if (!options.Force && row.PictureWidth > 0 && row.PictureHeight > 0)
                {
                    skipped++;
                    Console.WriteLine(
                        $"  SKIP pic_id={row.PicId} already {row.PictureWidth}x{row.PictureHeight}");
                    continue;
                }

                var measured = await probe.MeasureAsync(row, options.CancellationToken);
                if (measured is null)
                {
                    failed++;
                    Console.Error.WriteLine($"  FAIL pic_id={row.PicId}: could not load/decode image");
                    continue;
                }

                if (measured.Width <= 0 || measured.Height <= 0)
                {
                    failed++;
                    Console.Error.WriteLine($"  FAIL pic_id={row.PicId}: measured non-positive size");
                    continue;
                }

                // smallint range
                if (measured.Width > short.MaxValue || measured.Height > short.MaxValue)
                {
                    failed++;
                    Console.Error.WriteLine(
                        $"  FAIL pic_id={row.PicId}: measured {measured.Width}x{measured.Height} exceeds smallint");
                    continue;
                }

                wouldUpdate++;
                Console.WriteLine(
                    $"  {(options.Apply ? "UPDATE" : "PLAN")} pic_id={row.PicId} " +
                    $"{row.PictureWidth}x{row.PictureHeight} -> {measured.Width}x{measured.Height} url={row.Url}");

                if (options.Apply)
                {
                    await UpdateDimensionsAsync(
                        options.ConnectionString,
                        row.PicId,
                        measured.Width,
                        measured.Height,
                        options.CancellationToken);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  FAIL pic_id={row.PicId}: {ex.Message}");
            }

            if (options.DelayMs > 0)
            {
                await Task.Delay(options.DelayMs, options.CancellationToken);
            }
        }

        ToolArgs.WriteBackfillSummary(
            wouldUpdate,
            updated,
            skipped,
            failed,
            options.Apply,
            "Dry-run only. Re-run with --apply to write PIC_WIDTH / PIC_HEIGHT.");

        return failed == 0 ? 0 : 1;
    }

    private static IPhotoDimensionProbe CreateProbe(BackfillPhotoDimensionsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.StorageConnectionString))
        {
            return new BlobPhotoDimensionProbe(options.StorageConnectionString, options.BlobEndpoint);
        }

        return new HttpPhotoDimensionProbe(options.HttpTimeout, options.BlobEndpoint);
    }

    [ExcludeFromCodeCoverage]
    private static async Task<IReadOnlyList<BackfillPhotoRow>> LoadCandidatesAsync(
        BackfillPhotoDimensionsOptions options)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(options.CancellationToken);
        await using var command = connection.CreateCommand();

        var where = new List<string> { "1 = 1" };
        if (options.PublicOnly)
        {
            where.Add("DISPLAY = 1");
        }

        if (!options.Force)
        {
            where.Add("(ISNULL(PIC_WIDTH, 0) = 0 OR ISNULL(PIC_HEIGHT, 0) = 0)");
        }

        if (options.CategoryId is int catId)
        {
            where.Add("Cat_ID = @catId");
            command.Parameters.AddWithValue("@catId", catId);
        }

        if (options.PicIds is { Count: > 0 })
        {
            var names = new List<string>();
            for (var i = 0; i < options.PicIds.Count; i++)
            {
                var name = "@pic" + i;
                names.Add(name);
                command.Parameters.AddWithValue(name, options.PicIds[i]);
            }

            where.Add($"PIC_ID IN ({string.Join(", ", names)})");
        }

        var top = options.Limit is int limit ? $"TOP ({limit}) " : string.Empty;
        command.CommandText = $"""
            SELECT {top}PIC_ID, Url, ISNULL(PIC_WIDTH, 0), ISNULL(PIC_HEIGHT, 0), Cat_ID, DISPLAY
            FROM dbo.PIC_FILES_T
            WHERE {string.Join(" AND ", where)}
            ORDER BY PIC_ID
            """;

        var rows = new List<BackfillPhotoRow>();
        await using var reader = await command.ExecuteReaderAsync(options.CancellationToken);
        while (await reader.ReadAsync(options.CancellationToken))
        {
            rows.Add(new BackfillPhotoRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Convert.ToInt32(reader.GetValue(2)),
                Convert.ToInt32(reader.GetValue(3)),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5))));
        }

        return rows;
    }

    [ExcludeFromCodeCoverage]
    private static async Task UpdateDimensionsAsync(
        string connectionString,
        int picId,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.PIC_FILES_T
            SET PIC_WIDTH = @w, PIC_HEIGHT = @h
            WHERE PIC_ID = @id
            """;
        command.Parameters.AddWithValue("@w", (short)width);
        command.Parameters.AddWithValue("@h", (short)height);
        command.Parameters.AddWithValue("@id", picId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- backfill-photo-dimensions [options]",
            "",
            "Options:",
            "  --connection-string <cs>     SQL Server (or ConnectionStrings__QueenZoneLegacy)",
            "  --storage-connection-string  Optional Azure Blob connection (preferred for apply)",
            "  --blob-endpoint <url>        Blob/CDN base for URL rewrite (optional)",
            "  --category-id <id>           Limit to category",
            "  --pic-ids <id,id,...>        Limit to specific pic ids",
            "  --limit <n>                  Max rows to consider",
            "  --include-hidden             Include DISPLAY <> 1 rows",
            "  --force                      Overwrite non-zero dims (dangerous)",
            "  --delay-ms <n>               Pause between items (default 50)",
            "  --apply                      Write updates (default is dry-run)",
            "",
            "Default: dry-run, DISPLAY=1, only rows with width or height zero.",
            "Measures via ImageSharp decode of original image bytes. Never log secrets.",
            "After apply, re-run: photo-dim-inventory");
}

internal sealed record BackfillPhotoRow(
    int PicId,
    string Url,
    int PictureWidth,
    int PictureHeight,
    int CatId,
    int Display);

internal sealed record MeasuredPhotoSize(int Width, int Height);

internal interface IPhotoDimensionProbe
{
    Task<MeasuredPhotoSize?> MeasureAsync(BackfillPhotoRow row, CancellationToken cancellationToken);
}

internal sealed class HttpPhotoDimensionProbe(TimeSpan timeout, string? blobEndpoint) : IPhotoDimensionProbe, IAsyncDisposable
{
    private readonly HttpClient http = new() { Timeout = timeout };

    public async Task<MeasuredPhotoSize?> MeasureAsync(BackfillPhotoRow row, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(row.Url)
            ? null
            : PhotoImageUrl.Build(row.Url);
        if (url is null)
        {
            return null;
        }

        // Prefer direct CDN/public URL; optional blob rewrite for private endpoints.
        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            url = PhotoImageUrl.ToBlobStorageUrl(url, blobEndpoint);
        }

        await using var stream = await http.GetStreamAsync(url, cancellationToken);
        using var image = await Image.LoadAsync(stream, cancellationToken);
        return new MeasuredPhotoSize(image.Width, image.Height);
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }
}

[ExcludeFromCodeCoverage]
internal sealed class BlobPhotoDimensionProbe(string storageConnectionString, string? blobEndpoint)
    : IPhotoDimensionProbe
{
    private readonly BlobServiceClient blobService = new(storageConnectionString);

    public async Task<MeasuredPhotoSize?> MeasureAsync(BackfillPhotoRow row, CancellationToken cancellationToken)
    {
        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(row.Url, blobEndpoint);
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            return null;
        }

        var client = blobService.GetBlobContainerClient(container).GetBlobClient(blobName);
        if (!await client.ExistsAsync(cancellationToken))
        {
            return null;
        }

        await using var stream = await client.OpenReadAsync(cancellationToken: cancellationToken);
        using var image = await Image.LoadAsync(stream, cancellationToken);
        return new MeasuredPhotoSize(image.Width, image.Height);
    }
}

internal sealed class BackfillPhotoDimensionsOptions
{
    private BackfillPhotoDimensionsOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public string? StorageConnectionString { get; private init; }

    public string? BlobEndpoint { get; private init; }

    public int? CategoryId { get; private init; }

    public IReadOnlyList<int>? PicIds { get; private init; }

    public int? Limit { get; private init; }

    public bool PublicOnly { get; private init; } = true;

    public bool Force { get; private init; }

    public bool Apply { get; private init; }

    public int DelayMs { get; private init; } = 50;

    public TimeSpan HttpTimeout { get; private init; } = TimeSpan.FromSeconds(60);

    public CancellationToken CancellationToken { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public static BackfillPhotoDimensionsOptions Parse(string[] args)
    {
        string? connectionString = null;
        string? storageConnectionString = null;
        string? blobEndpoint = null;
        int? categoryId = null;
        List<int>? picIds = null;
        int? limit = null;
        var publicOnly = true;
        var force = false;
        var apply = false;
        var delayMs = 50;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
                continue;
            }

            if (string.Equals(arg, "--storage-connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                storageConnectionString = args[++index];
                continue;
            }

            if (string.Equals(arg, "--blob-endpoint", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                blobEndpoint = args[++index];
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

            if (string.Equals(arg, "--pic-ids", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                picIds = args[++index]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(int.Parse)
                    .ToList();
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--limit", 1, out var n, out var nError))
            {
                if (nError is not null)
                {
                    return Invalid(nError);
                }

                limit = n;
                continue;
            }

            if (string.Equals(arg, "--include-hidden", StringComparison.OrdinalIgnoreCase))
            {
                publicOnly = false;
                continue;
            }

            if (string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
                continue;
            }

            if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--delay-ms", 0, out var delay, out var delayError))
            {
                if (delayError is not null)
                {
                    return Invalid(delayError);
                }

                delayMs = delay;
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        storageConnectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__BlobStorage");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string or ConnectionStrings__QueenZoneLegacy is required.");
        }

        return new BackfillPhotoDimensionsOptions
        {
            ConnectionString = connectionString,
            StorageConnectionString = storageConnectionString,
            BlobEndpoint = blobEndpoint,
            CategoryId = categoryId,
            PicIds = picIds,
            Limit = limit,
            PublicOnly = publicOnly,
            Force = force,
            Apply = apply,
            DelayMs = delayMs,
            CancellationToken = CancellationToken.None,
            IsValid = true,
        };
    }

    private static BackfillPhotoDimensionsOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

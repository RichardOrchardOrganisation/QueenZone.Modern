using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Data.SqlClient;
using QueenZone.Data;
using QueenZone.Storage;
using SixLabors.ImageSharp;

namespace QueenZone.Tools;

internal static class GeneratePhotoThumbsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = GeneratePhotoThumbsOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var photos = await LoadPhotosAsync(options);
        return await RunCoreAsync(options, photos);
    }

    internal static async Task<int> RunCoreAsync(
        GeneratePhotoThumbsOptions options,
        IReadOnlyList<GalleryPhotoRow> photos,
        IGalleryThumbGenerator? generatorOverride = null)
    {
        if (photos.Count == 0)
        {
            Console.WriteLine("No PIC_FILES_T rows matched the requested pic ids.");
            return 0;
        }

        Console.WriteLine("Generate gallery WebP thumbnails");
        Console.WriteLine("================================");
        Console.WriteLine($"Photos: {photos.Count}");
        Console.WriteLine($"Thumb size: {options.ThumbSizePixels}px square WebP");
        Console.WriteLine($"Dry run: {options.DryRun}");
        Console.WriteLine();

        if (options.DryRun)
        {
            foreach (var photo in photos)
            {
                var plan = PlanThumbnail(photo, options.ThumbSizePixels);
                Console.WriteLine(
                    $"  pic_id={photo.PicId} url={photo.Url} -> blob={plan.Container}/{plan.ThumbnailBlobName} legacy={plan.LegacyThumbPath}");
            }

            Console.WriteLine();
            Console.WriteLine("Dry run only. No blob uploads or database updates were made.");
            return 0;
        }

        var generator = generatorOverride ?? new AzureGalleryThumbGenerator(options.StorageConnectionString);
        var succeeded = 0;
        var failed = 0;

        foreach (var photo in photos)
        {
            try
            {
                var result = await generator.GenerateAsync(photo, options);
                succeeded++;
                Console.WriteLine(
                    $"  OK pic_id={photo.PicId} thumb={result.LegacyThumbPath} ({result.WidthPx}x{result.HeightPx})");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  FAIL pic_id={photo.PicId}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Succeeded: {succeeded}");
        Console.WriteLine($"Failed: {failed}");
        return failed == 0 ? 0 : 1;
    }

    internal static ThumbnailPlan PlanThumbnail(GalleryPhotoRow photo, int thumbSizePixels)
    {
        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(photo.Url);
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            throw new InvalidOperationException($"Could not parse blob location from Url '{photo.Url}'.");
        }

        var thumbBlobName = PhotoWebpDerivatives.ToThumbnailBlobName(blobName);
        var legacyThumbPath = PhotoWebpDerivatives.ToLegacyThumbnailPath(photo.Url, thumbBlobName);
        return new ThumbnailPlan(container, blobName, thumbBlobName, legacyThumbPath, thumbSizePixels);
    }

    [ExcludeFromCodeCoverage]
    private static async Task<IReadOnlyList<GalleryPhotoRow>> LoadPhotosAsync(GeneratePhotoThumbsOptions options)
    {
        var ids = options.PicIds;
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(options.CancellationToken);
        await using var command = connection.CreateCommand();
        var parameterNames = new List<string>();
        for (var index = 0; index < ids.Count; index++)
        {
            var name = "@p" + index;
            parameterNames.Add(name);
            command.Parameters.AddWithValue(name, ids[index]);
        }

        command.CommandText = $"""
            SELECT PIC_ID, Url, Thumb_URL, DISPLAY
            FROM dbo.PIC_FILES_T
            WHERE PIC_ID IN ({string.Join(", ", parameterNames)})
            ORDER BY PIC_ID
            """;

        var rows = new List<GalleryPhotoRow>();
        await using var reader = await command.ExecuteReaderAsync(options.CancellationToken);
        while (await reader.ReadAsync(options.CancellationToken))
        {
            rows.Add(new GalleryPhotoRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return rows;
    }

    private static void WriteUsage(string errorMessage)
    {
        Console.Error.WriteLine(errorMessage);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- generate-photo-thumbs [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --pic-ids <id,id,...>         PIC_FILES_T ids to process");
        Console.Error.WriteLine("  --pic-ids-file <path>         File with one pic id per line");
        Console.Error.WriteLine("  --connection-string <value>   Legacy SQL connection string");
        Console.Error.WriteLine("  --storage-connection-string <value>  Azure Storage connection string");
        Console.Error.WriteLine("  --settings-file <path>        appsettings.Local.json path");
        Console.Error.WriteLine("  --thumb-size <px>             Square thumb size (default: 400)");
        Console.Error.WriteLine("  --dry-run                     Plan uploads/updates without writing");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Writes {stem}_t.webp into the same gallery container and updates Thumb_URL / t_width / t_height.");
    }
}

internal interface IGalleryThumbGenerator
{
    Task<GeneratedThumbResult> GenerateAsync(GalleryPhotoRow photo, GeneratePhotoThumbsOptions options);
}

[ExcludeFromCodeCoverage]
internal sealed class AzureGalleryThumbGenerator(string storageConnectionString) : IGalleryThumbGenerator
{
    private readonly BlobServiceClient blobService = new(storageConnectionString);

    public async Task<GeneratedThumbResult> GenerateAsync(
        GalleryPhotoRow photo,
        GeneratePhotoThumbsOptions options)
    {
        var plan = GeneratePhotoThumbsCommand.PlanThumbnail(photo, options.ThumbSizePixels);
        var sourceBlob = blobService.GetBlobContainerClient(plan.Container).GetBlobClient(plan.SourceBlobName);
        if (!await sourceBlob.ExistsAsync(options.CancellationToken))
        {
            throw new InvalidOperationException($"Source blob not found: {plan.Container}/{plan.SourceBlobName}");
        }

        await using var download = await sourceBlob.OpenReadAsync(cancellationToken: options.CancellationToken);
        using var image = await Image.LoadAsync(download, options.CancellationToken);
        await using var thumb = await PhotoWebpDerivatives.CreateSquareThumbnailAsync(
            image,
            options.ThumbSizePixels,
            cancellationToken: options.CancellationToken);

        var targetBlob = blobService.GetBlobContainerClient(plan.Container).GetBlobClient(plan.ThumbnailBlobName);
        await targetBlob.UploadAsync(
            thumb.Stream,
            new BlobHttpHeaders { ContentType = PhotoWebpDerivatives.WebpContentType },
            cancellationToken: options.CancellationToken);

        await UpdateThumbMetadataAsync(
            options.ConnectionString,
            photo.PicId,
            plan.LegacyThumbPath,
            thumb.WidthPx,
            thumb.HeightPx,
            options.CancellationToken);

        return new GeneratedThumbResult(plan.LegacyThumbPath, thumb.WidthPx, thumb.HeightPx);
    }

    private static async Task UpdateThumbMetadataAsync(
        string connectionString,
        int picId,
        string thumbUrl,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.PIC_FILES_T
            SET Thumb_URL = @ThumbUrl,
                t_width = @Width,
                t_height = @Height
            WHERE PIC_ID = @PicId;
            """;
        command.Parameters.AddWithValue("@ThumbUrl", thumbUrl);
        command.Parameters.AddWithValue("@Width", width);
        command.Parameters.AddWithValue("@Height", height);
        command.Parameters.AddWithValue("@PicId", picId);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            throw new InvalidOperationException($"Expected to update 1 PIC_FILES_T row for PIC_ID={picId}, updated {rows}.");
        }
    }
}

internal sealed record GalleryPhotoRow(int PicId, string Url, string ThumbUrl, int? Display);

internal sealed record ThumbnailPlan(
    string Container,
    string SourceBlobName,
    string ThumbnailBlobName,
    string LegacyThumbPath,
    int ThumbSizePixels);

internal sealed record GeneratedThumbResult(string LegacyThumbPath, int WidthPx, int HeightPx);

internal sealed class GeneratePhotoThumbsOptions
{
    private GeneratePhotoThumbsOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public string StorageConnectionString { get; private init; } = string.Empty;

    public IReadOnlyList<int> PicIds { get; private init; } = [];

    public int ThumbSizePixels { get; private init; } = PhotoWebpDerivatives.DefaultThumbSizePixels;

    public bool DryRun { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public CancellationToken CancellationToken => CancellationToken.None;

    public static GeneratePhotoThumbsOptions Parse(string[] args)
    {
        string? connectionString = null;
        string? storageConnectionString = null;
        string? settingsFile = null;
        string? picIdsRaw = null;
        string? picIdsFile = null;
        var thumbSize = PhotoWebpDerivatives.DefaultThumbSizePixels;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (ToolArgs.TryReadValue(args, ref index, "--connection-string", out var connectionStringValue))
            {
                connectionString = connectionStringValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--storage-connection-string", out var storageConnectionStringValue))
            {
                storageConnectionString = storageConnectionStringValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--settings-file", out var settingsFileValue))
            {
                settingsFile = settingsFileValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--pic-ids", out var picIdsValue))
            {
                picIdsRaw = picIdsValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--pic-ids-file", out var picIdsFileValue))
            {
                picIdsFile = picIdsFileValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--thumb-size", out var thumbSizeValue))
            {
                if (!int.TryParse(thumbSizeValue, out var parsedThumbSize) || parsedThumbSize < 1)
                {
                    return Invalid("--thumb-size must be a positive integer.");
                }

                thumbSize = parsedThumbSize;
                continue;
            }

            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        var localSettings = ToolsLocalSettings.TryLoad(settingsFile);
        connectionString ??= localSettings?.QueenZoneLegacyLive;
        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        storageConnectionString ??= localSettings?.BlobStorage;
        storageConnectionString ??= Environment.GetEnvironmentVariable("AzureStorage__ConnectionString");

        var picIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(picIdsRaw))
        {
            foreach (var part in picIdsRaw.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out var id) || id < 1)
                {
                    return Invalid($"Invalid pic id: {part}");
                }

                picIds.Add(id);
            }
        }

        if (!string.IsNullOrWhiteSpace(picIdsFile))
        {
            if (!File.Exists(picIdsFile))
            {
                return Invalid($"Pic ids file was not found: {picIdsFile}");
            }

            foreach (var line in File.ReadLines(picIdsFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("--"))
                {
                    continue;
                }

                if (!int.TryParse(trimmed, out var id) || id < 1)
                {
                    return Invalid($"Invalid pic id in file: {trimmed}");
                }

                picIds.Add(id);
            }
        }

        picIds = picIds.Distinct().OrderBy(id => id).ToList();
        if (picIds.Count == 0)
        {
            return Invalid("--pic-ids or --pic-ids-file is required.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("A legacy SQL connection string is required via --connection-string, ConnectionStrings:QueenZoneLegacyLive in appsettings.Local.json, or ConnectionStrings__QueenZoneLegacy.");
        }

        if (!dryRun && string.IsNullOrWhiteSpace(storageConnectionString))
        {
            return Invalid("A blob storage connection string is required via --storage-connection-string, ConnectionStrings:BlobStorage in appsettings.Local.json, or AzureStorage__ConnectionString.");
        }

        return new GeneratePhotoThumbsOptions
        {
            ConnectionString = connectionString,
            StorageConnectionString = storageConnectionString ?? string.Empty,
            PicIds = picIds,
            ThumbSizePixels = thumbSize,
            DryRun = dryRun,
            IsValid = true,
        };
    }

    private static GeneratePhotoThumbsOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

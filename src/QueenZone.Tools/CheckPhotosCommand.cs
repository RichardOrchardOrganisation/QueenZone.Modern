using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

internal static class CheckPhotosCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CheckPhotosOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var photos = await LoadPhotosAsync(options);
        return await RunInventoryCheckAsync(options, photos);
    }

    internal static async Task<int> RunInventoryCheckAsync(
        CheckPhotosOptions options,
        IReadOnlyList<PhotoItem> photos,
        IPhotoBlobChecker? checkerOverride = null)
    {
        if (photos.Count == 0)
        {
            Console.WriteLine("No photos matched the requested filters.");
            return 0;
        }

        if (options.DryRun)
        {
            Console.WriteLine($"Photos to check: {photos.Count}");
            Console.WriteLine($"Check method: {options.Method}");
            Console.WriteLine($"Blob endpoint: {options.BlobEndpoint}");
            Console.WriteLine("Dry run only. No blob or HTTP requests were made.");
            return 0;
        }

        var checker = checkerOverride ?? CreateChecker(options);
        await using var disposableChecker = checker as IAsyncDisposable;
        var assetResults = await CheckPhotosAsync(photos, checker, options.BlobEndpoint, options.Concurrency, options.CancellationToken);
        var report = PhotoInventoryReport.FromAssetResults(assetResults);

        PrintSummary(report);
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            await WriteReportCsvAsync(options.OutputPath, report);
            Console.WriteLine();
            Console.WriteLine($"Report written to: {options.OutputPath}");
        }

        if (!string.IsNullOrWhiteSpace(options.HideIdsOutputPath))
        {
            await WriteHideIdsAsync(options.HideIdsOutputPath, report);
            Console.WriteLine($"Hide list written to: {options.HideIdsOutputPath}");
        }

        return report.HideFromPages.Count > 0 ? 1 : 0;
    }

    private static async Task<IReadOnlyList<PhotoItem>> LoadPhotosAsync(CheckPhotosOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var repository = new EfPhotoRepository(dbContext);
        return await LoadPhotosAsync(options, repository);
    }

    internal static async Task<IReadOnlyList<PhotoItem>> LoadPhotosAsync(
        CheckPhotosOptions options,
        IPhotoRepository repository)
    {
        var categories = await repository.GetCategoriesAsync(options.CancellationToken);

        if (options.CategoryId is int categoryId)
        {
            categories = categories.Where(category => category.CatId == categoryId).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(options.CategorySlug))
        {
            categories = categories
                .Where(category => string.Equals(category.Slug, options.CategorySlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var photos = new List<PhotoItem>();
        foreach (var category in categories)
        {
            var items = await repository.GetCategoryAllAsync(category.CatId, options.CancellationToken);
            photos.AddRange(items);
            if (options.Limit is int limit && photos.Count >= limit)
            {
                return photos.Take(limit).ToList();
            }
        }

        return options.Limit is int cappedLimit
            ? photos.Take(cappedLimit).ToList()
            : photos;
    }

    private static IPhotoBlobChecker CreateChecker(CheckPhotosOptions options) =>
        options.Method switch
        {
            PhotoCheckMethod.Http => new HttpPhotoBlobChecker(options.HttpTimeout),
            PhotoCheckMethod.Blob => new AzureBlobPhotoChecker(options.StorageConnectionString),
            _ => throw new InvalidOperationException($"Unsupported check method: {options.Method}"),
        };

    private static async Task<List<PhotoBlobCheckResult>> CheckPhotosAsync(
        IReadOnlyList<PhotoItem> photos,
        IPhotoBlobChecker checker,
        string blobEndpoint,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(Math.Max(concurrency, 1));
        var tasks = new List<Task<PhotoBlobCheckResult[]>>();

        foreach (var photo in photos)
        {
            tasks.Add(CheckPhotoAsync(photo, checker, blobEndpoint, semaphore, cancellationToken));
        }

        var batches = await Task.WhenAll(tasks);
        return batches.SelectMany(batch => batch).ToList();
    }

    private static async Task<PhotoBlobCheckResult[]> CheckPhotoAsync(
        PhotoItem photo,
        IPhotoBlobChecker checker,
        string blobEndpoint,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var originalUrl = PhotoImageUrl.ToBlobStorageUrl(photo.ImageUrl, blobEndpoint);
            var thumbnailUrl = PhotoImageUrl.ToBlobStorageUrl(photo.ThumbnailUrl, blobEndpoint);
            var originalTask = checker.CheckAsync(originalUrl, cancellationToken);
            var thumbnailTask = checker.CheckAsync(thumbnailUrl, cancellationToken);
            await Task.WhenAll(originalTask, thumbnailTask);

            return
            [
                PhotoBlobCheckResult.FromPhoto(photo, "original", originalUrl, await originalTask),
                PhotoBlobCheckResult.FromPhoto(photo, "thumbnail", thumbnailUrl, await thumbnailTask),
            ];
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static void PrintSummary(PhotoInventoryReport report)
    {
        Console.WriteLine("Photo blob inventory check");
        Console.WriteLine("==========================");
        Console.WriteLine($"Photos checked: {report.PhotosChecked}");
        Console.WriteLine($"Both assets found: {report.BothFound.Count}");
        Console.WriteLine($"Main image missing: {report.HideFromPages.Count}");
        Console.WriteLine($"Thumbnail missing only: {report.ThumbnailMissingOnly.Count}");
        Console.WriteLine($"Both assets missing: {report.BothMissing.Count}");

        if (report.HideFromPages.Count == 0 && report.ThumbnailMissingOnly.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("All checked photos have their main image in blob storage.");
            return;
        }

        if (report.HideFromPages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Hide from photo pages (main image missing):");
            foreach (var photo in report.HideFromPages.Take(50))
            {
                Console.WriteLine(
                    $"  pic_id={photo.PicId} cat_id={photo.CatId} category={photo.CategoryName} thumb={(photo.ThumbnailExists ? "ok" : "missing")} main_status={photo.OriginalStatus} url={photo.OriginalUrl}");
            }

            if (report.HideFromPages.Count > 50)
            {
                Console.WriteLine($"  ... and {report.HideFromPages.Count - 50} more");
            }

            Console.WriteLine();
            Console.WriteLine("Hide pic_id list:");
            Console.WriteLine(string.Join(", ", report.HideFromPages.Select(photo => photo.PicId)));
        }

        if (report.ThumbnailMissingOnly.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Thumbnail missing only (main image exists; may still be displayable on detail page):");
            foreach (var photo in report.ThumbnailMissingOnly.Take(50))
            {
                Console.WriteLine(
                    $"  pic_id={photo.PicId} cat_id={photo.CatId} category={photo.CategoryName} thumb_status={photo.ThumbnailStatus} url={photo.ThumbnailUrl}");
            }

            if (report.ThumbnailMissingOnly.Count > 50)
            {
                Console.WriteLine($"  ... and {report.ThumbnailMissingOnly.Count - 50} more");
            }

            Console.WriteLine();
            Console.WriteLine("Thumbnail-missing pic_id list:");
            Console.WriteLine(string.Join(", ", report.ThumbnailMissingOnly.Select(photo => photo.PicId)));
        }
    }

    private static async Task WriteReportCsvAsync(string outputPath, PhotoInventoryReport report)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync(
            "pic_id,cat_id,category,original_exists,original_status,original_url,thumbnail_exists,thumbnail_status,thumbnail_url,hide_from_pages,thumbnail_missing_only");
        foreach (var photo in report.Rows.OrderBy(row => row.CatId).ThenBy(row => row.PicId))
        {
            await writer.WriteLineAsync(
                $"{photo.PicId},{photo.CatId},{EscapeCsv(photo.CategoryName)},{photo.OriginalExists},{EscapeCsv(photo.OriginalStatus)},{EscapeCsv(photo.OriginalUrl)},{photo.ThumbnailExists},{EscapeCsv(photo.ThumbnailStatus)},{EscapeCsv(photo.ThumbnailUrl)},{photo.HideFromPages},{photo.ThumbnailMissingOnly}");
        }
    }

    private static async Task WriteHideIdsAsync(string outputPath, PhotoInventoryReport report)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(
            outputPath,
            report.HideFromPages.Select(photo => photo.PicId.ToString()));
    }

    private static string EscapeCsv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void WriteUsage(string errorMessage)
    {
        Console.Error.WriteLine(errorMessage);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project src/QueenZone.Tools -- check-photos [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --connection-string <value>   Legacy SQL connection string");
        Console.Error.WriteLine("  --settings-file <path>        appsettings.Local.json path (default: src/QueenZone.Web/appsettings.Local.json)");
        Console.Error.WriteLine("  --category-id <id>            Limit to one legacy category id");
        Console.Error.WriteLine("  --category-slug <slug>        Limit to one category slug");
        Console.Error.WriteLine("  --limit <count>               Stop after this many photos");
        Console.Error.WriteLine("  --concurrency <count>         Parallel checks (default: 8)");
        Console.Error.WriteLine("  --method http|blob            Check via HTTPS HEAD to blob endpoint or Azure Blob SDK (default: http)");
        Console.Error.WriteLine("  --blob-endpoint <url>         Azure blob endpoint for checks (default: https://queenzoneprod.blob.core.windows.net)");
        Console.Error.WriteLine("  --storage-connection-string <value>  Azure Storage connection string for blob SDK checks");
        Console.Error.WriteLine("  --timeout <seconds>           HTTP timeout for http checks (default: 30)");
        Console.Error.WriteLine("  --output <path>               Write one-row-per-photo CSV report");
        Console.Error.WriteLine("  --hide-ids-output <path>      Write pic_id list for photos missing the main image");
        Console.Error.WriteLine("  --dry-run                     List matching photos without checking storage");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Defaults from src/QueenZone.Web/appsettings.Local.json when present:");
        Console.Error.WriteLine("  ConnectionStrings:QueenZoneLegacyLive");
        Console.Error.WriteLine("  ConnectionStrings:BlobStorage");
    }
}

internal enum PhotoCheckMethod
{
    Http,
    Blob,
}

internal sealed class CheckPhotosOptions
{
    private CheckPhotosOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public string StorageConnectionString { get; private init; } = string.Empty;

    public string BlobEndpoint { get; private init; } = "https://queenzoneprod.blob.core.windows.net";

    public int? CategoryId { get; private init; }

    public string? CategorySlug { get; private init; }

    public int? Limit { get; private init; }

    public int Concurrency { get; private init; } = 8;

    public int HttpTimeout { get; private init; } = 30;

    public PhotoCheckMethod Method { get; private init; } = PhotoCheckMethod.Http;

    public string? OutputPath { get; private init; }

    public string? HideIdsOutputPath { get; private init; }

    public bool DryRun { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public CancellationToken CancellationToken => CancellationToken.None;

    public static CheckPhotosOptions Parse(string[] args)
    {
        string? connectionString = null;
        string? storageConnectionString = null;
        string? settingsFile = null;
        string? blobEndpoint = null;
        int? categoryId = null;
        string? categorySlug = null;
        int? limit = null;
        var concurrency = 8;
        var httpTimeout = 30;
        var method = PhotoCheckMethod.Http;
        string? outputPath = null;
        string? hideIdsOutputPath = null;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (ToolArgs.TryReadCommonOption(args, ref index, ref connectionString, ref storageConnectionString, ref settingsFile))
            {
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--blob-endpoint", out var blobEndpointValue))
            {
                blobEndpoint = blobEndpointValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--category-slug", out var categorySlugValue))
            {
                categorySlug = categorySlugValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--output", out var outputPathValue))
            {
                outputPath = outputPathValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--hide-ids-output", out var hideIdsOutputPathValue))
            {
                hideIdsOutputPath = hideIdsOutputPathValue;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--method", out var methodValue))
            {
                if (!Enum.TryParse(methodValue, ignoreCase: true, out PhotoCheckMethod parsedMethod))
                {
                    return Invalid($"Unsupported --method value: {methodValue}");
                }

                method = parsedMethod;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--category-id", out var categoryIdValue))
            {
                if (!int.TryParse(categoryIdValue, out var parsedCategoryId))
                {
                    return Invalid("--category-id must be an integer.");
                }

                categoryId = parsedCategoryId;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--limit", out var limitValue))
            {
                if (!int.TryParse(limitValue, out var parsedLimit) || parsedLimit < 1)
                {
                    return Invalid("--limit must be a positive integer.");
                }

                limit = parsedLimit;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--concurrency", out var concurrencyValue))
            {
                if (!int.TryParse(concurrencyValue, out var parsedConcurrency) || parsedConcurrency < 1)
                {
                    return Invalid("--concurrency must be a positive integer.");
                }

                concurrency = parsedConcurrency;
                continue;
            }

            if (ToolArgs.TryReadValue(args, ref index, "--timeout", out var timeoutValue))
            {
                if (!int.TryParse(timeoutValue, out var parsedTimeout) || parsedTimeout < 1)
                {
                    return Invalid("--timeout must be a positive integer.");
                }

                httpTimeout = parsedTimeout;
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

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("A legacy SQL connection string is required via --connection-string, ConnectionStrings:QueenZoneLegacyLive in appsettings.Local.json, or ConnectionStrings__QueenZoneLegacy.");
        }

        if (method == PhotoCheckMethod.Blob && string.IsNullOrWhiteSpace(storageConnectionString))
        {
            return Invalid("A blob storage connection string is required for blob SDK checks via --storage-connection-string, ConnectionStrings:BlobStorage in appsettings.Local.json, or AzureStorage__ConnectionString.");
        }

        return new CheckPhotosOptions
        {
            ConnectionString = connectionString,
            StorageConnectionString = storageConnectionString ?? string.Empty,
            BlobEndpoint = string.IsNullOrWhiteSpace(blobEndpoint) ? "https://queenzoneprod.blob.core.windows.net" : blobEndpoint.TrimEnd('/'),
            CategoryId = categoryId,
            CategorySlug = categorySlug,
            Limit = limit,
            Concurrency = concurrency,
            HttpTimeout = httpTimeout,
            Method = method,
            OutputPath = outputPath,
            HideIdsOutputPath = hideIdsOutputPath,
            DryRun = dryRun,
            IsValid = true,
        };
    }

    private static CheckPhotosOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

internal sealed record PhotoBlobProbeResult(bool Exists, string Status);

internal sealed class PhotoBlobCheckResult
{
    internal PhotoBlobCheckResult(
        int picId,
        int catId,
        string categoryName,
        string assetType,
        string url,
        bool exists,
        string status)
    {
        PicId = picId;
        CatId = catId;
        CategoryName = categoryName;
        AssetType = assetType;
        Url = url;
        Exists = exists;
        Status = status;
    }

    public int PicId { get; }

    public int CatId { get; }

    public string CategoryName { get; }

    public string AssetType { get; }

    public string Url { get; }

    public bool Exists { get; }

    public string Status { get; }

    public static PhotoBlobCheckResult FromPhoto(
        PhotoItem photo,
        string assetType,
        string url,
        PhotoBlobProbeResult probe) =>
        new(photo.PicId, photo.CatId, photo.CategoryName, assetType, url, probe.Exists, probe.Status);
}

internal sealed class PhotoInventoryRow
{
    public int PicId { get; init; }

    public int CatId { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public bool OriginalExists { get; init; }

    public string OriginalStatus { get; init; } = string.Empty;

    public string OriginalUrl { get; init; } = string.Empty;

    public bool ThumbnailExists { get; init; }

    public string ThumbnailStatus { get; init; } = string.Empty;

    public string ThumbnailUrl { get; init; } = string.Empty;

    public bool HideFromPages => !OriginalExists;

    public bool ThumbnailMissingOnly => OriginalExists && !ThumbnailExists;
}

internal sealed class PhotoInventoryReport
{
    public IReadOnlyList<PhotoInventoryRow> Rows { get; init; } = [];

    public int PhotosChecked => Rows.Count;

    public IReadOnlyList<PhotoInventoryRow> BothFound { get; init; } = [];

    public IReadOnlyList<PhotoInventoryRow> HideFromPages { get; init; } = [];

    public IReadOnlyList<PhotoInventoryRow> ThumbnailMissingOnly { get; init; } = [];

    public IReadOnlyList<PhotoInventoryRow> BothMissing { get; init; } = [];

    public static PhotoInventoryReport FromAssetResults(IReadOnlyList<PhotoBlobCheckResult> assetResults)
    {
        var rows = assetResults
            .GroupBy(result => result.PicId)
            .Select(group =>
            {
                var original = group.First(result => string.Equals(result.AssetType, "original", StringComparison.Ordinal));
                var thumbnail = group.First(result => string.Equals(result.AssetType, "thumbnail", StringComparison.Ordinal));
                return new PhotoInventoryRow
                {
                    PicId = original.PicId,
                    CatId = original.CatId,
                    CategoryName = original.CategoryName,
                    OriginalExists = original.Exists,
                    OriginalStatus = original.Status,
                    OriginalUrl = original.Url,
                    ThumbnailExists = thumbnail.Exists,
                    ThumbnailStatus = thumbnail.Status,
                    ThumbnailUrl = thumbnail.Url,
                };
            })
            .OrderBy(row => row.CatId)
            .ThenBy(row => row.PicId)
            .ToList();

        return new PhotoInventoryReport
        {
            Rows = rows,
            BothFound = rows.Where(row => row.OriginalExists && row.ThumbnailExists).ToList(),
            HideFromPages = rows.Where(row => row.HideFromPages).ToList(),
            ThumbnailMissingOnly = rows.Where(row => row.ThumbnailMissingOnly).ToList(),
            BothMissing = rows.Where(row => !row.OriginalExists && !row.ThumbnailExists).ToList(),
        };
    }
}

internal interface IPhotoBlobChecker
{
    Task<PhotoBlobProbeResult> CheckAsync(string publicUrl, CancellationToken cancellationToken);
}

internal sealed class HttpPhotoBlobChecker : IPhotoBlobChecker, IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool disposeClient;

    public HttpPhotoBlobChecker(int timeoutSeconds)
        : this(CreateClient(timeoutSeconds), disposeClient: true)
    {
    }

    internal HttpPhotoBlobChecker(HttpClient httpClient, bool disposeClient = false)
    {
        this.httpClient = httpClient;
        this.disposeClient = disposeClient;
    }

    private static HttpClient CreateClient(int timeoutSeconds) =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

    public async Task<PhotoBlobProbeResult> CheckAsync(string publicUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, publicUrl);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return new PhotoBlobProbeResult(response.IsSuccessStatusCode, ((int)response.StatusCode).ToString());
    }

    public ValueTask DisposeAsync()
    {
        if (disposeClient)
        {
            httpClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class AzureBlobPhotoChecker(string connectionString) : IPhotoBlobChecker
{
    private readonly BlobServiceClient blobServiceClient = new(connectionString);

    public async Task<PhotoBlobProbeResult> CheckAsync(string blobUrl, CancellationToken cancellationToken)
    {
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            return new PhotoBlobProbeResult(false, "invalid-url");
        }

        var blobClient = blobServiceClient
            .GetBlobContainerClient(container)
            .GetBlobClient(blobName);

        var exists = await blobClient.ExistsAsync(cancellationToken);
        return new PhotoBlobProbeResult(exists.Value, exists.Value ? "200" : "404");
    }
}

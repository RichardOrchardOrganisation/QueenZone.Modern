namespace QueenZone.Data;

/// <summary>
/// Builds public image URLs from legacy PIC_FILES_T paths (e.g. "/Brian_May/3120008212.jpg").
/// cdn.queenzone.org is a straight Cloudflare CDN proxy to Azure Blob Storage, so the first
/// path segment must match the Azure container name exactly (lowercased, underscores as hyphens).
/// </summary>
public static class PhotoImageUrl
{
    private const string DefaultPublicBaseUrl = "https://cdn.queenzone.org";
    private const string DefaultBlobStorageBaseUrl = "https://queenzoneprod.blob.core.windows.net";

    public const string PublicBaseUrlEnvironmentVariable = "QUEENZONE_PHOTO_PUBLIC_BASE_URL";

    public const string BlobEndpointEnvironmentVariable = "QUEENZONE_PHOTO_BLOB_ENDPOINT";

    /// <summary>
    /// Legacy PIC_FILES_T folders that do not match the Azure container after the usual
    /// underscore→hyphen lowercasing. Keys are already normalized container candidates.
    /// </summary>
    private static readonly Dictionary<string, string> ContainerAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Category "US Queen Convention 2001" → folder US_Queen_Convention_2001, but blobs live in us-convention-2001.
            ["us-queen-convention-2001"] = "us-convention-2001",
        };

    public static string Build(string legacyPath)
    {
        return BuildFromBase(
            Environment.GetEnvironmentVariable(PublicBaseUrlEnvironmentVariable) ?? DefaultPublicBaseUrl,
            legacyPath);
    }

    public static string BuildBlobStorageUrl(string legacyPath, string? blobEndpoint = null) =>
        BuildFromBase(
            blobEndpoint
                ?? Environment.GetEnvironmentVariable(BlobEndpointEnvironmentVariable)
                ?? DefaultBlobStorageBaseUrl,
            legacyPath);

    public static string ToBlobStorageUrl(string url, string? blobEndpoint = null)
    {
        var endpoint = (blobEndpoint
            ?? Environment.GetEnvironmentVariable(BlobEndpointEnvironmentVariable)
            ?? DefaultBlobStorageBaseUrl).TrimEnd('/');
        if (TryParseBlobLocation(url, out var container, out var blobName))
        {
            return $"{endpoint}/{NormalizeContainer(container)}/{blobName}";
        }

        return BuildBlobStorageUrl(url, endpoint);
    }

    private static string BuildFromBase(string baseUrl, string legacyPath)
    {
        var trimmed = legacyPath.TrimStart('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return $"{baseUrl.TrimEnd('/')}/{trimmed}";
        }

        var folder = NormalizeContainer(segments[0]);
        var fileName = segments[^1];
        return $"{baseUrl.TrimEnd('/')}/{folder}/{fileName}";
    }

    /// <summary>
    /// Parses a public CDN URL into the Azure Blob container and blob name.
    /// </summary>
    public static bool TryParseBlobLocation(string publicUrl, out string container, out string blobName)
    {
        container = string.Empty;
        blobName = string.Empty;
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimStart('/');
        var slashIndex = path.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= path.Length - 1)
        {
            return false;
        }

        container = NormalizeContainer(Uri.UnescapeDataString(path[..slashIndex]));
        blobName = Uri.UnescapeDataString(path[(slashIndex + 1)..]);
        return container.Length > 0 && blobName.Length > 0;
    }

    internal static string NormalizeContainer(string folderOrContainer)
    {
        var normalized = folderOrContainer.Trim().ToLowerInvariant().Replace('_', '-');
        return ContainerAliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using QueenZone.Data;

namespace QueenZone.Tools;

internal sealed record PhotoCandidate(int Id, int CategoryId, string? Url, string? ThumbUrl, bool Required);

internal sealed record PhotoSelection(IReadOnlyList<int> PhotoIds, IReadOnlyList<SnapshotBlob> Blobs);

internal sealed record MissingForumBlobReference(long? LegacyPostId, Guid? AttachmentId);

internal sealed record MissingEditorialBlobReference(int? LegacyNewsId, Guid? EditorialArticleId, bool IsLive);

[ExcludeFromCodeCoverage]
internal sealed record ReferencedBlobSelection(
    IReadOnlyList<SnapshotBlob> Blobs,
    IReadOnlyList<MissingForumBlobReference> MissingForumBlobs,
    IReadOnlyList<MissingEditorialBlobReference> MissingEditorialBlobs);

[ExcludeFromCodeCoverage]
internal sealed class BlobSnapshotService(
    DevSnapshotConfig config,
    string sourceConnectionString,
    string targetConnectionString)
{
    private const int MaximumParallelTransfers = 16;
    private const string SourceVersionMetadataName = "qzsourceversion";
    private const string SnapshotFormatMetadataName = "qzsnapshotformat";
    private const string SnapshotFormatVersion = "1";
    private readonly BlobServiceClient source = new(sourceConnectionString);
    private readonly BlobServiceClient target = new(targetConnectionString);

    public async Task<PhotoSelection> SelectPhotosAsync(IReadOnlyList<PhotoCandidate> candidates)
    {
        var ids = new List<int>();
        var blobs = new List<SnapshotBlob>();
        var acceptedBytes = 0L;

        foreach (var candidate in candidates)
        {
            var candidateBlobs = await ResolvePhotoAssetsAsync(candidate);

            if (candidateBlobs.Count == 0)
            {
                if (candidate.Required)
                {
                    throw new InvalidOperationException($"Required gallery photo {candidate.Id} has missing assets.");
                }
                continue;
            }

            var newBytes = candidateBlobs.Sum(blob => blob.Bytes);
            if (acceptedBytes + newBytes > config.GalleryBudgetBytes)
            {
                if (candidate.Required)
                {
                    throw new InvalidOperationException($"Required gallery photo {candidate.Id} exceeds the gallery budget.");
                }
                continue;
            }

            acceptedBytes += newBytes;
            ids.Add(candidate.Id);
            blobs.AddRange(candidateBlobs);
        }

        var missingCategories = candidates.Select(item => item.CategoryId).Distinct()
            .Except(candidates.Where(item => ids.Contains(item.Id)).Select(item => item.CategoryId))
            .ToArray();
        if (missingCategories.Length > 0)
        {
            throw new InvalidOperationException($"No valid in-budget photo was found for categories: {string.Join(", ", missingCategories)}.");
        }

        return new PhotoSelection(ids, blobs);
    }

    private async Task<List<SnapshotBlob>> ResolvePhotoAssetsAsync(PhotoCandidate candidate)
    {
        var candidateBlobs = new List<SnapshotBlob>();
        foreach (var (path, kind) in new[] { (candidate.Url, "gallery-original"), (candidate.ThumbUrl, "gallery-thumbnail") })
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                candidateBlobs.Clear();
                break;
            }

            if (!TryParseGalleryLocation(path, out var container, out var name))
            {
                candidateBlobs.Clear();
                break;
            }

            var blob = await ResolveAsync(container, name, "gallery", $"PIC_FILES_T:{candidate.Id}:{kind}");
            if (blob is null)
            {
                candidateBlobs.Clear();
                break;
            }

            candidateBlobs.Add(blob);
        }
        return candidateBlobs;
    }

    public async Task<ReferencedBlobSelection> GetForumAndEditorialBlobsAsync(SqlSnapshotCopySession session)
    {
        var references = await session.GetBlobReferencesAsync();
        var result = new List<SnapshotBlob>();
        var missingForumBlobs = new List<MissingForumBlobReference>();
        var missingEditorialBlobs = new List<MissingEditorialBlobReference>();
        foreach (var reference in references)
        {
            var blob = await ResolveAsync(reference.Container, reference.Name, reference.Budget, reference.Source);
            if (blob is null)
            {
                if (string.Equals(reference.Budget, "forum", StringComparison.OrdinalIgnoreCase))
                {
                    missingForumBlobs.Add(ParseMissingForumBlobReference(reference.Source));
                    continue;
                }

                missingEditorialBlobs.Add(ParseMissingEditorialBlobReference(reference.Source));
                continue;
            }

            result.Add(blob);
        }

        return new ReferencedBlobSelection(result, missingForumBlobs, missingEditorialBlobs);
    }

    public void EnsureBudgets(IEnumerable<SnapshotBlob> manifest)
    {
        var gallery = manifest.Where(blob => blob.Budget == "gallery").Sum(blob => blob.Bytes);
        var forum = manifest.Where(blob => blob.Budget == "forum").Sum(blob => blob.Bytes);
        if (gallery > config.GalleryBudgetBytes)
        {
            throw new InvalidOperationException($"Gallery blob budget exceeded: {gallery} > {config.GalleryBudgetBytes} bytes.");
        }

        if (forum > config.ForumAttachmentBudgetBytes)
        {
            throw new InvalidOperationException($"Forum attachment budget exceeded: {forum} > {config.ForumAttachmentBudgetBytes} bytes.");
        }
    }

    public async Task ReconcileTargetAsync(IReadOnlyList<SnapshotBlob> manifest)
    {
        var desired = manifest.ToDictionary(
            item => BlobKey(item.Container, item.Name),
            StringComparer.Ordinal);
        var existing = await GetTargetInventoryAsync();

        await Parallel.ForEachAsync(
            manifest.Select(item => item.Container).Distinct(StringComparer.Ordinal),
            ParallelOptions(),
            async (container, cancellationToken) =>
                await target.GetBlobContainerClient(container).CreateIfNotExistsAsync(cancellationToken: cancellationToken));

        var obsolete = existing
            .Where(item => !desired.ContainsKey(item.Key))
            .Select(item => item.Value)
            .ToArray();
        await Parallel.ForEachAsync(obsolete, ParallelOptions(), async (item, cancellationToken) =>
        {
            await target.GetBlobContainerClient(item.Container)
                .DeleteBlobIfExistsAsync(item.Name, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        });

        var copied = 0;
        var skipped = 0;
        await Parallel.ForEachAsync(manifest, ParallelOptions(), async (item, cancellationToken) =>
        {
            var key = BlobKey(item.Container, item.Name);
            if (existing.TryGetValue(key, out var targetBlob) && IsCurrent(item, targetBlob.Bytes, targetBlob.Metadata))
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            var sourceBlob = source.GetBlobContainerClient(item.Container).GetBlobClient(item.Name);
            var targetBlobClient = target.GetBlobContainerClient(item.Container).GetBlockBlobClient(item.Name);
            var sourceUri = CreateSourceReadUri(sourceBlob, DateTimeOffset.UtcNow);
            await targetBlobClient.SyncUploadFromUriAsync(sourceUri, new BlobSyncUploadFromUriOptions
            {
                CopySourceBlobProperties = false,
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = item.ContentType,
                    CacheControl = item.CacheControl,
                    ContentDisposition = item.ContentDisposition,
                    ContentEncoding = item.ContentEncoding,
                    ContentLanguage = item.ContentLanguage,
                },
                Metadata = SnapshotMetadata(item),
                SourceConditions = new BlobRequestConditions { IfMatch = new ETag(item.SourceETag) },
            }, cancellationToken);

            var completed = Interlocked.Increment(ref copied);
            if (completed % 100 == 0)
            {
                Console.WriteLine($"Server-side blob copies completed: {completed}/{manifest.Count}.");
            }
        });

        await VerifyTargetAsync(manifest);
        Console.WriteLine($"Blob reconciliation complete: {copied} copied, {skipped} unchanged, {obsolete.Length} obsolete deleted.");
    }

    private async Task<Dictionary<string, TargetBlob>> GetTargetInventoryAsync()
    {
        var result = new Dictionary<string, TargetBlob>(StringComparer.Ordinal);
        await foreach (var container in target.GetBlobContainersAsync())
        {
            var client = target.GetBlobContainerClient(container.Name);
            await foreach (var blob in client.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }))
            {
                result.Add(
                    BlobKey(container.Name, blob.Name),
                    new TargetBlob(container.Name, blob.Name, blob.Properties.ContentLength, blob.Metadata));
            }
        }

        return result;
    }

    private async Task VerifyTargetAsync(IReadOnlyList<SnapshotBlob> manifest)
    {
        var actual = await GetTargetInventoryAsync();
        if (actual.Count != manifest.Count)
        {
            throw new InvalidOperationException($"Target blob count mismatch: expected {manifest.Count}, found {actual.Count}.");
        }

        foreach (var item in manifest)
        {
            if (!actual.TryGetValue(BlobKey(item.Container, item.Name), out var targetBlob)
                || !IsCurrent(item, targetBlob.Bytes, targetBlob.Metadata))
            {
                throw new InvalidOperationException($"Target blob verification failed for {item.Container}/{item.Name}.");
            }
        }
    }

    private static ParallelOptions ParallelOptions() => new() { MaxDegreeOfParallelism = MaximumParallelTransfers };

    private static string BlobKey(string container, string name) => $"{container}\n{name}";

    internal static bool IsCurrent(SnapshotBlob sourceBlob, long? targetBytes, IDictionary<string, string>? metadata) =>
        targetBytes == sourceBlob.Bytes
        && metadata is not null
        && metadata.TryGetValue(SourceVersionMetadataName, out var sourceVersion)
        && string.Equals(sourceVersion, SourceVersion(sourceBlob.SourceETag), StringComparison.Ordinal)
        && metadata.TryGetValue(SnapshotFormatMetadataName, out var snapshotFormat)
        && string.Equals(snapshotFormat, SnapshotFormatVersion, StringComparison.Ordinal);

    internal static Uri CreateSourceReadUri(BlobClient sourceBlob, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(sourceBlob.Uri.Query))
        {
            return sourceBlob.Uri;
        }

        if (!sourceBlob.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                $"Cannot create a read-only source URL for {sourceBlob.BlobContainerName}/{sourceBlob.Name}. "
                + "The source connection must use a shared key or include a read-only SAS token.");
        }

        var sas = new BlobSasBuilder
        {
            BlobContainerName = sourceBlob.BlobContainerName,
            BlobName = sourceBlob.Name,
            Resource = "b",
            StartsOn = now.AddMinutes(-5),
            ExpiresOn = now.AddHours(4),
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return sourceBlob.GenerateSasUri(sas);
    }

    private static Dictionary<string, string> SnapshotMetadata(SnapshotBlob item) => new(StringComparer.OrdinalIgnoreCase)
    {
        [SourceVersionMetadataName] = SourceVersion(item.SourceETag),
        [SnapshotFormatMetadataName] = SnapshotFormatVersion,
    };

    internal static string SourceVersion(string etag) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(etag)));

    private async Task<SnapshotBlob?> ResolveAsync(string container, string name, string budget, string reference)
    {
        try
        {
            var properties = await source.GetBlobContainerClient(container).GetBlobClient(name).GetPropertiesAsync();
            return new SnapshotBlob(
                container,
                name,
                budget,
                properties.Value.ContentLength,
                reference,
                properties.Value.ETag.ToString(),
                properties.Value.ContentType,
                properties.Value.CacheControl,
                properties.Value.ContentDisposition,
                properties.Value.ContentEncoding,
                properties.Value.ContentLanguage);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    internal static bool TryParseGalleryLocation(string path, out string container, out string name)
    {
        var isHttpUrl = Uri.TryCreate(path, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (uri?.IsAbsoluteUri == true && !isHttpUrl && !path.StartsWith("/", StringComparison.Ordinal))
        {
            container = string.Empty;
            name = string.Empty;
            return false;
        }

        var absolute = isHttpUrl ? path : PhotoImageUrl.BuildBlobStorageUrl(path);
        return PhotoImageUrl.TryParseBlobLocation(absolute, out container, out name);
    }

    internal static MissingForumBlobReference ParseMissingForumBlobReference(string source)
    {
        const string legacyPrefix = "ModernForumPost:";
        if (source.StartsWith(legacyPrefix, StringComparison.Ordinal)
            && long.TryParse(source[legacyPrefix.Length..], out var legacyPostId))
        {
            return new MissingForumBlobReference(legacyPostId, null);
        }

        const string attachmentPrefix = "ForumPostAttachments:";
        if (source.StartsWith(attachmentPrefix, StringComparison.Ordinal)
            && Guid.TryParse(source[attachmentPrefix.Length..], out var attachmentId))
        {
            return new MissingForumBlobReference(null, attachmentId);
        }

        throw new InvalidOperationException($"Unknown forum blob reference source: {source}.");
    }

    internal static MissingEditorialBlobReference ParseMissingEditorialBlobReference(string source)
    {
        const string newsPrefix = "NEWS_T:";
        if (source.StartsWith(newsPrefix, StringComparison.Ordinal)
            && int.TryParse(source[newsPrefix.Length..], out var newsId))
        {
            return new MissingEditorialBlobReference(newsId, null, false);
        }

        const string articlePrefix = "EditorialArticles:";
        if (source.StartsWith(articlePrefix, StringComparison.Ordinal)
            && Guid.TryParse(source[articlePrefix.Length..], out var articleId))
        {
            return new MissingEditorialBlobReference(null, articleId, false);
        }

        const string liveArticlePrefix = "EditorialArticles-live:";
        if (source.StartsWith(liveArticlePrefix, StringComparison.Ordinal)
            && Guid.TryParse(source[liveArticlePrefix.Length..], out var liveArticleId))
        {
            return new MissingEditorialBlobReference(null, liveArticleId, true);
        }

        throw new InvalidOperationException($"Unknown editorial blob reference source: {source}.");
    }

    private sealed record TargetBlob(
        string Container,
        string Name,
        long? Bytes,
        IDictionary<string, string> Metadata);
}

using System.Runtime.CompilerServices;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Storage;

/// <summary>
/// Uploads, reads, and deletes public gallery blobs in legacy CDN containers (not UGC containers).
/// </summary>
public interface IGalleryPhotoBlobService
{
    Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the blob if it exists. Missing blobs are a no-op.
    /// </summary>
    Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams blobs in the container along with their last-modified time, one service page at
    /// a time, so a large container is never held in memory. Yields nothing if the container
    /// does not exist.
    /// </summary>
    IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
        string containerName,
        CancellationToken cancellationToken = default);

    bool IsConfigured { get; }
}

public sealed record GalleryBlobDescriptor(string BlobName, DateTimeOffset LastModified);

public sealed class NullGalleryPhotoBlobService : IGalleryPhotoBlobService
{
    private readonly Dictionary<string, byte[]> blobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> lastModified = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public bool IsConfigured => true;

    public Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        lock (sync)
        {
            var key = Key(containerName, blobName);
            blobs[key] = buffer.ToArray();
            lastModified[key] = TimeProvider.GetUtcNow();
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (!blobs.TryGetValue(Key(containerName, blobName), out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }
    }

    public Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var key = Key(containerName, blobName);
            blobs.Remove(key);
            lastModified.Remove(key);
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        var prefix = containerName + "/";
        lock (sync)
        {
            return blobs.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(key => new GalleryBlobDescriptor(key[prefix.Length..], lastModified[key]))
                .ToList()
                .ToAsyncEnumerable();
        }
    }

    private static string Key(string containerName, string blobName) =>
        $"{containerName}/{blobName}";
}

public sealed class AzureGalleryPhotoBlobService(BlobServiceClient blobServiceClient) : IGalleryPhotoBlobService
{
    public bool IsConfigured => true;

    public async Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var blob = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var blob = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        return await blob.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var blob = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
        string containerName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken))
        {
            yield break;
        }

        // GetBlobsAsync fetches one service page (up to 5,000 names) per round trip.
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            yield return new GalleryBlobDescriptor(blob.Name, blob.Properties.LastModified ?? DateTimeOffset.MinValue);
        }
    }
}

public static class GalleryPhotoBlobServiceCollectionExtensions
{
    public static IServiceCollection AddGalleryPhotoBlobService(this IServiceCollection services)
    {
        services.AddSingleton<IGalleryPhotoBlobService>(sp =>
        {
            var blobServiceClient = sp.GetService<BlobServiceClient>();
            return blobServiceClient is null
                ? new NullGalleryPhotoBlobService()
                : new AzureGalleryPhotoBlobService(blobServiceClient);
        });
        return services;
    }
}

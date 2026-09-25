using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Lazy single-track fallback for legacy rows without a stored duration.
/// </summary>
public sealed class FanPerformanceDurationResolver(
    IBlobUploadService blobUploadService,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    public async Task<int?> ResolveAsync(FanPerformance performance, CancellationToken cancellationToken)
    {
        if (performance.DurationSeconds is int seconds)
        {
            return seconds;
        }

        var cacheKey =
            $"fan-performance-duration:{performance.Id}:{performance.FileSizeBytes}:{performance.AudioFileName}";
        if (cache.TryGetValue(cacheKey, out CachedDuration cached))
        {
            return cached.Seconds;
        }

        var fromBlob = await TryReadFromBlobAsync(performance, cancellationToken);
        cache.Set(cacheKey, new CachedDuration(fromBlob), CacheLifetime);
        return fromBlob;
    }

    private async Task<int?> TryReadFromBlobAsync(
        FanPerformance performance,
        CancellationToken cancellationToken)
    {
        if (!SongFileUrl.IsSafeBlobName(performance.AudioFileName))
        {
            return null;
        }

        var blobName = SongFileUrl.GetBlobName(performance.AudioFileName);
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return null;
        }

        try
        {
            await using var content = await blobUploadService.OpenReadAsync(
                SongFileUrl.ContainerName,
                blobName,
                cancellationToken);
            if (content is null)
            {
                return null;
            }

            var prefix = new byte[Mp3Duration.PrefixBytes];
            var read = await ReadPrefixAsync(content.Stream, prefix, cancellationToken);
            if (read <= 0)
            {
                return null;
            }

            var length = performance.FileSizeBytes;
            if (content.Stream.CanSeek && content.Stream.Length > 0)
            {
                length = content.Stream.Length;
            }

            return Mp3Duration.TryGetSeconds(prefix.AsSpan(0, read), length);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<int> ReadPrefixAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private readonly record struct CachedDuration(int? Seconds);
}

using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class Mp3DurationTests
{
    [Fact]
    public void TryGetSeconds_ReturnsNull_ForEmptyPrefix()
    {
        Assert.Null(Mp3Duration.TryGetSeconds([], 16_000));
        Assert.Null(Mp3Duration.TryGetSeconds([0xFF, 0xFB, 0x90, 0x00], 0));
    }

    [Fact]
    public void TryGetSeconds_UsesCbrBitrateAndFileLength()
    {
        var prefix = CreateMpeg1Layer3Header(bitrateIndex: 9);
        var seconds = Mp3Duration.TryGetSeconds(prefix, fileLengthBytes: 16_000);

        Assert.Equal(1, seconds);
    }

    [Fact]
    public void TryGetSeconds_SkipsId3v2Tag()
    {
        var header = CreateMpeg1Layer3Header(bitrateIndex: 9);
        var prefix = new byte[18];
        prefix[0] = (byte)'I';
        prefix[1] = (byte)'D';
        prefix[2] = (byte)'3';
        prefix[9] = 4;
        header.CopyTo(prefix.AsSpan(14));

        var seconds = Mp3Duration.TryGetSeconds(prefix, fileLengthBytes: 16_014);
        Assert.Equal(1, seconds);
    }

    [Fact]
    public void TryGetSeconds_ReturnsNull_WhenNoMpegHeader()
    {
        Assert.Null(Mp3Duration.TryGetSeconds("not-an-mp3"u8.ToArray(), 16_000));
    }

    [Fact]
    public void TryGetSeconds_ReturnsNull_WhenId3TagExtendsPastPrefix()
    {
        var prefix = new byte[12];
        prefix[0] = (byte)'I';
        prefix[1] = (byte)'D';
        prefix[2] = (byte)'3';
        prefix[9] = 20;

        Assert.Null(Mp3Duration.TryGetSeconds(prefix, 16_000));
    }

    [Fact]
    public void TryGetSeconds_ReturnsNull_WhenFileEndsAtMpegHeader()
    {
        var prefix = CreateMpeg1Layer3Header(bitrateIndex: 9);

        Assert.Null(Mp3Duration.TryGetSeconds(prefix, fileLengthBytes: 4));
    }

    [Fact]
    public void TryGetSeconds_UsesMpeg2Layer3BitrateTable()
    {
        // MPEG-2 Layer III, protection bit set, bitrate index 9 = 80 kbps.
        var prefix = new byte[] { 0xFF, 0xF3, 0x90, 0x00 };
        var seconds = Mp3Duration.TryGetSeconds(prefix, fileLengthBytes: 10_000);

        Assert.Equal(1, seconds);
    }

    internal static byte[] CreateMpeg1Layer3Header(int bitrateIndex)
    {
        // MPEG-1 Layer III, no CRC. Byte2 high nibble is the bitrate index
        // (9 = 128 kbps). Sample rate 44100 (00) and no padding.
        return [0xFF, 0xFB, (byte)(bitrateIndex << 4), 0x00];
    }
}

public sealed class FanPerformanceDurationResolverTests
{
    [Fact]
    public async Task ResolveAsync_FallsBackToDomainDuration_WhenBlobMissing()
    {
        var performance = SampleFanPerformanceData.CreateSeedPerformances()[0];
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        var seconds = await resolver.ResolveAsync(performance, CancellationToken.None);

        Assert.Equal(320, seconds);
    }

    [Fact]
    public async Task ResolveAsync_PrefersMpegDuration_WhenBlobIsReadable()
    {
        var performance = new FanPerformance(
            1,
            "Test",
            "Fan",
            "",
            "probe.mp3",
            16_000,
            DateTime.UtcNow,
            DurationSeconds: 99);
        var blobs = new MemoryBlobUploadService();
        await using var payload = new MemoryStream(CreateCbrPayload(16_000));
        await blobs.UploadAsync(
            payload,
            "probe.mp3",
            SongFileUrl.ContainerName,
            new BlobUploadContext { PreferredBlobName = "probe.mp3" });

        var resolver = new FanPerformanceDurationResolver(blobs, new MemoryCache(new MemoryCacheOptions()));
        var seconds = await resolver.ResolveAsync(performance, CancellationToken.None);

        Assert.Equal(1, seconds);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenBlobAndDomainDurationMissing()
    {
        var performance = new FanPerformance(2, "Silent", "Fan", "", "missing.mp3", 10, DateTime.UtcNow);
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync(performance, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenFilenameIsUnsafe()
    {
        var performance = new FanPerformance(4, "Secret", "Fan", "", "../secret.mp3", 10, DateTime.UtcNow);
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync(performance, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveManyAsync_ReturnsEmpty_WhenThereAreNoItems()
    {
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        var durations = await resolver.ResolveManyAsync([], CancellationToken.None);

        Assert.Empty(durations);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenStorageIsNotConfigured()
    {
        var performance = new FanPerformance(3, "Local", "Fan", "", "local.mp3", 10, DateTime.UtcNow);
        var resolver = new FanPerformanceDurationResolver(new NullBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync(performance, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveManyAsync_PreservesItemOrder()
    {
        var items = SampleFanPerformanceData.CreateSeedPerformances();
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        var durations = await resolver.ResolveManyAsync(items, CancellationToken.None);

        Assert.Equal(items.Select(item => item.DurationSeconds), durations);
    }

    [Fact]
    public async Task ResolveAsync_UsesCache_OnSecondCall()
    {
        var performance = SampleFanPerformanceData.CreateSeedPerformances()[0];
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new FanPerformanceDurationResolver(new MemoryBlobUploadService(), cache);

        var first = await resolver.ResolveAsync(performance, CancellationToken.None);
        var second = await resolver.ResolveAsync(performance, CancellationToken.None);

        Assert.Equal(320, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ResolveAsync_CachesFallbackAfterBlobReadFailure()
    {
        var performance = new FanPerformance(55, "Test", "Fan", "", "track.mp3", 10,
            DateTime.UtcNow, DurationSeconds: 42);
        var blobs = new FailingReadBlobService();
        var resolver = new FanPerformanceDurationResolver(blobs, new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal(42, await resolver.ResolveAsync(performance, CancellationToken.None));
        Assert.Equal(42, await resolver.ResolveAsync(performance, CancellationToken.None));
        Assert.Equal(1, blobs.ReadCount);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackWhenBlobStreamIsEmpty()
    {
        var performance = new FanPerformance(56, "Test", "Fan", "", "empty.mp3", 10,
            DateTime.UtcNow, DurationSeconds: 17);
        var resolver = new FanPerformanceDurationResolver(new EmptyReadBlobService(),
            new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal(17, await resolver.ResolveAsync(performance, CancellationToken.None));
    }

    private sealed class FailingReadBlobService : IBlobUploadService
    {
        public int ReadCount { get; private set; }

        public Task<BlobContent?> OpenReadAsync(string containerName, string blobName,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            throw new IOException("read failed");
        }

        public Task<BlobUploadResult> UploadAsync(Stream content, string originalFileName,
            string containerName, BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string containerName, string blobName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyReadBlobService : IBlobUploadService
    {
        public Task<BlobContent?> OpenReadAsync(string containerName, string blobName,
            CancellationToken cancellationToken = default) => Task.FromResult<BlobContent?>(new BlobContent
            {
                Stream = new MemoryStream(),
                ContentType = "audio/mpeg",
            });

        public Task<BlobUploadResult> UploadAsync(Stream content, string originalFileName,
            string containerName, BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string containerName, string blobName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static byte[] CreateCbrPayload(int length)
    {
        var bytes = new byte[length];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        return bytes;
    }
}

public sealed class ContentApiFanPerformanceMapperTests
{
    [Fact]
    public void ToFanPerformanceDto_UsesWebsiteListingPathAndApiAudioPath()
    {
        var performance = SampleFanPerformanceData.CreateSeedPerformances()[0];

        var dto = ContentApiMapper.ToFanPerformanceDto(performance, 320);

        Assert.Equal(187, dto.Id);
        Assert.Equal("Reaching Out", dto.Title);
        Assert.Equal("Mike Ryde", dto.PerformedBy);
        Assert.Equal(320, dto.DurationSeconds);
        Assert.Equal("/fan-performances", dto.DetailPath);
        Assert.Equal("/api/v1/content/fan-performances/187/audio", dto.AudioPath);
    }

    [Fact]
    public void ToFanPerformanceDtos_PairsDurationsByIndex()
    {
        var items = SampleFanPerformanceData.CreateSeedPerformances().Take(2).ToList();

        var mapped = ContentApiMapper.ToFanPerformanceDtos(items, [11, 22]);

        Assert.Equal(11, mapped[0].DurationSeconds);
        Assert.Equal(22, mapped[1].DurationSeconds);
    }

    [Fact]
    public void ToFanPerformanceDtos_FallsBackToDomainDuration_WhenListIsShorter()
    {
        var items = SampleFanPerformanceData.CreateSeedPerformances().Take(2).ToList();

        var mapped = ContentApiMapper.ToFanPerformanceDtos(items, [11]);

        Assert.Equal(11, mapped[0].DurationSeconds);
        Assert.Equal(items[1].DurationSeconds, mapped[1].DurationSeconds);
    }
}

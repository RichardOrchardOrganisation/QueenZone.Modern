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
    public async Task ResolveAsync_UsesStoredDuration_WhenBlobIsReadable()
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

        Assert.Equal(99, seconds);
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
    public async Task ResolveAsync_ReturnsNull_WhenStorageIsNotConfigured()
    {
        var performance = new FanPerformance(3, "Local", "Fan", "", "local.mp3", 10, DateTime.UtcNow);
        var resolver = new FanPerformanceDurationResolver(new NullBlobUploadService(), new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync(performance, CancellationToken.None));
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
    public void ToFanPerformanceDtos_UsesStoredDurations()
    {
        var items = SampleFanPerformanceData.CreateSeedPerformances().Take(2).ToList();

        var mapped = ContentApiMapper.ToFanPerformanceDtos(items);

        Assert.Equal(items[0].DurationSeconds, mapped[0].DurationSeconds);
        Assert.Equal(items[1].DurationSeconds, mapped[1].DurationSeconds);
    }

}

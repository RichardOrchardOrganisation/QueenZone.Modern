using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceSubmissionPurgeServiceTests
{
    [Fact]
    public void IsPurgeEligible_OnlyRejectedAndWithdrawnPastCutoffWithBlobPath()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var oldRejected = Sample(
            FanPerformanceSubmissionStatus.Rejected,
            "pending/old.mp3",
            cutoff.AddDays(-1));
        var recentRejected = Sample(
            FanPerformanceSubmissionStatus.Rejected,
            "pending/recent.mp3",
            cutoff.AddDays(1));
        var oldPending = Sample(
            FanPerformanceSubmissionStatus.Pending,
            "pending/pending.mp3",
            cutoff.AddDays(-40));
        var withdrawnNoBlob = Sample(
            FanPerformanceSubmissionStatus.Withdrawn,
            string.Empty,
            cutoff.AddDays(-40));

        Assert.True(FanPerformanceSubmissionPurgeService.IsPurgeEligible(oldRejected, cutoff));
        Assert.False(FanPerformanceSubmissionPurgeService.IsPurgeEligible(recentRejected, cutoff));
        Assert.False(FanPerformanceSubmissionPurgeService.IsPurgeEligible(oldPending, cutoff));
        Assert.False(FanPerformanceSubmissionPurgeService.IsPurgeEligible(withdrawnNoBlob, cutoff));
    }

    [Fact]
    public async Task PurgeAsync_DeletesEligiblePendingBlobsAndClearsPath()
    {
        var repository = new InMemoryFanPerformanceSubmissionRepository();
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var memberId = Guid.NewGuid();

        var rejected = await repository.CreateAsync(NewSubmission(memberId, "old-reject.mp3"));
        await repository.UpdateStatusAsync(
            rejected.Id,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            null,
            "No");
        repository.SetTimestamps(rejected.Id, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-40));
        await SeedPendingAsync(blobs, rejected.BlobPath);

        var pending = await repository.CreateAsync(NewSubmission(memberId, "still-pending.mp3"));
        repository.SetTimestamps(pending.Id, DateTimeOffset.UtcNow.AddDays(-40), null);
        await SeedPendingAsync(blobs, pending.BlobPath);

        var service = new FanPerformanceSubmissionPurgeService(
            repository,
            blobs,
            TimeProvider.System,
            NullLogger<FanPerformanceSubmissionPurgeService>.Instance);

        var result = await service.PurgeAsync();

        Assert.Equal(1, result.Deleted);
        Assert.False(backend.Exists(BlobUploadContainers.FanPerformances, rejected.BlobPath));
        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, pending.BlobPath));
        Assert.True(string.IsNullOrWhiteSpace((await repository.GetByIdAsync(rejected.Id))!.BlobPath));
        Assert.Equal(pending.BlobPath, (await repository.GetByIdAsync(pending.Id))!.BlobPath);
    }

    [Fact]
    public void StartupDelay_is_longer_than_app_service_container_start_limit()
    {
        Assert.True(
            FanPerformanceSubmissionPurgeHostedService.DefaultStartupDelay > TimeSpan.FromSeconds(230));
        Assert.Equal(TimeSpan.FromMinutes(5), FanPerformanceSubmissionPurgeHostedService.DefaultStartupDelay);
    }

    [Fact]
    public async Task HostedService_Purges_AfterStartupDelay()
    {
        var repository = new InMemoryFanPerformanceSubmissionRepository();
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var rejected = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "hosted.mp3"));
        await repository.UpdateStatusAsync(
            rejected.Id,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            null,
            "No");
        repository.SetTimestamps(rejected.Id, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-40));
        await SeedPendingAsync(blobs, rejected.BlobPath);

        var services = new ServiceCollection();
        services.AddSingleton<IFanPerformanceSubmissionRepository>(repository);
        services.AddSingleton<IBlobUploadService>(blobs);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddScoped<FanPerformanceSubmissionPurgeService>();
        using var provider = services.BuildServiceProvider();

        // Only the hosted loop's waits use the fake clock; the purge itself judges age from the
        // system clock registered above, matching the timestamps seeded relative to UtcNow.
        var clock = new TimerAwareFakeTimeProvider();
        using var hosted = new FanPerformanceSubmissionPurgeHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<FanPerformanceSubmissionPurgeHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);

        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, rejected.BlobPath));

        clock.Advance(FanPerformanceSubmissionPurgeHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);
        Assert.False(backend.Exists(BlobUploadContainers.FanPerformances, rejected.BlobPath));
    }

    private static FanPerformanceSubmission Sample(
        string status,
        string blobPath,
        DateTimeOffset markedAt) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Title",
            "Song",
            "Fan",
            null,
            blobPath,
            "cover.mp3",
            10,
            "audio/mpeg",
            1,
            status,
            markedAt,
            markedAt,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion);

    private static NewFanPerformanceSubmission NewSubmission(Guid memberId, string fileName) =>
        new(
            memberId,
            "Cover",
            "Song",
            "Fan",
            null,
            $"members/{memberId:N}/{fileName}",
            fileName,
            200,
            "audio/mpeg",
            1,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion);

    private static async Task SeedPendingAsync(IBlobUploadService blobs, string blobPath)
    {
        var bytes = new byte[64];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        await using var payload = new MemoryStream(bytes);
        await blobs.UploadAsync(
            payload,
            Path.GetFileName(blobPath),
            BlobUploadContainers.FanPerformances,
            new BlobUploadContext { PreferredBlobName = blobPath });
    }
}

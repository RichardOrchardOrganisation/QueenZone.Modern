using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_rejects_missing_rights_and_required_fields()
    {
        var service = CreateService(out _);
        await using var audio = new MemoryStream(CreateMpegPayload(200));

        var unsigned = await service.SubmitAsync(Guid.Empty, new FanPerformanceSubmissionDetails("Title", "Song", "Me", null, true), audio, "cover.mp3");
        Assert.False(unsigned.Succeeded);
        Assert.Contains("Sign in", unsigned.Error);

        audio.Position = 0;
        var noRights = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", "Song", "Me", null, false), audio, "cover.mp3");
        Assert.False(noRights.Succeeded);
        Assert.Contains("own performance", noRights.Error);

        audio.Position = 0;
        var emptyTitle = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("  ", "Song", "Me", null, true), audio, "cover.mp3");
        Assert.False(emptyTitle.Succeeded);

        audio.Position = 0;
        var longTitle = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails(new string('t', 201), "Song", "Me", null, true), audio, "cover.mp3");
        Assert.False(longTitle.Succeeded);

        audio.Position = 0;
        var emptySong = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", "  ", "Me", null, true), audio, "cover.mp3");
        Assert.False(emptySong.Succeeded);

        audio.Position = 0;
        var longSong = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", new string('s', 201), "Me", null, true), audio, "cover.mp3");
        Assert.False(longSong.Succeeded);

        audio.Position = 0;
        var emptyPerformer = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", "Song", "  ", null, true), audio, "cover.mp3");
        Assert.False(emptyPerformer.Succeeded);

        audio.Position = 0;
        var longPerformer = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", "Song", new string('p', 201), null, true), audio, "cover.mp3");
        Assert.False(longPerformer.Succeeded);

        audio.Position = 0;
        var longDescription = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Title", "Song", "Me", new string('d', 2001), true), audio, "cover.mp3");
        Assert.False(longDescription.Succeeded);
    }

    [Fact]
    public async Task SubmitAsync_uploads_to_pending_container_and_records_pending_row()
    {
        var service = CreateService(out var backend);
        var memberId = Guid.NewGuid();
        await using var audio = new MemoryStream(CreateMpegPayload(16_000));

        var result = await service.SubmitAsync(memberId, new FanPerformanceSubmissionDetails("Reaching Out cover", "Reaching Out", "A fan", "Living room take", true), audio, "cover.mp3");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, result.Submission!.Status);
        Assert.Equal("Reaching Out cover", result.Submission.Title);
        Assert.Equal("Reaching Out", result.Submission.CoveredSong);
        Assert.Equal("A fan", result.Submission.PerformedBy);
        Assert.Equal(FanPerformanceSubmissionRights.DeclarationVersion, result.Submission.RightsDeclarationVersion);
        Assert.Null(result.Submission.PromotedStageId);
        Assert.Equal(1, result.Submission.DurationSeconds);
        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, result.Submission.BlobPath));
        Assert.False(backend.Exists(SongFileUrl.ContainerName, result.Submission.BlobPath));
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_daily_quota_exceeded()
    {
        var repository = new InMemoryFanPerformanceSubmissionRepository();
        var backend = new InMemoryBlobStorageBackend();
        var options = Options.Create(new BlobUploadOptions());
        var quota = new MemberUploadQuotaService(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new UploadQuotaOptions
            {
                Enabled = true,
                MaxUploadsPerDay = 1,
                MaxBytesPerDay = 100L * 1024 * 1024,
            }));
        var blobs = new AzureBlobUploadService(backend, options);
        var service = new FanPerformanceSubmissionService(
            repository,
            new FanPerformanceAudioUploadService(blobs, quota, options),
            blobs);

        var memberId = Guid.NewGuid();
        await using var first = new MemoryStream(CreateMpegPayload(200));
        var ok = await service.SubmitAsync(memberId, new FanPerformanceSubmissionDetails("One", "Song", "Me", null, true), first, "a.mp3");
        Assert.True(ok.Succeeded, ok.Error);

        await using var second = new MemoryStream(CreateMpegPayload(200));
        var blocked = await service.SubmitAsync(memberId, new FanPerformanceSubmissionDetails("Two", "Song", "Me", null, true), second, "b.mp3");
        Assert.False(blocked.Succeeded);
        Assert.Contains("Daily upload", blocked.Error);
    }

    [Fact]
    public async Task SubmitAsync_surfaces_invalid_audio_errors()
    {
        var service = CreateService(out var backend);
        await using var junk = new MemoryStream("not-an-mp3"u8.ToArray());
        var result = await service.SubmitAsync(Guid.NewGuid(), new FanPerformanceSubmissionDetails("Bad file", "Song", "Me", null, true), junk, "fake.mp3");
        Assert.False(result.Succeeded);
        Assert.Contains("not recognized as audio", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Submission);
    }

    [Fact]
    public async Task WithdrawAsync_sets_withdrawn_and_deletes_pending_blob()
    {
        var service = CreateService(out var backend);
        var memberId = Guid.NewGuid();
        await using var audio = new MemoryStream(CreateMpegPayload(200));
        var created = await service.SubmitAsync(memberId, new FanPerformanceSubmissionDetails("Withdraw me", "Song", "Me", null, true), audio, "cover.mp3");
        Assert.True(created.Succeeded, created.Error);
        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, created.Submission!.BlobPath));

        var withdrawn = await service.WithdrawAsync(memberId, created.Submission.Id);
        Assert.True(withdrawn.Succeeded, withdrawn.Error);
        Assert.Equal(FanPerformanceSubmissionStatus.Withdrawn, withdrawn.Submission!.Status);
        Assert.False(backend.Exists(BlobUploadContainers.FanPerformances, created.Submission.BlobPath));
    }

    [Fact]
    public async Task WithdrawAsync_rejects_other_members_and_terminal_rows()
    {
        var repository = new InMemoryFanPerformanceSubmissionRepository();
        var backend = new InMemoryBlobStorageBackend();
        var service = CreateService(repository, backend);
        var owner = Guid.NewGuid();
        await using var audio = new MemoryStream(CreateMpegPayload(200));
        var created = await service.SubmitAsync(owner, new FanPerformanceSubmissionDetails("Mine", "Song", "Me", null, true), audio, "cover.mp3");

        var missing = await service.WithdrawAsync(owner, Guid.NewGuid());
        Assert.False(missing.Succeeded);

        var stranger = await service.WithdrawAsync(Guid.NewGuid(), created.Submission!.Id);
        Assert.False(stranger.Succeeded);

        await repository.UpdateStatusAsync(
            created.Submission.Id,
            FanPerformanceSubmissionStatus.Approved,
            "admin@test.local",
            "ok",
            null);
        var tooLate = await service.WithdrawAsync(owner, created.Submission.Id);
        Assert.False(tooLate.Succeeded);
        Assert.Contains("no longer be withdrawn", tooLate.Error);
        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, created.Submission.BlobPath));
    }

    [Fact]
    public async Task ReplyNeedsInfoAsync_moves_to_under_review()
    {
        var repository = new InMemoryFanPerformanceSubmissionRepository();
        var service = CreateService(repository, new InMemoryBlobStorageBackend());
        var memberId = Guid.NewGuid();
        await using var audio = new MemoryStream(CreateMpegPayload(200));
        var created = await service.SubmitAsync(memberId, new FanPerformanceSubmissionDetails("Needs reply", "Song", "Me", null, true), audio, "cover.mp3");
        await repository.UpdateStatusAsync(
            created.Submission!.Id,
            FanPerformanceSubmissionStatus.NeedsInfo,
            "admin@test.local",
            "Please name the song",
            null);

        var stranger = await service.ReplyNeedsInfoAsync(Guid.NewGuid(), created.Submission.Id, "Nope");
        Assert.False(stranger.Succeeded);

        var missing = await service.ReplyNeedsInfoAsync(memberId, Guid.NewGuid(), "Nope");
        Assert.False(missing.Succeeded);

        var empty = await service.ReplyNeedsInfoAsync(memberId, created.Submission.Id, "  ");
        Assert.False(empty.Succeeded);

        var tooLong = await service.ReplyNeedsInfoAsync(
            memberId,
            created.Submission.Id,
            new string('r', FanPerformanceSubmissionService.MaxReplyLength + 1));
        Assert.False(tooLong.Succeeded);

        var replied = await service.ReplyNeedsInfoAsync(memberId, created.Submission.Id, "It is Reaching Out.");
        Assert.True(replied.Succeeded, replied.Error);
        Assert.Equal(FanPerformanceSubmissionStatus.UnderReview, replied.Submission!.Status);
        Assert.Equal("Please name the song", replied.Submission.ReviewNotes);
        Assert.Contains(
            repository.GetAuditLogs(created.Submission.Id),
            log => log.Details == "It is Reaching Out.");

        var notWaiting = await service.ReplyNeedsInfoAsync(memberId, created.Submission.Id, "Again");
        Assert.False(notWaiting.Succeeded);
        Assert.Contains("not waiting", notWaiting.Error);
    }

    private static FanPerformanceSubmissionService CreateService(out InMemoryBlobStorageBackend backend)
    {
        backend = new InMemoryBlobStorageBackend();
        return CreateService(new InMemoryFanPerformanceSubmissionRepository(), backend);
    }

    private static FanPerformanceSubmissionService CreateService(
        InMemoryFanPerformanceSubmissionRepository repository,
        InMemoryBlobStorageBackend backend)
    {
        var options = Options.Create(new BlobUploadOptions());
        var blobs = new AzureBlobUploadService(backend, options);
        var quota = new MemberUploadQuotaService(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new UploadQuotaOptions { Enabled = false }));
        var audio = new FanPerformanceAudioUploadService(blobs, quota, options);
        return new FanPerformanceSubmissionService(repository, audio, blobs);
    }

    private static byte[] CreateMpegPayload(int length)
    {
        var bytes = new byte[Math.Max(length, 4)];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        return bytes;
    }
}

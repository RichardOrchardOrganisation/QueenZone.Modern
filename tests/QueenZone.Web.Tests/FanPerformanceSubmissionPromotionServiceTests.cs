using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceSubmissionPromotionServiceTests
{
    private readonly Guid memberId = Guid.NewGuid();

    [Fact]
    public async Task PromoteAsync_CopiesAudioCreatesPublicRowAndMarksSubmissionPromoted()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);

        var stageId = await service.PromoteAsync(
            submission,
            "admin@test.local",
            "Sounds good",
            new FanPerformanceReviewEdits("Edited title", "Edited performer", "Edited notes", "Edited song"));

        var updated = await submissionRepository.GetByIdAsync(submission.Id);
        Assert.Equal(FanPerformanceSubmissionStatus.Approved, updated!.Status);
        Assert.Equal(stageId, updated.PromotedStageId);
        Assert.Equal("Edited title", updated.Title);
        Assert.Equal("Edited performer", updated.PerformedBy);
        Assert.Equal("Edited notes", updated.Description);
        Assert.Equal("Edited song", updated.CoveredSong);
        Assert.True(string.IsNullOrWhiteSpace(updated.BlobPath));

        var published = await adminRepository.GetByIdAsync(stageId);
        Assert.NotNull(published);
        Assert.True(published!.IsVisible);
        Assert.Equal("Edited title", published.Title);
        Assert.Equal("Edited performer", published.PerformedBy);
        Assert.Equal($"{submission.Id:N}.mp3", published.AudioFileName);
        Assert.Equal(submission.DurationSeconds, published.DurationSeconds);
        Assert.Equal(submission.DurationSeconds, (await new InMemoryFanPerformanceRepository(store).GetByIdAsync(stageId))!.DurationSeconds);
        Assert.True(SongFileUrl.IsSafeBlobName(published.AudioFileName));
        Assert.True(backend.Exists(SongFileUrl.ContainerName, published.AudioFileName));
        Assert.False(backend.Exists(BlobUploadContainers.FanPerformances, submission.BlobPath));
        Assert.Contains(
            submissionRepository.GetAuditLogs(submission.Id),
            log => log.Action == FanPerformanceSubmissionStatus.Approved);
    }

    [Fact]
    public async Task PromoteAsync_IsIdempotent_WhenPromotedStageIdAlreadySet()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);
        await submissionRepository.PromoteAsync(submission.Id, 99, "admin@test.local", null);
        var before = await submissionRepository.GetByIdAsync(submission.Id);

        var stageId = await service.PromoteAsync(before!, "admin@test.local", null, null);

        Assert.Equal(99, stageId);
        var page = await adminRepository.GetPageAsync(new AdminFanPerformanceListFilter(), 1, 50);
        Assert.Empty(page.Items);
        Assert.False(backend.Exists(SongFileUrl.ContainerName, $"{submission.Id:N}.mp3"));
    }

    [Fact]
    public async Task PromoteAsync_UsesFlacExtension_WhenOriginalNameIsNotAudio()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var created = await submissionRepository.CreateAsync(
            new NewFanPerformanceSubmission(
                memberId,
                "Flac cover",
                "Reaching Out",
                "A fan",
                "Living room take",
                $"members/{memberId:N}/take.bin",
                "take.bin",
                400,
                "audio/flac",
                1,
                DateTimeOffset.UtcNow,
                FanPerformanceSubmissionRights.DeclarationVersion));
        var flacBytes = new byte[400];
        "fLaC"u8.CopyTo(flacBytes);
        await using (var payload = new MemoryStream(flacBytes))
        {
            await blobs.UploadAsync(
                payload,
                "take.bin",
                BlobUploadContainers.FanPerformances,
                new BlobUploadContext { PreferredBlobName = created.BlobPath });
        }

        var stageId = await service.PromoteAsync(created, "admin@test.local", null, null);
        var published = await adminRepository.GetByIdAsync(stageId);
        Assert.Equal($"{created.Id:N}.flac", published!.AudioFileName);
        Assert.True(backend.Exists(SongFileUrl.ContainerName, published.AudioFileName));
    }

    [Fact]
    public async Task PromoteAsync_EnsuresApproved_WhenPromotedStageIdSetButStatusIsNotApproved()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);
        submissionRepository.ForcePromotedStageId(submission.Id, 77);

        var stageId = await service.PromoteAsync(
            (await submissionRepository.GetByIdAsync(submission.Id))!,
            "admin@test.local",
            "Retry after partial promote",
            null);

        Assert.Equal(77, stageId);
        var updated = await submissionRepository.GetByIdAsync(submission.Id);
        Assert.Equal(FanPerformanceSubmissionStatus.Approved, updated!.Status);
        Assert.Equal(77, updated.PromotedStageId);
        Assert.Empty((await adminRepository.GetPageAsync(new AdminFanPerformanceListFilter(), 1, 50)).Items);
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenRightsDeclarationMissing()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var blobs = new AzureBlobUploadService(new InMemoryBlobStorageBackend(), Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var created = await submissionRepository.CreateAsync(new NewFanPerformanceSubmission(
            memberId,
            "No rights",
            "Song",
            "Fan",
            null,
            "members/x/cover.mp3",
            "cover.mp3",
            100,
            "audio/mpeg",
            10,
            default,
            string.Empty));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(created, "admin@test.local", null, null));
        Assert.Contains("rights declaration", ex.Message);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, (await submissionRepository.GetByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenWithdrawn()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);
        await submissionRepository.UpdateStatusAsync(
            submission.Id,
            FanPerformanceSubmissionStatus.Withdrawn,
            string.Empty,
            null,
            null);
        var withdrawn = await submissionRepository.GetByIdAsync(submission.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(withdrawn!, "admin@test.local", null, null));
        Assert.Contains("Cannot transition", ex.Message);
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenBlobMissing()
    {
        var submissionRepository = new InMemoryFanPerformanceSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var blobs = new AzureBlobUploadService(new InMemoryBlobStorageBackend(), Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var created = await submissionRepository.CreateAsync(NewSubmission("members/missing/cover.mp3"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(created, "admin@test.local", null, null));
        Assert.Contains("missing from storage", ex.Message);
    }

    [Fact]
    public async Task PromoteAsync_DeletesUploadedSongFile_WhenDbWriteFailsAfterUpload()
    {
        var submissionRepository = new FailingOnPromoteSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);
        var publishedName = FanPerformanceSubmissionPromotionService.BuildPublishedBlobName(submission);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(submission, "admin@test.local", "Looks great", null));

        Assert.False(backend.Exists(SongFileUrl.ContainerName, publishedName));
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, (await submissionRepository.GetByIdAsync(submission.Id))!.Status);
        Assert.Null((await submissionRepository.GetByIdAsync(submission.Id))!.PromotedStageId);
    }

    [Fact]
    public async Task PromoteAsync_DoesNotCompensateDeleteSongFile_WhenConcurrentApproveAlreadyCommitted()
    {
        var submissionRepository = new ConcurrentApproveLoserSubmissionRepository();
        var store = new SharedFanPerformanceStore();
        var adminRepository = new InMemoryAdminFanPerformanceRepository(store);
        var backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        var service = CreateService(submissionRepository, adminRepository, blobs);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, backend, blobs);
        var publishedName = FanPerformanceSubmissionPromotionService.BuildPublishedBlobName(submission);

        var stageId = await service.PromoteAsync(submission, "admin@test.local", "Looks great", null);

        Assert.Equal(ConcurrentApproveLoserSubmissionRepository.WinnerStageId, stageId);
        Assert.True(backend.Exists(SongFileUrl.ContainerName, publishedName));
        var latest = await submissionRepository.GetByIdAsync(submission.Id);
        Assert.Equal(ConcurrentApproveLoserSubmissionRepository.WinnerStageId, latest!.PromotedStageId);
    }

    [Fact]
    public void BuildPublishedBlobName_IsSafeBareFilenameFromSubmissionId()
    {
        var submission = new FanPerformanceSubmission(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.NewGuid(),
            "Title",
            "Song",
            "Fan",
            null,
            "members/x/cover.mp3",
            "cover.mp3",
            10,
            "audio/mpeg",
            1,
            FanPerformanceSubmissionStatus.Pending,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion);

        var name = FanPerformanceSubmissionPromotionService.BuildPublishedBlobName(submission);
        Assert.Equal("aaaaaaaabbbbccccddddeeeeeeeeeeee.mp3", name);
        Assert.True(SongFileUrl.IsSafeBlobName(name));
    }

    private static FanPerformanceSubmissionPromotionService CreateService(
        IFanPerformanceSubmissionRepository submissionRepository,
        IAdminFanPerformanceRepository adminRepository,
        IBlobUploadService blobs) =>
        new(
            submissionRepository,
            adminRepository,
            blobs,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<FanPerformanceSubmissionPromotionService>.Instance);

    private async Task<FanPerformanceSubmission> CreateApprovableSubmissionAsync(
        IFanPerformanceSubmissionRepository submissionRepository,
        InMemoryBlobStorageBackend backend,
        IBlobUploadService blobs)
    {
        var created = await submissionRepository.CreateAsync(NewSubmission($"members/{memberId:N}/cover.mp3"));
        await using var payload = new MemoryStream(CreateMpegPayload(400));
        await blobs.UploadAsync(
            payload,
            "cover.mp3",
            BlobUploadContainers.FanPerformances,
            new BlobUploadContext { PreferredBlobName = created.BlobPath });
        Assert.True(backend.Exists(BlobUploadContainers.FanPerformances, created.BlobPath));
        return created;
    }

    private NewFanPerformanceSubmission NewSubmission(string blobPath) =>
        new(
            memberId,
            "Promotable cover",
            "Reaching Out",
            "A fan",
            "Living room take",
            blobPath,
            "cover.mp3",
            400,
            "audio/mpeg",
            1,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion);

    private static byte[] CreateMpegPayload(int length)
    {
        var bytes = new byte[Math.Max(length, 4)];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        return bytes;
    }

    private sealed class FailingOnPromoteSubmissionRepository : IFanPerformanceSubmissionRepository
    {
        private readonly InMemoryFanPerformanceSubmissionRepository inner = new();

        public Task<FanPerformanceSubmission> CreateAsync(
            NewFanPerformanceSubmission submission,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(submission, cancellationToken);

        public Task<FanPerformanceSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmissionListItem>> GetPendingAsync(
            int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.GetPendingAsync(page, pageSize, cancellationToken);

        public Task<SubmissionListPage<FanPerformanceSubmission>> GetBySubmitterAsync(
            Guid submitterMemberId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
            inner.GetBySubmitterAsync(submitterMemberId, page, pageSize, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmissionAuditEntry>> GetAuditLogsAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            inner.GetAuditLogsAsync(id, cancellationToken);

        public Task<FanPerformanceSubmission?> UpdateStatusAsync(
            Guid id,
            string status,
            string? actorEmail,
            string? reviewNotes,
            string? rejectionReason,
            string? auditDetails = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(id, status, actorEmail, reviewNotes, rejectionReason, auditDetails, cancellationToken);

        public Task<FanPerformanceSubmission?> UpdateReviewMetadataAsync(
            Guid id,
            FanPerformanceReviewEdits edits,
            string editorEmail,
            CancellationToken cancellationToken = default) =>
            inner.UpdateReviewMetadataAsync(id, edits, editorEmail, cancellationToken);

        public Task<FanPerformanceSubmission?> PromoteAsync(
            Guid id,
            int promotedStageId,
            string reviewerEmail,
            string? reviewNotes,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated DB write failure after blob upload.");

        public Task<FanPerformanceDashboardCounts> GetDashboardCountsAsync(
            DateTimeOffset utcNow,
            int staleAfterDays = FanPerformanceDashboardCounts.DefaultStaleAfterDays,
            CancellationToken cancellationToken = default) =>
            inner.GetDashboardCountsAsync(utcNow, staleAfterDays, cancellationToken);

        public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
            DateTimeOffset monthStart, int maxCount, CancellationToken cancellationToken = default) =>
            inner.GetTopContributorsThisMonthAsync(monthStart, maxCount, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmission>> GetEligibleForPendingBlobPurgeAsync(
            DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
            inner.GetEligibleForPendingBlobPurgeAsync(cutoffUtc, cancellationToken);

        public Task ClearPendingBlobPathAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.ClearPendingBlobPathAsync(id, cancellationToken);

        public Task<IReadOnlyDictionary<int, FanPerformanceContributorCredit>> GetApprovedContributorCreditsAsync(
            IReadOnlyCollection<int> stageIds,
            CancellationToken cancellationToken = default) =>
            inner.GetApprovedContributorCreditsAsync(stageIds, cancellationToken);
    }

    private sealed class ConcurrentApproveLoserSubmissionRepository : IFanPerformanceSubmissionRepository
    {
        public const int WinnerStageId = 188;

        private readonly InMemoryFanPerformanceSubmissionRepository inner = new();

        public Task<FanPerformanceSubmission> CreateAsync(
            NewFanPerformanceSubmission submission,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(submission, cancellationToken);

        public Task<FanPerformanceSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmissionListItem>> GetPendingAsync(
            int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.GetPendingAsync(page, pageSize, cancellationToken);

        public Task<SubmissionListPage<FanPerformanceSubmission>> GetBySubmitterAsync(
            Guid submitterMemberId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
            inner.GetBySubmitterAsync(submitterMemberId, page, pageSize, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmissionAuditEntry>> GetAuditLogsAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            inner.GetAuditLogsAsync(id, cancellationToken);

        public Task<FanPerformanceSubmission?> UpdateStatusAsync(
            Guid id,
            string status,
            string? actorEmail,
            string? reviewNotes,
            string? rejectionReason,
            string? auditDetails = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(id, status, actorEmail, reviewNotes, rejectionReason, auditDetails, cancellationToken);

        public Task<FanPerformanceSubmission?> UpdateReviewMetadataAsync(
            Guid id,
            FanPerformanceReviewEdits edits,
            string editorEmail,
            CancellationToken cancellationToken = default) =>
            inner.UpdateReviewMetadataAsync(id, edits, editorEmail, cancellationToken);

        public Task<FanPerformanceSubmission?> PromoteAsync(
            Guid id,
            int promotedStageId,
            string reviewerEmail,
            string? reviewNotes,
            CancellationToken cancellationToken = default)
        {
            inner.ForcePromotedStageId(id, WinnerStageId);
            throw new InvalidOperationException("Simulated concurrent approve lost the Q_STAGE_T race.");
        }

        public Task<FanPerformanceDashboardCounts> GetDashboardCountsAsync(
            DateTimeOffset utcNow,
            int staleAfterDays = FanPerformanceDashboardCounts.DefaultStaleAfterDays,
            CancellationToken cancellationToken = default) =>
            inner.GetDashboardCountsAsync(utcNow, staleAfterDays, cancellationToken);

        public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
            DateTimeOffset monthStart, int maxCount, CancellationToken cancellationToken = default) =>
            inner.GetTopContributorsThisMonthAsync(monthStart, maxCount, cancellationToken);

        public Task<IReadOnlyList<FanPerformanceSubmission>> GetEligibleForPendingBlobPurgeAsync(
            DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
            inner.GetEligibleForPendingBlobPurgeAsync(cutoffUtc, cancellationToken);

        public Task ClearPendingBlobPathAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.ClearPendingBlobPathAsync(id, cancellationToken);

        public Task<IReadOnlyDictionary<int, FanPerformanceContributorCredit>> GetApprovedContributorCreditsAsync(
            IReadOnlyCollection<int> stageIds,
            CancellationToken cancellationToken = default) =>
            inner.GetApprovedContributorCreditsAsync(stageIds, cancellationToken);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfFanPerformanceSubmissionRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfFanPerformanceSubmissionRepository repository;
    private readonly Guid memberId = Guid.NewGuid();

    public EfFanPerformanceSubmissionRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "fanperf-ef@example.com",
            NormalizedEmail = "FANPERF-EF@EXAMPLE.COM",
            DisplayName = "EF Stage Fan",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();

        repository = new EfFanPerformanceSubmissionRepository(dbContext);
    }

    [Fact]
    public async Task CreateAsync_PersistsPendingSubmissionAndAudit()
    {
        var preferredId = Guid.NewGuid();
        var created = await repository.CreateAsync(NewSubmission(preferredId, "Live cover"));

        Assert.Equal(preferredId, created.Id);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, created.Status);
        Assert.Equal("Live cover", created.Title);
        Assert.Equal("Bohemian Rhapsody", created.CoveredSong);
        Assert.Equal(FanPerformanceSubmissionRights.DeclarationVersion, created.RightsDeclarationVersion);
        Assert.Null(created.PromotedStageId);

        var loaded = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("EF Stage Fan", loaded!.SubmitterDisplayName);
        Assert.Equal("fanperf-ef@example.com", loaded.SubmitterEmail);

        Assert.Single(dbContext.FanPerformanceSubmissionAuditLogs.Where(log => log.FanPerformanceSubmissionId == created.Id));
    }

    [Fact]
    public async Task CreateAsync_GeneratesId_WhenPreferredMissing()
    {
        var created = await repository.CreateAsync(NewSubmission(null, "No preferred id"));
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task GetBySubmitterAsync_ReturnsOnlyOwnedRows()
    {
        var otherMember = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = otherMember,
            Email = "other-fanperf@example.com",
            NormalizedEmail = "OTHER-FANPERF@EXAMPLE.COM",
            DisplayName = "Other",
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Mine"));
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Theirs") with
        {
            SubmitterMemberId = otherMember,
        });

        var mine = await repository.GetBySubmitterAsync(memberId);
        Assert.Single(mine.Items);
        Assert.Equal("Mine", mine.Items[0].Title);
        Assert.Equal(1, mine.TotalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateStatusAsync_WithdrawAndNeedsInfoReply()
    {
        var forWithdraw = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Withdraw me"));
        var withdrawn = await repository.UpdateStatusAsync(
            forWithdraw.Id,
            FanPerformanceSubmissionStatus.Withdrawn,
            string.Empty,
            null,
            null,
            "Member withdrew the submission.");
        Assert.Equal(FanPerformanceSubmissionStatus.Withdrawn, withdrawn!.Status);

        var forReply = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Reply me"));
        var needsInfo = await repository.UpdateStatusAsync(
            forReply.Id,
            FanPerformanceSubmissionStatus.NeedsInfo,
            "admin@test.local",
            "Please add the song",
            null);
        Assert.Equal(FanPerformanceSubmissionStatus.NeedsInfo, needsInfo!.Status);

        var replied = await repository.UpdateStatusAsync(
            forReply.Id,
            FanPerformanceSubmissionStatus.UnderReview,
            string.Empty,
            null,
            null,
            "It is Reaching Out.");
        Assert.Equal(FanPerformanceSubmissionStatus.UnderReview, replied!.Status);
        Assert.Equal("Please add the song", replied.ReviewNotes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateStatusAsync(
                forWithdraw.Id,
                FanPerformanceSubmissionStatus.Pending,
                "admin@test.local",
                null,
                null));

        Assert.Null(await repository.UpdateStatusAsync(
            Guid.NewGuid(),
            FanPerformanceSubmissionStatus.UnderReview,
            "admin@test.local",
            null,
            null));
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectRequiresReason()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Reject me"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateStatusAsync(
                created.Id,
                FanPerformanceSubmissionStatus.Rejected,
                "admin@test.local",
                null,
                rejectionReason: null));

        var rejected = await repository.UpdateStatusAsync(
            created.Id,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            "internal",
            "  Not a Queen cover  ");
        Assert.Equal(FanPerformanceSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Not a Queen cover", rejected.RejectionReason);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsReviewableStatusesNewestFirst()
    {
        var older = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Older pending"));
        var newer = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Newer pending"));
        var submittedAt = DateTimeOffset.UtcNow;
        (await dbContext.FanPerformanceSubmissions.SingleAsync(row => row.Id == older.Id)).SubmittedAt = submittedAt.AddMinutes(-1);
        (await dbContext.FanPerformanceSubmissions.SingleAsync(row => row.Id == newer.Id)).SubmittedAt = submittedAt;
        await dbContext.SaveChangesAsync();
        var withdrawn = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Withdrawn"));
        await repository.UpdateStatusAsync(
            withdrawn.Id,
            FanPerformanceSubmissionStatus.Withdrawn,
            string.Empty,
            null,
            null);

        var page = await repository.GetPendingAsync(1, 50);
        Assert.Equal(2, page.Count);
        Assert.Equal(newer.Id, page[0].Id);
        Assert.Equal(older.Id, page[1].Id);
        Assert.DoesNotContain(page, item => item.Id == withdrawn.Id);
    }

    [Fact]
    public async Task PromoteAsync_SetsApprovedStatusPromotedStageIdAndAudit()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Promote me"));
        var promoted = await repository.PromoteAsync(created.Id, 42, "admin@test.local", "Ready");

        Assert.Equal(FanPerformanceSubmissionStatus.Approved, promoted!.Status);
        Assert.Equal(42, promoted.PromotedStageId);
        Assert.Equal("admin@test.local", promoted.ReviewerEmail);
        Assert.Contains(
            await repository.GetAuditLogsAsync(created.Id),
            log => log.Action == FanPerformanceSubmissionStatus.Approved && log.Details!.Contains("#42"));
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenTransitionNotAllowed()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Already done"));
        await repository.UpdateStatusAsync(
            created.Id,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            null,
            "Nope");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.PromoteAsync(created.Id, 1, "admin@test.local", null));
    }

    [Fact]
    public async Task GetApprovedContributorCreditsAsync_BatchesNewestApprovedSubmission()
    {
        var older = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Older credit"));
        await repository.PromoteAsync(older.Id, 187, "admin@test.local", null);
        var olderRow = await dbContext.FanPerformanceSubmissions.SingleAsync(row => row.Id == older.Id);
        olderRow.SubmittedAt = DateTimeOffset.UtcNow.AddDays(-2);
        await dbContext.SaveChangesAsync();

        var newer = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Newer credit"));
        await repository.PromoteAsync(newer.Id, 187, "admin@test.local", null);

        var pending = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Pending other"));
        var pendingRow = await dbContext.FanPerformanceSubmissions.SingleAsync(row => row.Id == pending.Id);
        pendingRow.PromotedStageId = 186;
        await dbContext.SaveChangesAsync();

        var credits = await repository.GetApprovedContributorCreditsAsync([187, 186, 173]);

        Assert.True(credits.ContainsKey(187));
        Assert.False(credits.ContainsKey(186));
        Assert.False(credits.ContainsKey(173));
        Assert.Equal(memberId, credits[187].MemberId);
        Assert.Equal("EF Stage Fan", credits[187].DisplayName);
    }

    [Fact]
    public async Task GetEligibleForPendingBlobPurgeAsync_OnlyRejectedAndWithdrawnPastCutoff()
    {
        var rejected = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Old reject"));
        await repository.UpdateStatusAsync(
            rejected.Id,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            null,
            "No");
        var pending = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Still pending"));

        var rejectedEntity = await dbContext.FanPerformanceSubmissions.SingleAsync(row => row.Id == rejected.Id);
        rejectedEntity.ReviewedAt = DateTimeOffset.UtcNow.AddDays(-40);
        await dbContext.SaveChangesAsync();

        var eligible = await repository.GetEligibleForPendingBlobPurgeAsync(DateTimeOffset.UtcNow.AddDays(-30));
        Assert.Contains(eligible, row => row.Id == rejected.Id);
        Assert.DoesNotContain(eligible, row => row.Id == pending.Id);
    }

    public ValueTask DisposeAsync()
    {
        dbContext.Dispose();
        connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private NewFanPerformanceSubmission NewSubmission(Guid? id, string title) =>
        new(
            memberId,
            title,
            "Bohemian Rhapsody",
            "EF Stage Fan",
            "Studio take",
            $"members/{memberId:N}/cover.mp3",
            "cover.mp3",
            2048,
            "audio/mpeg",
            120,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion,
            id);
}

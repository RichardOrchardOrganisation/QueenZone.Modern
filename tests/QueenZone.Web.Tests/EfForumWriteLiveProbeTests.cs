using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

/// <summary>
/// Opt-in SQL Express mirror probe for modern forum thread/post writes (sequences + stats).
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfForumWriteLiveProbeTests
{
    private const string LeftoverProbeSnapshotMarker = "forum-report-probe-";

    [Fact]
    public async Task Create_and_moderate_forum_report_on_mirror_when_enabled()
    {
        if (!IsProbeEnabled(out var connectionString))
        {
            return;
        }

        await using (var schema = CreateContext(connectionString))
        {
            if (!await LiveProbeSchema.TableExistsAsync(schema, LiveProbeSchema.ForumPostReportsTable))
            {
                // Mirror matches production. ForumPostReports is a same-day
                // migration that may not be deployed yet (#1528 / #1507).
                return;
            }
        }

        var uniqueSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var marker = $"forum-report-probe-{uniqueSuffix}";
        var authorId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        int? topicId = null;
        Guid? reportId = null;

        try
        {
            await using var setup = CreateContext(connectionString);
            // Prior failed probes or Sync can leave Open ForumPostReports.
            // Scrub only leftover probe-marked rows so residue does not grow;
            // do not wipe Sync/prod Open reports on the shared mirror (#1702).
            await DeleteLeftoverProbeOpenReportsAsync(setup);

            var category = await setup.ModernForumCategories.AsNoTracking()
                .Where(c => !c.IsSynthetic)
                .OrderBy(c => c.LegacyForumId)
                .FirstAsync();
            setup.MemberAccounts.AddRange(
                NewProbeMember(authorId, $"{marker}-author@queenzone.local", $"Report Author {uniqueSuffix}"),
                NewProbeMember(reporterId, $"{marker}-reporter@queenzone.local", $"Report Reporter {uniqueSuffix}"));
            await setup.SaveChangesAsync();

            var forumWrites = new EfForumWriteRepository(setup);
            var created = await forumWrites.CreateThreadAsync(new NewForumThread(
                category.LegacyForumId,
                authorId,
                $"Report Author {uniqueSuffix}",
                $"{marker} subject",
                $"<p>{marker} evidence</p>",
                DateTimeOffset.UtcNow));
            topicId = created.TopicId;

            var reports = new EfForumPostReportRepository(setup);
            var baseline = await reports.CountOpenAsync();
            var first = await reports.CreateAsync(
                reporterId,
                created.StarterPostId,
                ForumPostReportCategories.Harassment,
                "Mirror report details",
                DateTimeOffset.UtcNow);
            var duplicate = await reports.CreateAsync(
                reporterId,
                created.StarterPostId,
                ForumPostReportCategories.Other,
                null,
                DateTimeOffset.UtcNow);
            Assert.True(first.Succeeded);
            Assert.False(first.AlreadyReported);
            Assert.True(duplicate.AlreadyReported);
            Assert.Equal(first.ReportId, duplicate.ReportId);
            reportId = first.ReportId;

            var stored = await reports.GetAsync(reportId!.Value);
            Assert.NotNull(stored);
            Assert.Equal(PrivateMessageReportStatus.Open, stored!.Status);
            Assert.Equal(authorId, stored.ReportedMemberId);
            Assert.Contains(marker, stored.PostBodySnapshot, StringComparison.Ordinal);
            Assert.Equal(baseline + 1, await reports.CountOpenAsync());

            await reports.AppendViewedAuditAsync(reportId.Value, "probe@queenzone.local");
            var updated = await reports.UpdateStatusAsync(
                reportId.Value,
                PrivateMessageReportStatus.Actioned,
                "probe@queenzone.local");
            Assert.Equal(PrivateMessageReportStatus.Actioned, updated!.Status);
            Assert.Equal(2, await setup.ForumPostReportAuditLogs.CountAsync(log => log.ReportId == reportId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            if (reportId is Guid id)
            {
                await cleanup.ForumPostReportAuditLogs.Where(log => log.ReportId == id).ExecuteDeleteAsync();
                await cleanup.ForumPostReports.Where(report => report.Id == id).ExecuteDeleteAsync();
            }
            await CleanupAsync(cleanup, [authorId, reporterId], topicId, marker);
        }
    }

    [Fact]
    public async Task Leftover_probe_open_reports_do_not_break_count_open_delta()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var leftoverReporter = NewProbeMember(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeea1"),
            "leftover-reporter@queenzone.local",
            "Leftover Reporter");
        var syncedReporter = NewProbeMember(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeea2"),
            "synced-reporter@queenzone.local",
            "Synced Reporter");
        var createdReporter = NewProbeMember(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeea3"),
            "created-reporter@queenzone.local",
            "Created Reporter");
        db.MemberAccounts.AddRange(leftoverReporter, syncedReporter, createdReporter);
        await db.SaveChangesAsync();

        var leftoverId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1");
        var syncedOpenId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee2");
        db.ForumPostReports.AddRange(
            new ForumPostReportEntity
            {
                Id = leftoverId,
                PostId = 101,
                TopicId = 201,
                ReporterMemberId = leftoverReporter.Id,
                Category = ForumPostReportCategories.Other,
                CreatedAt = DateTimeOffset.Parse("2026-09-22T00:00:00Z"),
                Status = PrivateMessageReportStatus.Open,
                PostBodySnapshot = "<p>forum-report-probe-202609220000000 leftover evidence</p>",
                AuthorDisplayNameSnapshot = "Leftover Author",
                PostCreatedAtSnapshot = DateTimeOffset.Parse("2026-09-22T00:00:00Z"),
                ThreadTitleSnapshot = "forum-report-probe-202609220000000 leftover subject",
            },
            new ForumPostReportEntity
            {
                Id = syncedOpenId,
                PostId = 102,
                TopicId = 202,
                ReporterMemberId = syncedReporter.Id,
                Category = ForumPostReportCategories.Spam,
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                Status = PrivateMessageReportStatus.Open,
                PostBodySnapshot = "Synced production open report",
                AuthorDisplayNameSnapshot = "Synced Author",
                PostCreatedAtSnapshot = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                ThreadTitleSnapshot = "Production thread",
            });
        db.ForumPostReportAuditLogs.Add(new ForumPostReportAuditLogEntity
        {
            ReportId = leftoverId,
            Action = "Viewed",
            ActorEmail = "probe@queenzone.local",
            OccurredAt = DateTimeOffset.Parse("2026-09-22T00:01:00Z"),
            Details = "forum-report-probe-202609220000000 leftover audit",
        });
        await db.SaveChangesAsync();

        await DeleteLeftoverProbeOpenReportsAsync(db);

        Assert.False(await db.ForumPostReports.AnyAsync(report => report.Id == leftoverId));
        Assert.False(await db.ForumPostReportAuditLogs.AnyAsync(log => log.ReportId == leftoverId));
        Assert.True(await db.ForumPostReports.AnyAsync(report => report.Id == syncedOpenId));

        var reports = new EfForumPostReportRepository(db);
        var baseline = await reports.CountOpenAsync();
        Assert.Equal(1, baseline);

        var createdId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee3");
        db.ForumPostReports.Add(new ForumPostReportEntity
        {
            Id = createdId,
            PostId = 103,
            TopicId = 203,
            ReporterMemberId = createdReporter.Id,
            Category = ForumPostReportCategories.Harassment,
            CreatedAt = DateTimeOffset.Parse("2026-09-24T00:00:00Z"),
            Status = PrivateMessageReportStatus.Open,
            PostBodySnapshot = "<p>forum-report-probe-now evidence</p>",
            AuthorDisplayNameSnapshot = "Probe Author",
            PostCreatedAtSnapshot = DateTimeOffset.Parse("2026-09-24T00:00:00Z"),
            ThreadTitleSnapshot = "forum-report-probe-now subject",
        });
        await db.SaveChangesAsync();

        var storedStatus = await db.ForumPostReports
            .Where(report => report.Id == createdId)
            .Select(report => report.Status)
            .SingleAsync();
        Assert.Equal(PrivateMessageReportStatus.Open, storedStatus);
        Assert.Equal(baseline + 1, await reports.CountOpenAsync());
        Assert.True(await db.ForumPostReports.AnyAsync(report => report.Id == syncedOpenId));
    }

    [Fact]
    public async Task Create_thread_and_reply_on_mirror_when_enabled()
    {
        if (!IsProbeEnabled(out var connectionString))
        {
            return;
        }

        var uniqueSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var memberId = Guid.NewGuid();
        var marker = $"forum-write-probe-{uniqueSuffix}";
        int? topicId = null;

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                var category = await setup.ModernForumCategories
                    .AsNoTracking()
                    .Where(c => !c.IsSynthetic)
                    .OrderBy(c => c.LegacyForumId)
                    .FirstOrDefaultAsync();
                Assert.NotNull(category);
                Assert.True(
                    category.LegacyForumId > 0,
                    "Mirror has no non-synthetic modern forum category. Import/project forum categories before this probe.");

                setup.MemberAccounts.Add(NewProbeMember(
                    memberId,
                    $"{marker}@queenzone.local",
                    $"Forum Write Probe {uniqueSuffix}"));
                await setup.SaveChangesAsync();

                var repo = new EfForumWriteRepository(setup);
                var created = await repo.CreateThreadAsync(new NewForumThread(
                    category.LegacyForumId,
                    memberId,
                    $"Forum Write Probe {uniqueSuffix}",
                    $"{marker} subject",
                    $"<p>{marker} starter body</p>",
                    DateTimeOffset.UtcNow));
                topicId = created.TopicId;

                var replyId = await repo.CreatePostAsync(new NewForumPost(
                    created.TopicId,
                    memberId,
                    $"Forum Write Probe {uniqueSuffix}",
                    $"<p>{marker} reply body</p>",
                    DateTimeOffset.UtcNow));
                Assert.True(replyId > created.StarterPostId);

                var thread = await repo.GetThreadAsync(created.TopicId);
                Assert.NotNull(thread);
                Assert.Equal(2, thread.PostCount);
                Assert.Contains(marker, thread.Subject, StringComparison.Ordinal);
            }
        }
        finally
        {
            await CleanupAsync(connectionString, memberId, topicId, marker);
        }
    }

    private static async Task DeleteLeftoverProbeOpenReportsAsync(QueenZoneDbContext db)
    {
        var leftoverIds = await db.ForumPostReports
            .Where(report =>
                report.Status == PrivateMessageReportStatus.Open
                && (report.PostBodySnapshot.Contains(LeftoverProbeSnapshotMarker)
                    || report.ThreadTitleSnapshot.Contains(LeftoverProbeSnapshotMarker)))
            .Select(report => report.Id)
            .ToListAsync();
        if (leftoverIds.Count == 0)
        {
            return;
        }

        await db.ForumPostReportAuditLogs
            .Where(log => leftoverIds.Contains(log.ReportId))
            .ExecuteDeleteAsync();
        await db.ForumPostReports
            .Where(report => leftoverIds.Contains(report.Id))
            .ExecuteDeleteAsync();
    }

    private static QueenZoneDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.CommandTimeout(QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds);
                    sql.EnableRetryOnFailure(
                        maxRetryCount: QueenZoneSqlServerOptions.MaxRetryCount,
                        maxRetryDelay: QueenZoneSqlServerOptions.MaxRetryDelay,
                        errorNumbersToAdd: null);
                })
            .Options;
        return new QueenZoneDbContext(options);
    }

    private static MemberAccount NewProbeMember(Guid id, string email, string displayName) =>
        new()
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task CleanupAsync(
        string connectionString,
        Guid memberId,
        int? topicId,
        string marker)
    {
        await using var cleanup = CreateContext(connectionString);
        await CleanupAsync(cleanup, [memberId], topicId, marker);
    }

    private static async Task CleanupAsync(
        QueenZoneDbContext cleanup,
        IReadOnlyCollection<Guid> memberIds,
        int? topicId,
        string marker)
    {
        if (topicId is int legacyTopicId)
        {
            var thread = await cleanup.ModernForumThreads
                .SingleOrDefaultAsync(t => t.LegacyTopicId == legacyTopicId);
            if (thread is not null)
            {
                await cleanup.Database.ExecuteSqlRawAsync(
                    """
                    IF OBJECT_ID(N'dbo.ModernForumThreadReadStats', N'U') IS NOT NULL
                    BEGIN
                        DELETE FROM dbo.ModernForumThreadReadStats WHERE LegacyTopicId = {0};
                    END
                    """,
                    legacyTopicId);

                await cleanup.ModernForumPosts
                    .Where(p => p.ThreadId == thread.Id)
                    .ExecuteDeleteAsync();
                await cleanup.ModernForumThreads
                    .Where(t => t.Id == thread.Id)
                    .ExecuteDeleteAsync();
                await SearchDocumentTeardown.DeleteBySourceKeysAsync(
                    cleanup,
                    [SearchDocumentSourceKey.ForForumThread(legacyTopicId)]);
            }
        }

        // Category LegacyPostCount / read-stat counters may retain tiny disposable-mirror drift.
        await cleanup.MemberAccounts
            .Where(m => memberIds.Contains(m.Id) || m.Email.Contains(marker))
            .ExecuteDeleteAsync();
    }

    private static bool IsProbeEnabled(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_FORUM_WRITE_PROBE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}

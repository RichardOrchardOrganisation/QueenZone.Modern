using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfLegacyProbeResidueTests
{
    private const string UiTestMarker = "uie2e-";

    private const string LegacyPhotoResidueSql =
        """
        SELECT CAST(COUNT(*) AS int) AS [Value]
        FROM dbo.PIC_FILES_T
        WHERE Name IN ('Thumb regen photo', 'Route upload photo')
           OR Name LIKE 'photo-submission-probe-%'
           OR Name LIKE 'uie2e-%'
        """;

    private const string LegacyBiographyResidueSql =
        """
        SELECT CAST(COUNT(*) AS int) AS [Value]
        FROM dbo.Q_BIO_T
        WHERE TITLE LIKE 'uie2e-%'
        """;

    /// <summary>
    /// Deterministic: forces SQL Server translation of every residue marker predicate
    /// without connecting. Catches StringComparison overloads and other non-translatable
    /// LINQ that would only fail on the nightly SQL Express residue step.
    /// </summary>
    [Fact]
    public void Residue_marker_predicates_translate_for_sql_server()
    {
        using var dbContext = CreateSqlServerContext("Server=.;Database=unused;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=1");

        Assert.False(string.IsNullOrWhiteSpace(NewsResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(NewsAuditResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(RunRequestResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(HeartbeatResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(CandidateResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(DiscoverySourceResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(MemberResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PrivateMessageResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PrivateConversationResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PrivateConversationParticipantResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(MemberMessageBlockResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(MemberFollowResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(ForumThreadResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(ForumPostResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PhotoSubmissionResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PhotoSubmissionAuditResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(ArticleSubmissionResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(NewsSuggestionResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(TriviaFactSubmissionResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(TriviaFactSubmissionAuditResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(FanPerformanceSubmissionResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(FanPerformanceSubmissionAuditResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(PhotoAdminAuditResidueQuery(dbContext).ToQueryString()));
        Assert.False(string.IsNullOrWhiteSpace(SearchDocumentResidueQuery(dbContext).ToQueryString()));
    }

    [Fact]
    public async Task Ui_test_marker_predicates_detect_seeded_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await CreateExcludedResidueTestTablesAsync(dbContext);

        SeedUiTestResidue(dbContext);
        await dbContext.SaveChangesAsync();

        Assert.True(await NewsResidueQuery(dbContext).AnyAsync());
        Assert.True(await NewsAuditResidueQuery(dbContext).AnyAsync());
        Assert.True(await MemberResidueQuery(dbContext).AnyAsync());
        Assert.True(await PrivateMessageResidueQuery(dbContext).AnyAsync());
        Assert.True(await PrivateConversationResidueQuery(dbContext).AnyAsync());
        Assert.True(await PrivateConversationParticipantResidueQuery(dbContext).AnyAsync());
        Assert.True(await MemberMessageBlockResidueQuery(dbContext).AnyAsync());
        Assert.True(await MemberFollowResidueQuery(dbContext).AnyAsync());
        Assert.True(await ForumThreadResidueQuery(dbContext).AnyAsync());
        Assert.True(await ForumPostResidueQuery(dbContext).AnyAsync());
        Assert.True(await PhotoSubmissionResidueQuery(dbContext).AnyAsync());
        Assert.True(await PhotoSubmissionAuditResidueQuery(dbContext).AnyAsync());
        Assert.True(await ArticleSubmissionResidueQuery(dbContext).AnyAsync());
        Assert.True(await NewsSuggestionResidueQuery(dbContext).AnyAsync());
        Assert.True(await TriviaFactSubmissionResidueQuery(dbContext).AnyAsync());
        Assert.True(await TriviaFactSubmissionAuditResidueQuery(dbContext).AnyAsync());
        Assert.True(await FanPerformanceSubmissionResidueQuery(dbContext).AnyAsync());
        Assert.True(await FanPerformanceSubmissionAuditResidueQuery(dbContext).AnyAsync());
        Assert.True(await PhotoAdminAuditResidueQuery(dbContext).AnyAsync());
        Assert.True(await SearchDocumentResidueQuery(dbContext).AnyAsync());
    }

    [Fact]
    public void SearchDocument_residue_failure_names_content_type_and_source_key()
    {
        var leftovers = new[]
        {
            new SearchDocumentResidueRef("news", "news:900042"),
            new SearchDocumentResidueRef("article", "article:probe-article-slug"),
        };

        var message = FormatSearchDocumentResidueFailure(leftovers);

        Assert.Contains("Residue found in SearchDocument", message, StringComparison.Ordinal);
        Assert.Contains("news news:900042", message, StringComparison.Ordinal);
        Assert.Contains("article article:probe-article-slug", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Known_probe_and_web_test_markers_are_absent_when_check_enabled()
    {
        if (!IsCheckEnabled(out var connectionString))
        {
            return;
        }

        await using var dbContext = CreateSqlServerContext(connectionString);
        dbContext.Database.SetCommandTimeout(QueenZoneSqlServerOptions.LongRunningCommandTimeoutSeconds);

        Assert.False(await NewsResidueQuery(dbContext).AnyAsync(), "Residue found in NEWS_T.");
        Assert.False(await NewsAuditResidueQuery(dbContext).AnyAsync(), "Residue found in NewsAuditLog.");
        Assert.False(await RunRequestResidueQuery(dbContext).AnyAsync(), "Residue found in NewsAgentRunRequests.");
        Assert.False(await HeartbeatResidueQuery(dbContext).AnyAsync(), "Residue found in NewsAgentRunnerHeartbeats.");
        Assert.False(await CandidateResidueQuery(dbContext).AnyAsync(), "Residue found in NewsCandidates.");
        Assert.False(await DiscoverySourceResidueQuery(dbContext).AnyAsync(), "Residue found in NewsDiscoverySources.");
        Assert.False(await MemberResidueQuery(dbContext).AnyAsync(), "Residue found in MemberAccounts.");
        Assert.False(await PrivateMessageResidueQuery(dbContext).AnyAsync(), "Residue found in PrivateMessages.");
        Assert.False(await PrivateConversationResidueQuery(dbContext).AnyAsync(), "Residue found in PrivateConversations.");
        Assert.False(
            await PrivateConversationParticipantResidueQuery(dbContext).AnyAsync(),
            "Residue found in PrivateConversationParticipants.");
        Assert.False(await MemberMessageBlockResidueQuery(dbContext).AnyAsync(), "Residue found in MemberMessageBlocks.");
        Assert.False(await MemberFollowResidueQuery(dbContext).AnyAsync(), "Residue found in MemberFollows.");
        Assert.False(await ForumThreadResidueQuery(dbContext).AnyAsync(), "Residue found in ModernForumThread.");
        Assert.False(await ForumPostResidueQuery(dbContext).AnyAsync(), "Residue found in ModernForumPost.");
        Assert.False(await PhotoSubmissionResidueQuery(dbContext).AnyAsync(), "Residue found in PhotoSubmissions.");
        Assert.False(
            await PhotoSubmissionAuditResidueQuery(dbContext).AnyAsync(),
            "Residue found in PhotoSubmissionAuditLog.");
        Assert.False(await ArticleSubmissionResidueQuery(dbContext).AnyAsync(), "Residue found in ArticleSubmissions.");
        Assert.False(await NewsSuggestionResidueQuery(dbContext).AnyAsync(), "Residue found in NewsSuggestions.");
        Assert.False(await TriviaFactSubmissionResidueQuery(dbContext).AnyAsync(), "Residue found in TriviaFactSubmissions.");
        Assert.False(
            await TriviaFactSubmissionAuditResidueQuery(dbContext).AnyAsync(),
            "Residue found in TriviaFactSubmissionAuditLog.");
        Assert.False(
            await FanPerformanceSubmissionResidueQuery(dbContext).AnyAsync(),
            "Residue found in FanPerformanceSubmissions.");
        Assert.False(
            await FanPerformanceSubmissionAuditResidueQuery(dbContext).AnyAsync(),
            "Residue found in FanPerformanceSubmissionAuditLog.");
        Assert.False(await PhotoAdminAuditResidueQuery(dbContext).AnyAsync(), "Residue found in PhotoAdminAuditLog.");
        var searchDocumentResidue = await SearchDocumentResidueQuery(dbContext)
            .Select(document => new SearchDocumentResidueRef(document.ContentType, document.SourceKey))
            .ToListAsync();
        Assert.True(searchDocumentResidue.Count == 0, FormatSearchDocumentResidueFailure(searchDocumentResidue));
        var legacyTestPhotoCount = await dbContext.Database
            .SqlQueryRaw<int>(LegacyPhotoResidueSql)
            .SingleAsync();
        Assert.True(legacyTestPhotoCount == 0, "Residue found in PIC_FILES_T.");

        var legacyUiTestBiographyCount = await dbContext.Database
            .SqlQueryRaw<int>(LegacyBiographyResidueSql)
            .SingleAsync();
        Assert.True(legacyUiTestBiographyCount == 0, "Residue found in Q_BIO_T.");
    }

    private static QueenZoneDbContext CreateSqlServerContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new QueenZoneDbContext(options);
    }

    // Use parameterless string overloads only. string.Contains/StartsWith/EndsWith
    // with StringComparison cannot be translated by EF Core SQL Server.
    // SQL Express mirror collations are case-insensitive, so marker matching still works.

    private static IQueryable<NewsTableRow> NewsResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsRows.Where(row =>
            (row.Slug != null
                && (row.Slug.StartsWith("probe-write-")
                    || row.Slug.StartsWith("news-section-live-probe-")
                    || row.Slug.StartsWith("full-lifecycle-live-probe-")))
            || row.EditorEmail == "legacy-write-probe@queenzone.local"
            // uie2e-{runId}-{fixture}-{n}: RealDataPageTest marker convention (Web.E2E),
            // e.g. AdminModerationWorkflowTests publishing an approved article for #548.
            || row.Title.Contains(UiTestMarker));

    private static IQueryable<NewsAuditLogEntity> NewsAuditResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsAuditLogs.Where(audit =>
            audit.ActorEmail == "legacy-write-probe@queenzone.local"
            // AdminModerationWorkflowTests (#548) acts as the E2E admin test identity when
            // publishing/unpublishing news; catches audit rows orphaned if CleanupCreatedRowsAsync's
            // NewsId-scoped delete never runs.
            || audit.ActorEmail == "admin@test.local"
            || (audit.Details != null && audit.Details.Contains(UiTestMarker))
            || dbContext.NewsRows.Any(row =>
                row.NewsId == audit.NewsId && row.Title.Contains(UiTestMarker)));

    private static IQueryable<NewsAgentRunRequestEntity> RunRequestResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsAgentRunRequests.Where(request =>
            request.RequestedBy == "url-ingestion-probe@queenzone.local"
            || request.RequestedBy == "url-ingestion-full-probe@queenzone.local"
            || (request.ArticleUrl != null && request.ArticleUrl.Contains("qz-url-ingestion-probe")));

    private static IQueryable<NewsAgentRunnerHeartbeatEntity> HeartbeatResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsAgentRunnerHeartbeats.Where(heartbeat =>
            heartbeat.RunnerId.Contains("url-ingestion")
            && heartbeat.RunnerId.Contains("probe"));

    private static IQueryable<NewsCandidateEntity> CandidateResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsCandidates.Where(candidate =>
            candidate.SourceUrl.Contains("qz-url-ingestion-probe")
            || candidate.CanonicalUrl.Contains("qz-url-ingestion-probe")
            || candidate.SourceUrl.Contains("qz-discovery-promo-probe")
            || candidate.CanonicalUrl.Contains("qz-discovery-promo-probe"));

    private static IQueryable<NewsDiscoverySourceEntity> DiscoverySourceResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsDiscoverySources.Where(source =>
            source.Key.StartsWith("discovery-promo-probe-"));

    private static IQueryable<MemberAccount> MemberResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.MemberAccounts.Where(member =>
            member.Email.EndsWith("@example.com")
            || member.Email.EndsWith("@example.test")
            || member.Email.EndsWith("@test.local")
            || member.Email.Contains("pm-probe-")
            || member.Email.Contains("forum-write-probe-")
            || member.Email.Contains("photo-submission-probe-")
            || member.Email.Contains("article-submission-probe-")
            || member.Email.Contains("member-account-probe-")
            // uie2e-{runId}-{fixture}-{n}: RealDataPageTest marker convention (Web.E2E),
            // e.g. CommunitySubmissionWorkflowTests member seeding for #546.
            || member.Email.Contains(UiTestMarker));

    private static IQueryable<PrivateMessageEntity> PrivateMessageResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.PrivateMessages.Where(message =>
            message.Body.Contains("Probe concurrent")
            || message.Body.Contains("Probe reply")
            // uie2e-{runId}-{fixture}-{n}: RealDataPageTest marker convention (Web.E2E),
            // e.g. PrivateMessagingWorkflowTests message bodies for #547.
            || message.Body.Contains(UiTestMarker));

    private static IQueryable<PrivateConversationEntity> PrivateConversationResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.PrivateConversations.Where(conversation =>
            dbContext.MemberAccounts.Any(member =>
                member.Email.Contains(UiTestMarker)
                && (member.Id == conversation.MemberLowId || member.Id == conversation.MemberHighId)));

    private static IQueryable<PrivateConversationParticipantEntity> PrivateConversationParticipantResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.PrivateConversationParticipants.Where(participant =>
            dbContext.MemberAccounts.Any(member =>
                member.Id == participant.MemberId && member.Email.Contains(UiTestMarker)));

    private static IQueryable<MemberMessageBlockEntity> MemberMessageBlockResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.MemberMessageBlocks.Where(block =>
            dbContext.MemberAccounts.Any(member =>
                member.Email.Contains(UiTestMarker)
                && (member.Id == block.BlockerMemberId || member.Id == block.BlockedMemberId)));

    private static IQueryable<MemberFollowEntity> MemberFollowResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.MemberFollows.Where(follow =>
            dbContext.MemberAccounts.Any(member =>
                member.Email.Contains(UiTestMarker)
                && (member.Id == follow.FollowerMemberId || member.Id == follow.FollowedMemberId)));

    private static IQueryable<ModernForumThreadEntity> ForumThreadResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.ModernForumThreads.Where(thread =>
            thread.Title.Contains("forum-write-probe-")
            || thread.Title.Contains(UiTestMarker)
            || thread.StartedByDisplayName.Contains(UiTestMarker));

    private static IQueryable<ModernForumPostEntity> ForumPostResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.ModernForumPosts.Where(post =>
            post.BodyHtml.Contains(UiTestMarker)
            || post.AuthorDisplayName.Contains(UiTestMarker)
            || dbContext.ModernForumThreads.Any(thread =>
                thread.Id == post.ThreadId && thread.Title.Contains(UiTestMarker)));

    private static IQueryable<PhotoSubmissionEntity> PhotoSubmissionResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.PhotoSubmissions.Where(submission =>
            submission.Title.Contains("photo-submission-probe-")
            || submission.Title.Contains(UiTestMarker));

    private static IQueryable<PhotoSubmissionAuditLogEntity> PhotoSubmissionAuditResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.PhotoSubmissionAuditLogs.Where(audit =>
            dbContext.PhotoSubmissions.Any(submission =>
                submission.Id == audit.PhotoSubmissionId
                && submission.Title.Contains(UiTestMarker)));

    private static IQueryable<ArticleSubmissionEntity> ArticleSubmissionResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.ArticleSubmissions.Where(submission =>
            submission.Title.Contains("article-submission-probe-")
            || submission.Title.Contains(UiTestMarker));

    // No existing probe seeds NewsSuggestions; this covers CommunitySubmissionWorkflowTests (#546)
    // only. If CleanupCreatedRowsAsync ever fails to run, this catches it in the nightly scan.
    private static IQueryable<NewsSuggestionEntity> NewsSuggestionResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.NewsSuggestions.Where(suggestion =>
            suggestion.Url.Contains(UiTestMarker)
            || (suggestion.Title != null && suggestion.Title.Contains(UiTestMarker)));

    // CommunitySubmissionWorkflowTests (#1596) trivia submit + reject.
    private static IQueryable<TriviaFactSubmissionEntity> TriviaFactSubmissionResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.TriviaFactSubmissions.Where(submission =>
            submission.Text.Contains(UiTestMarker));

    private static IQueryable<TriviaFactSubmissionAuditLogEntity> TriviaFactSubmissionAuditResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.TriviaFactSubmissionAuditLogs.Where(audit =>
            (audit.Details != null && audit.Details.Contains(UiTestMarker))
            || dbContext.TriviaFactSubmissions.Any(submission =>
                submission.Id == audit.TriviaFactSubmissionId
                && submission.Text.Contains(UiTestMarker)));

    // CommunitySubmissionWorkflowTests (#1596) fan-performance submit + reject.
    private static IQueryable<FanPerformanceSubmissionEntity> FanPerformanceSubmissionResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.FanPerformanceSubmissions.Where(submission =>
            submission.Title.Contains(UiTestMarker)
            || submission.CoveredSong.Contains(UiTestMarker)
            || submission.PerformedBy.Contains(UiTestMarker)
            || (submission.Description != null && submission.Description.Contains(UiTestMarker)));

    private static IQueryable<FanPerformanceSubmissionAuditLogEntity> FanPerformanceSubmissionAuditResidueQuery(
        QueenZoneDbContext dbContext) =>
        dbContext.FanPerformanceSubmissionAuditLogs.Where(audit =>
            (audit.Details != null && audit.Details.Contains(UiTestMarker))
            || dbContext.FanPerformanceSubmissions.Any(submission =>
                submission.Id == audit.FanPerformanceSubmissionId
                && submission.Title.Contains(UiTestMarker)));

    private static IQueryable<PhotoAdminAuditLogEntity> PhotoAdminAuditResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.PhotoAdminAuditLogs.Where(audit =>
            audit.ActorEmail == "admin@test.local"
            || audit.ActorEmail == "photo-submission-probe@queenzone.local"
            || (audit.Details != null && audit.Details.Contains(UiTestMarker)));

    private static IQueryable<SearchDocumentEntity> SearchDocumentResidueQuery(QueenZoneDbContext dbContext) =>
        dbContext.SearchDocuments.Where(document =>
            document.Title.Contains(UiTestMarker)
            || document.SourceKey.Contains(UiTestMarker)
            || document.Body.Contains(UiTestMarker)
            || document.Url.Contains(UiTestMarker)
            || (document.Summary != null && document.Summary.Contains(UiTestMarker))
            || (document.AuthorDisplayName != null && document.AuthorDisplayName.Contains(UiTestMarker)));

    internal readonly record struct SearchDocumentResidueRef(string ContentType, string SourceKey);

    internal static string FormatSearchDocumentResidueFailure(IReadOnlyList<SearchDocumentResidueRef> leftovers)
    {
        if (leftovers.Count == 0)
        {
            return "Residue found in SearchDocument.";
        }

        var details = string.Join(
            ", ",
            leftovers.Select(item => $"{item.ContentType} {item.SourceKey}"));
        return $"Residue found in SearchDocument: {details}.";
    }

    private static void SeedUiTestResidue(QueenZoneDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var marker = $"{UiTestMarker}predicate-proof";
        var markedMemberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var photoSubmissionId = Guid.NewGuid();
        var triviaSubmissionId = Guid.NewGuid();
        var fanPerformanceSubmissionId = Guid.NewGuid();

        dbContext.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = markedMemberId,
                Email = $"{marker}@e2e.queenzone.local",
                NormalizedEmail = $"{marker}@e2e.queenzone.local".ToUpperInvariant(),
                DisplayName = marker,
                CreatedAt = now.UtcDateTime,
            },
            new MemberAccount
            {
                Id = otherMemberId,
                Email = "residue-proof-other@e2e.queenzone.local",
                NormalizedEmail = "RESIDUE-PROOF-OTHER@E2E.QUEENZONE.LOCAL",
                DisplayName = "Residue proof other",
                CreatedAt = now.UtcDateTime,
            });

        dbContext.NewsAuditLogs.Add(new NewsAuditLogEntity
        {
            NewsId = 900001,
            Action = "proof",
            ActorEmail = "residue-proof@e2e.queenzone.local",
            OccurredAt = now.UtcDateTime,
        });

        dbContext.PhotoSubmissions.Add(new PhotoSubmissionEntity
        {
            Id = photoSubmissionId,
            SubmitterMemberId = markedMemberId,
            Title = marker,
            BlobPath = marker,
            WebOptimizedBlobPath = marker,
            ThumbnailBlobPath = marker,
            OriginalFileName = "proof.png",
            MimeType = "image/png",
            SubmittedAt = now,
        });
        dbContext.PhotoSubmissionAuditLogs.Add(new PhotoSubmissionAuditLogEntity
        {
            PhotoSubmissionId = photoSubmissionId,
            Action = "proof",
            ActorEmail = "residue-proof@e2e.queenzone.local",
            OccurredAt = now,
        });
        dbContext.PhotoAdminAuditLogs.Add(new PhotoAdminAuditLogEntity
        {
            PicId = 900001,
            Action = "proof",
            ActorEmail = "residue-proof@e2e.queenzone.local",
            OccurredAt = now,
            Details = marker,
        });
        dbContext.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = markedMemberId,
            Title = marker,
            Slug = marker,
            Body = marker,
        });
        dbContext.NewsSuggestions.Add(new NewsSuggestionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = markedMemberId,
            Url = $"https://example.com/{marker}",
            UrlHash = new string('a', 64),
            Title = marker,
            SubmittedAt = now,
        });
        dbContext.TriviaFactSubmissions.Add(new TriviaFactSubmissionEntity
        {
            Id = triviaSubmissionId,
            SubmitterMemberId = markedMemberId,
            Text = marker,
            SubmittedAt = now,
        });
        dbContext.TriviaFactSubmissionAuditLogs.Add(new TriviaFactSubmissionAuditLogEntity
        {
            TriviaFactSubmissionId = triviaSubmissionId,
            Action = "proof",
            ActorEmail = "residue-proof@e2e.queenzone.local",
            OccurredAt = now,
            Details = marker,
        });
        dbContext.FanPerformanceSubmissions.Add(new FanPerformanceSubmissionEntity
        {
            Id = fanPerformanceSubmissionId,
            SubmitterMemberId = markedMemberId,
            Title = marker,
            CoveredSong = marker,
            PerformedBy = marker,
            BlobPath = marker,
            OriginalFileName = "proof.mp3",
            MimeType = "audio/mpeg",
            SubmittedAt = now,
            RightsDeclaredAt = now,
        });
        dbContext.FanPerformanceSubmissionAuditLogs.Add(new FanPerformanceSubmissionAuditLogEntity
        {
            FanPerformanceSubmissionId = fanPerformanceSubmissionId,
            Action = "proof",
            ActorEmail = "residue-proof@e2e.queenzone.local",
            OccurredAt = now,
            Details = marker,
        });

        dbContext.PrivateConversations.Add(new PrivateConversationEntity
        {
            Id = conversationId,
            MemberLowId = markedMemberId,
            MemberHighId = otherMemberId,
            CreatedAt = now,
            LastMessageAt = now,
            LastMessageSortKey = 1,
            LastMessagePreview = marker,
            LastMessageSenderId = markedMemberId,
        });
        dbContext.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
        {
            ConversationId = conversationId,
            MemberId = markedMemberId,
        });
        dbContext.PrivateMessages.Add(new PrivateMessageEntity
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderMemberId = markedMemberId,
            Body = marker,
            CreatedAt = now,
            SortKey = 1,
        });
        dbContext.MemberMessageBlocks.Add(new MemberMessageBlockEntity
        {
            Id = Guid.NewGuid(),
            BlockerMemberId = markedMemberId,
            BlockedMemberId = otherMemberId,
            CreatedAt = now,
        });
        dbContext.MemberFollows.Add(new MemberFollowEntity
        {
            Id = Guid.NewGuid(),
            FollowerMemberId = markedMemberId,
            FollowedMemberId = otherMemberId,
            CreatedAt = now,
        });
        dbContext.SearchDocuments.Add(new SearchDocumentEntity
        {
            Id = Guid.NewGuid(),
            SourceKey = marker,
            ContentType = "proof",
            Title = marker,
            Body = marker,
            Url = $"/{marker}",
            IndexedAt = now,
        });
    }

    private static async Task CreateExcludedResidueTestTablesAsync(QueenZoneDbContext dbContext)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE NEWS_T (
                NEWS_ID INTEGER PRIMARY KEY,
                TITLE TEXT NOT NULL,
                SLUG TEXT NULL,
                EDITOR_EMAIL TEXT NULL
            );
            INSERT INTO NEWS_T (NEWS_ID, TITLE, SLUG, EDITOR_EMAIL)
            VALUES (900001, 'uie2e-predicate-proof', NULL, NULL);

            CREATE TABLE ModernForumThread (
                Id INTEGER PRIMARY KEY,
                Title TEXT NOT NULL,
                StartedByDisplayName TEXT NOT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO ModernForumThread (Id, Title, StartedByDisplayName)
            VALUES (900001, 'uie2e-predicate-proof', 'Residue proof');

            CREATE TABLE ModernForumPost (
                Id INTEGER PRIMARY KEY,
                ThreadId INTEGER NOT NULL,
                BodyHtml TEXT NOT NULL,
                AuthorDisplayName TEXT NOT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO ModernForumPost (Id, ThreadId, BodyHtml, AuthorDisplayName)
            VALUES (900001, 900001, 'uie2e-predicate-proof', 'Residue proof');
            """);
    }
    private static bool IsCheckEnabled(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(connectionString)
            && string.Equals(
                Environment.GetEnvironmentVariable("RUN_LEGACY_PROBE_RESIDUE_CHECK"),
                "true",
                StringComparison.OrdinalIgnoreCase);
    }
}

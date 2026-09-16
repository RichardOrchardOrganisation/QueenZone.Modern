using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class QueenZoneDbContext : DbContext
{
    public QueenZoneDbContext(DbContextOptions<QueenZoneDbContext> options)
        : base(options)
    {
    }

    public DbSet<NewsTableRow> NewsRows => Set<NewsTableRow>();

    public DbSet<NewsAuditLogEntity> NewsAuditLogs => Set<NewsAuditLogEntity>();

    public DbSet<MemberAccount> MemberAccounts => Set<MemberAccount>();

    public DbSet<MemberExternalLogin> MemberExternalLogins => Set<MemberExternalLogin>();

    public DbSet<MemberAccountDeletionAuditLogEntity> MemberAccountDeletionAuditLogs =>
        Set<MemberAccountDeletionAuditLogEntity>();

    public DbSet<ModernForumCategoryEntity> ModernForumCategories => Set<ModernForumCategoryEntity>();

    public DbSet<ModernForumThreadEntity> ModernForumThreads => Set<ModernForumThreadEntity>();

    public DbSet<ModernForumPostEntity> ModernForumPosts => Set<ModernForumPostEntity>();

    public DbSet<ForumPostAttachmentEntity> ForumPostAttachments => Set<ForumPostAttachmentEntity>();

    public DbSet<ForumPollEntity> ForumPolls => Set<ForumPollEntity>();

    public DbSet<ForumPollOptionEntity> ForumPollOptions => Set<ForumPollOptionEntity>();

    public DbSet<ForumPollVoteEntity> ForumPollVotes => Set<ForumPollVoteEntity>();

    public DbSet<HomePollEntity> HomePolls => Set<HomePollEntity>();

    public DbSet<HomePollOptionEntity> HomePollOptions => Set<HomePollOptionEntity>();

    public DbSet<HomePollVoteEntity> HomePollVotes => Set<HomePollVoteEntity>();

    public DbSet<QuizEntity> Quizzes => Set<QuizEntity>();

    public DbSet<QuizQuestionEntity> QuizQuestions => Set<QuizQuestionEntity>();

    public DbSet<QuizOptionEntity> QuizOptions => Set<QuizOptionEntity>();

    public DbSet<QuizAttemptEntity> QuizAttempts => Set<QuizAttemptEntity>();

    public DbSet<NewsDiscoverySourceEntity> NewsDiscoverySources => Set<NewsDiscoverySourceEntity>();

    public DbSet<NewsCandidateEntity> NewsCandidates => Set<NewsCandidateEntity>();

    public DbSet<NewsCandidateEvidenceEntity> NewsCandidateEvidence => Set<NewsCandidateEvidenceEntity>();

    public DbSet<NewsAiRunEntity> NewsAiRuns => Set<NewsAiRunEntity>();

    public DbSet<NewsAgentGuidanceRevisionEntity> NewsAgentGuidanceRevisions => Set<NewsAgentGuidanceRevisionEntity>();

    public DbSet<NewsAgentDraftEntity> NewsAgentDrafts => Set<NewsAgentDraftEntity>();

    public DbSet<NewsAgentRunLeaseEntity> NewsAgentRunLeases => Set<NewsAgentRunLeaseEntity>();

    public DbSet<NewsAgentRunRequestEntity> NewsAgentRunRequests => Set<NewsAgentRunRequestEntity>();

    public DbSet<NewsAgentRunnerHeartbeatEntity> NewsAgentRunnerHeartbeats => Set<NewsAgentRunnerHeartbeatEntity>();

    public DbSet<QueenHistoryEventEntity> QueenHistoryEvents => Set<QueenHistoryEventEntity>();

    public DbSet<PhotoSubmissionEntity> PhotoSubmissions => Set<PhotoSubmissionEntity>();

    public DbSet<PhotoSubmissionAuditLogEntity> PhotoSubmissionAuditLogs => Set<PhotoSubmissionAuditLogEntity>();

    public DbSet<FanPerformanceSubmissionEntity> FanPerformanceSubmissions => Set<FanPerformanceSubmissionEntity>();

    public DbSet<FanPerformanceSubmissionAuditLogEntity> FanPerformanceSubmissionAuditLogs =>
        Set<FanPerformanceSubmissionAuditLogEntity>();

    public DbSet<FanPerformanceReportEntity> FanPerformanceReports => Set<FanPerformanceReportEntity>();

    public DbSet<PhotoAdminAuditLogEntity> PhotoAdminAuditLogs => Set<PhotoAdminAuditLogEntity>();

    public DbSet<ArticleSubmissionEntity> ArticleSubmissions => Set<ArticleSubmissionEntity>();

    public DbSet<EditorialArticleEntity> EditorialArticles => Set<EditorialArticleEntity>();

    public DbSet<NewsSuggestionEntity> NewsSuggestions => Set<NewsSuggestionEntity>();

    public DbSet<HelpRequestEntity> HelpRequests => Set<HelpRequestEntity>();

    public DbSet<QueenLinkCheckEntity> QueenLinkChecks => Set<QueenLinkCheckEntity>();

    public DbSet<PrivateConversationEntity> PrivateConversations => Set<PrivateConversationEntity>();

    public DbSet<PrivateConversationParticipantEntity> PrivateConversationParticipants =>
        Set<PrivateConversationParticipantEntity>();

    public DbSet<PrivateMessageEntity> PrivateMessages => Set<PrivateMessageEntity>();

    public DbSet<PrivateMessageReportEntity> PrivateMessageReports => Set<PrivateMessageReportEntity>();

    public DbSet<PrivateMessageReportAuditLogEntity> PrivateMessageReportAuditLogs =>
        Set<PrivateMessageReportAuditLogEntity>();

    public DbSet<ForumPostReportEntity> ForumPostReports => Set<ForumPostReportEntity>();

    public DbSet<ForumPostReportAuditLogEntity> ForumPostReportAuditLogs =>
        Set<ForumPostReportAuditLogEntity>();

    public DbSet<MemberMessageBlockEntity> MemberMessageBlocks => Set<MemberMessageBlockEntity>();

    public DbSet<MemberFollowEntity> MemberFollows => Set<MemberFollowEntity>();

    public DbSet<MemberSocialLinkEntity> MemberSocialLinks => Set<MemberSocialLinkEntity>();

    public DbSet<SearchDocumentEntity> SearchDocuments => Set<SearchDocumentEntity>();

    public DbSet<SearchReindexLeaseEntity> SearchReindexLeases => Set<SearchReindexLeaseEntity>();

    public DbSet<SearchReindexRunRequestEntity> SearchReindexRunRequests => Set<SearchReindexRunRequestEntity>();

    public DbSet<MobileAuthAuthorizationCodeEntity> MobileAuthAuthorizationCodes =>
        Set<MobileAuthAuthorizationCodeEntity>();

    public DbSet<MobileAuthRefreshTokenEntity> MobileAuthRefreshTokens => Set<MobileAuthRefreshTokenEntity>();

    public DbSet<DeviceTokenEntity> DeviceTokens => Set<DeviceTokenEntity>();

    public DbSet<NotificationPreferenceEntity> NotificationPreferences => Set<NotificationPreferenceEntity>();

    public DbSet<MemberTopicWatchEntity> MemberTopicWatches => Set<MemberTopicWatchEntity>();

    public DbSet<QuoteEntity> Quotes => Set<QuoteEntity>();

    public DbSet<TriviaFactEntity> TriviaFacts => Set<TriviaFactEntity>();

    public DbSet<TriviaFactSubmissionEntity> TriviaFactSubmissions => Set<TriviaFactSubmissionEntity>();

    public DbSet<TriviaFactSubmissionAuditLogEntity> TriviaFactSubmissionAuditLogs =>
        Set<TriviaFactSubmissionAuditLogEntity>();

    public DbSet<IdempotencyReceiptEntity> IdempotencyReceipts => Set<IdempotencyReceiptEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuoteEntity>(entity =>
        {
            entity.ToTable("QUEEN_QUOTE_T", table => table.ExcludeFromMigrations());
            entity.HasKey(quote => quote.QuoteId);

            entity.Property(quote => quote.QuoteId)
                .HasColumnName("QUEEN_QUOTE_ID")
                .ValueGeneratedOnAdd();
            entity.Property(quote => quote.Text).HasColumnName("QUEEN_QUOTE").HasMaxLength(QuoteValidation.MaxTextLength);
            entity.Property(quote => quote.WhoSaid).HasColumnName("WHO_SAID").HasMaxLength(QuoteValidation.MaxWhoSaidLength);
            entity.Property(quote => quote.Context).HasColumnName("CONTEXT").HasMaxLength(QuoteValidation.MaxContextLength).HasColumnType($"varchar({QuoteValidation.MaxContextLength})");
            entity.Property(quote => quote.SourceType).HasColumnName("SOURCE_TYPE").HasConversion<string>().HasMaxLength(50).HasColumnType("varchar(50)");
            entity.Property(quote => quote.SourceKey).HasColumnName("SOURCE_KEY").HasMaxLength(QuoteValidation.MaxSourceKeyLength).HasColumnType($"varchar({QuoteValidation.MaxSourceKeyLength})");
            entity.Property(quote => quote.CreatedAt).HasColumnName("CREATE_DATE");
            entity.Property(quote => quote.IsPublished)
                .HasColumnName("DISPLAY")
                .HasConversion(
                    value => value ? (byte)1 : (byte)0,
                    value => value == 1);

            entity.HasIndex(quote => new { quote.SourceType, quote.SourceKey })
                .IsUnique()
                .HasFilter("[SOURCE_TYPE] IS NOT NULL AND [SOURCE_KEY] IS NOT NULL")
                .HasDatabaseName("IX_QUEEN_QUOTE_T_Source");
        });

        modelBuilder.Entity<TriviaFactEntity>(entity =>
        {
            entity.ToTable("TriviaFacts");
            entity.HasKey(fact => fact.Id);

            entity.Property(fact => fact.Text).HasMaxLength(TriviaValidation.MaxTextLength).IsRequired();
            entity.Property(fact => fact.Category).HasMaxLength(TriviaValidation.MaxCategoryLength);
            entity.Property(fact => fact.Difficulty).HasMaxLength(TriviaValidation.MaxDifficultyLength);
            entity.Property(fact => fact.Source).HasMaxLength(TriviaValidation.MaxSourceLength);
            entity.Property(fact => fact.IsPublished).IsRequired();
            entity.Property(fact => fact.CreatedAt).IsRequired();

            entity.HasIndex(fact => new { fact.IsPublished, fact.Category })
                .HasDatabaseName("IX_TriviaFacts_Published_Category");
        });

        modelBuilder.Entity<TriviaFactSubmissionEntity>(entity =>
        {
            entity.ToTable("TriviaFactSubmissions");
            entity.HasKey(submission => submission.Id);

            entity.Property(submission => submission.Text)
                .HasMaxLength(TriviaValidation.MaxTextLength)
                .IsRequired();
            entity.Property(submission => submission.Category).HasMaxLength(TriviaValidation.MaxCategoryLength);
            entity.Property(submission => submission.Difficulty).HasMaxLength(TriviaValidation.MaxDifficultyLength);
            entity.Property(submission => submission.SourceNote).HasMaxLength(TriviaValidation.MaxSourceNoteLength);
            entity.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
            entity.Property(submission => submission.SubmittedAt).IsRequired();
            entity.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
            entity.Property(submission => submission.ReviewNotes).HasMaxLength(500);
            entity.Property(submission => submission.RejectionReason).HasMaxLength(500);

            entity.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_TriviaFactSubmissions_Status_SubmittedAt");

            entity.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_TriviaFactSubmissions_Submitter_SubmittedAt");

            entity.HasOne(submission => submission.Submitter)
                .WithMany()
                .HasForeignKey(submission => submission.SubmitterMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriviaFactSubmissionAuditLogEntity>(entity =>
        {
            entity.ToTable("TriviaFactSubmissionAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);

            entity.HasIndex(log => new { log.TriviaFactSubmissionId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_TriviaFactSubmissionAuditLog_Submission_OccurredAt");

            entity.HasOne(log => log.Submission)
                .WithMany(submission => submission.AuditLogs)
                .HasForeignKey(log => log.TriviaFactSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NewsTableRow>(entity =>
        {
            entity.ToTable("NEWS_T", table => table.ExcludeFromMigrations());
            entity.HasKey(row => row.NewsId);

            entity.Property(row => row.NewsId)
                .HasColumnName("NEWS_ID")
                .ValueGeneratedOnAdd();
            entity.Property(row => row.Title).HasColumnName("TITLE").HasMaxLength(150);
            entity.Property(row => row.Excerpt).HasColumnName("EXCERPT").HasMaxLength(NewsValidation.MaxExcerptLength);
            entity.Property(row => row.Body).HasColumnName("ARTICLE");
            entity.Property(row => row.PublishedAt).HasColumnName("DATE");
            entity.Property(row => row.SourceUrl).HasColumnName("SOURCE_URL").HasMaxLength(NewsValidation.MaxSourceUrlLength);
            entity.Property(row => row.Slug).HasColumnName("SLUG").HasMaxLength(200);
            entity.Property(row => row.CreatedAt).HasColumnName("CREATED_AT");
            entity.Property(row => row.UpdatedAt).HasColumnName("UPDATED_AT");
            entity.Property(row => row.EditorEmail).HasColumnName("EDITOR_EMAIL").HasMaxLength(256);
            entity.Property(row => row.UserId).HasColumnName("USER_ID");
            entity.Property(row => row.Type).HasColumnName("TYPE");
            entity.Property(row => row.QueenOnline).HasColumnName("QUEEN_ONLINE");
            entity.Property(row => row.ImageBlobKey)
                .HasColumnName("IMAGE_BLOB_KEY")
                .HasMaxLength(NewsValidation.MaxImageBlobKeyLength);
            entity.Property(row => row.ImageGalleryPicId).HasColumnName("IMAGE_GALLERY_PIC_ID");
            entity.Property(row => row.ForumTopicId).HasColumnName("FORUM_TOPIC_ID");
            entity.Property(row => row.IsPublished)
                .HasColumnName("DISPLAY")
                .HasConversion(
                    value => value ? 1 : 0,
                    value => value == 1);
        });

        modelBuilder.Entity<NewsAuditLogEntity>(entity =>
        {
            entity.ToTable("NewsAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.NewsId).IsRequired();
            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);
            entity.Property(log => log.OccurredAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .IsRequired();

            entity.HasIndex(log => new { log.NewsId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsAuditLog_NewsId_OccurredAt");
        });

        modelBuilder.Entity<MemberAccount>(entity =>
        {
            entity.ToTable("MemberAccounts");
            entity.HasKey(account => account.Id);

            entity.Property(account => account.Email).HasMaxLength(256).IsRequired();
            entity.Property(account => account.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(account => account.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(account => account.AvatarUrl).HasMaxLength(512);
            entity.Property(account => account.PasswordHash).HasMaxLength(512);
            entity.Property(account => account.CreatedAt).IsRequired();
            entity.Property(account => account.LastLoginAt);
            entity.Property(account => account.MessagePrivacy)
                .HasConversion<byte>()
                .IsRequired()
                .HasDefaultValue(MemberMessagePrivacy.Members);
            entity.Property(account => account.IsSuspended).IsRequired().HasDefaultValue(false);
            entity.Property(account => account.SuspendedAt);
            entity.Property(account => account.SuspendedReason).HasMaxLength(1000);
            entity.Property(account => account.SuspendedByAdminEmail).HasMaxLength(256);
            entity.Property(account => account.DeletionRequestedAt);
            entity.Property(account => account.DeletionRecoveryDisplayName).HasMaxLength(100);
            entity.Property(account => account.DeletionRecoveryAvatarUrl).HasMaxLength(512);
            entity.Property(account => account.PersonalDataPurgedAt);

            entity.HasIndex(account => account.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("IX_MemberAccounts_NormalizedEmail");

            entity.HasIndex(account => account.IsSuspended)
                .HasDatabaseName("IX_MemberAccounts_IsSuspended");

            entity.HasIndex(account => new { account.DeletionRequestedAt, account.PersonalDataPurgedAt })
                .HasDatabaseName("IX_MemberAccounts_DeletionRequestedAt_PersonalDataPurgedAt");
        });

        modelBuilder.Entity<MemberAccountDeletionAuditLogEntity>(entity =>
        {
            entity.ToTable("MemberAccountDeletionAuditLog");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.HasIndex(log => new { log.MemberAccountId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_MemberAccountDeletionAuditLog_MemberAccountId_OccurredAt");
        });

        modelBuilder.Entity<MemberExternalLogin>(entity =>
        {
            entity.ToTable("MemberExternalLogins");
            entity.HasKey(login => login.Id);

            entity.Property(login => login.Provider).HasMaxLength(50).IsRequired();
            entity.Property(login => login.ProviderKey).HasMaxLength(256).IsRequired();
            entity.Property(login => login.Email).HasMaxLength(256).IsRequired();
            entity.Property(login => login.LinkedAt).IsRequired();

            entity.HasIndex(login => new { login.Provider, login.ProviderKey })
                .IsUnique()
                .HasDatabaseName("IX_MemberExternalLogins_Provider_ProviderKey");

            entity.HasOne<MemberAccount>()
                .WithMany()
                .HasForeignKey(login => login.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModernForumCategoryEntity>(entity =>
        {
            entity.ToTable("ModernForumCategory", table => table.ExcludeFromMigrations());
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).HasMaxLength(100).IsRequired();
            entity.Property(category => category.Description).HasMaxLength(400);
            entity.HasIndex(category => category.LegacyForumId)
                .IsUnique()
                .HasDatabaseName("UQ_ModernForumCategory_LegacyForumId");
        });

        modelBuilder.Entity<ModernForumThreadEntity>(entity =>
        {
            entity.ToTable("ModernForumThread", table => table.ExcludeFromMigrations());
            entity.HasKey(thread => thread.Id);
            entity.Property(thread => thread.Title).HasMaxLength(200).IsRequired();
            entity.Property(thread => thread.StartedByDisplayName).HasMaxLength(100).IsRequired();
            entity.Property(thread => thread.IsHidden).IsRequired().HasDefaultValue(false);
            entity.Property(thread => thread.StarterAttachment).HasMaxLength(120);
            entity.Property(thread => thread.StarterFileSize).HasMaxLength(12);
            entity.HasIndex(thread => thread.LegacyTopicId)
                .IsUnique()
                .HasDatabaseName("UQ_ModernForumThread_LegacyTopicId");
            entity.HasOne(thread => thread.Category)
                .WithMany()
                .HasForeignKey(thread => thread.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModernForumPostEntity>(entity =>
        {
            entity.ToTable("ModernForumPost", table =>
            {
                table.ExcludeFromMigrations();
                table.HasTrigger("TR_ModernForumPost_RefreshArchiveAuthorSummary");
            });
            entity.HasKey(post => post.Id);
            entity.Property(post => post.AuthorDisplayName).HasMaxLength(100).IsRequired();
            entity.Property(post => post.BodyHtml).IsUnicode().HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(post => post.SignatureHtml).IsUnicode().HasColumnType("nvarchar(max)");
            entity.Property(post => post.Attachment).HasMaxLength(120).IsUnicode(false);
            entity.Property(post => post.FileSize).HasMaxLength(12).IsUnicode(false);
            entity.Property(post => post.EditCount).HasDefaultValue(0);
            entity.Property(post => post.IsHidden).IsRequired().HasDefaultValue(false);
            entity.Property(post => post.UpdatedAt).IsConcurrencyToken();
            entity.HasIndex(post => post.LegacyPostId)
                .IsUnique()
                .HasDatabaseName("UQ_ModernForumPost_LegacyPostId");
            entity.HasIndex(post => new { post.AuthorMemberId, post.PostedAt })
                .HasDatabaseName("IX_ModernForumPost_AuthorMemberId_PostedAt");
            entity.HasIndex(post => new { post.AuthorLegacyUserId, post.PostedAt })
                .HasDatabaseName("IX_ModernForumPost_AuthorLegacyUserId_PostedAt");
            entity.HasOne(post => post.Thread)
                .WithMany()
                .HasForeignKey(post => post.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForumPostAttachmentEntity>(entity =>
        {
            entity.ToTable("ForumPostAttachments");
            entity.HasKey(attachment => attachment.Id);

            entity.Property(attachment => attachment.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(attachment => attachment.BlobPath).HasMaxLength(512).IsRequired();
            entity.Property(attachment => attachment.ContainerName).HasMaxLength(64).IsRequired();
            entity.Property(attachment => attachment.MimeType).HasMaxLength(100).IsRequired();
            entity.Property(attachment => attachment.UploadedAt).IsRequired();
            entity.Property(attachment => attachment.DownloadCount).HasDefaultValue(0);

            entity.HasIndex(attachment => attachment.LegacyPostId)
                .HasDatabaseName("IX_ForumPostAttachments_LegacyPostId");
            entity.HasIndex(attachment => attachment.PostId)
                .HasDatabaseName("IX_ForumPostAttachments_PostId");

            // ModernForumPost is excluded from EF migrations; SQL Server migration adds the FK in SQL.
            entity.HasOne(attachment => attachment.Post)
                .WithMany()
                .HasForeignKey(attachment => attachment.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForumPollEntity>(entity =>
        {
            entity.ToTable("ForumPolls");
            entity.HasKey(poll => poll.Id);
            entity.Property(poll => poll.Question).HasMaxLength(300).IsRequired();
            entity.Property(poll => poll.CreatedAt).IsRequired();
            entity.HasIndex(poll => poll.LegacyTopicId)
                .IsUnique()
                .HasDatabaseName("UQ_ForumPolls_LegacyTopicId");
            entity.HasIndex(poll => poll.ThreadId)
                .IsUnique()
                .HasDatabaseName("UQ_ForumPolls_ThreadId");
            entity.HasOne(poll => poll.Thread)
                .WithMany()
                .HasForeignKey(poll => poll.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(poll => poll.Options)
                .WithOne(option => option.Poll)
                .HasForeignKey(option => option.PollId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(poll => poll.Votes)
                .WithOne(vote => vote.Poll)
                .HasForeignKey(vote => vote.PollId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForumPollOptionEntity>(entity =>
        {
            entity.ToTable("ForumPollOptions");
            entity.HasKey(option => option.Id);
            entity.Property(option => option.OptionText).HasMaxLength(200).IsRequired();
            entity.HasIndex(option => new { option.PollId, option.DisplayOrder })
                .HasDatabaseName("IX_ForumPollOptions_PollId_DisplayOrder");
        });

        modelBuilder.Entity<ForumPollVoteEntity>(entity =>
        {
            entity.ToTable("ForumPollVotes");
            entity.HasKey(vote => vote.Id);
            entity.Property(vote => vote.VotedAt).IsRequired();
            entity.HasIndex(vote => new { vote.PollId, vote.MemberAccountId, vote.OptionId })
                .IsUnique()
                .HasDatabaseName("UQ_ForumPollVotes_Poll_Member_Option");
            entity.HasOne(vote => vote.Option)
                .WithMany(option => option.Votes)
                .HasForeignKey(vote => vote.OptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NewsDiscoverySourceEntity>(entity =>
        {
            entity.ToTable("NewsDiscoverySources");
            entity.HasKey(source => source.Id);

            entity.Property(source => source.Key).HasMaxLength(100).IsRequired();
            entity.Property(source => source.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(source => source.HomepageUrl).HasMaxLength(2000).IsRequired();
            entity.Property(source => source.FeedOrSiteUrl).HasMaxLength(2000);
            entity.Property(source => source.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(source => source.TrustTier).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(source => source.RelevanceKeywords).HasMaxLength(1000);
            entity.Property(source => source.CreatedAt).IsRequired();
            entity.Property(source => source.UpdatedAt).IsRequired();

            entity.HasIndex(source => source.Key)
                .IsUnique()
                .HasDatabaseName("IX_NewsDiscoverySources_Key");
        });

        modelBuilder.Entity<NewsCandidateEntity>(entity =>
        {
            entity.ToTable("NewsCandidates");
            entity.HasKey(candidate => candidate.Id);

            entity.Property(candidate => candidate.SourceUrl).HasMaxLength(2000).IsRequired();
            entity.Property(candidate => candidate.CanonicalUrl).HasMaxLength(2000).IsRequired();
            entity.Property(candidate => candidate.CanonicalUrlHash).HasMaxLength(64).IsRequired();
            entity.Property(candidate => candidate.SourceTitle).HasMaxLength(500).IsRequired();
            entity.Property(candidate => candidate.ContentHash).HasMaxLength(64);
            entity.Property(candidate => candidate.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(candidate => candidate.RelevanceScore).HasPrecision(5, 4);
            entity.Property(candidate => candidate.ConfidenceScore).HasPrecision(5, 4);
            entity.Property(candidate => candidate.ReviewNotes).HasMaxLength(2000);
            entity.Property(candidate => candidate.DiscoveredAt).IsRequired();
            entity.Property(candidate => candidate.CreatedAt).IsRequired();
            entity.Property(candidate => candidate.UpdatedAt).IsRequired();

            entity.HasIndex(candidate => candidate.CanonicalUrlHash)
                .IsUnique()
                .HasDatabaseName("IX_NewsCandidates_CanonicalUrlHash");

            entity.HasIndex(candidate => new { candidate.Status, candidate.DiscoveredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsCandidates_Status_DiscoveredAt");

            entity.HasIndex(candidate => candidate.ContentHash)
                .HasDatabaseName("IX_NewsCandidates_ContentHash");

            entity.HasOne(candidate => candidate.Source)
                .WithMany()
                .HasForeignKey(candidate => candidate.SourceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(candidate => candidate.DuplicateOfCandidate)
                .WithMany()
                .HasForeignKey(candidate => candidate.DuplicateOfCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NewsCandidateEvidenceEntity>(entity =>
        {
            entity.ToTable("NewsCandidateEvidence");
            entity.HasKey(evidence => evidence.Id);

            entity.Property(evidence => evidence.SourceUrl).HasMaxLength(2000).IsRequired();
            entity.Property(evidence => evidence.CanonicalUrl).HasMaxLength(2000).IsRequired();
            entity.Property(evidence => evidence.SourceName).HasMaxLength(200).IsRequired();
            entity.Property(evidence => evidence.SourceTrustTier).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(evidence => evidence.FetchedTitle).HasMaxLength(500).IsRequired();
            entity.Property(evidence => evidence.Excerpt).HasMaxLength(4000);
            entity.Property(evidence => evidence.ContentHash).HasMaxLength(64);
            entity.Property(evidence => evidence.Etag).HasMaxLength(256);
            entity.Property(evidence => evidence.FetchedAt).IsRequired();
            entity.Property(evidence => evidence.CreatedAt).IsRequired();

            entity.HasIndex(evidence => new { evidence.CandidateId, evidence.FetchedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsCandidateEvidence_CandidateId_FetchedAt");

            entity.HasOne(evidence => evidence.Candidate)
                .WithMany(candidate => candidate.Evidence)
                .HasForeignKey(evidence => evidence.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NewsAiRunEntity>(entity =>
        {
            entity.ToTable("NewsAiRuns");
            entity.HasKey(run => run.Id);

            entity.Property(run => run.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(run => run.ModelProvider).HasMaxLength(100).IsRequired();
            entity.Property(run => run.ModelId).HasMaxLength(200).IsRequired();
            entity.Property(run => run.PromptVersion).HasMaxLength(100).IsRequired();
            entity.Property(run => run.GuidanceRevisionId);
            entity.Property(run => run.GuidanceRevisionNumber);
            entity.Property(run => run.GuidanceContentHash).HasMaxLength(64);
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(run => run.EstimatedCostUsd).HasPrecision(10, 6);
            entity.Property(run => run.StructuredResultJson).HasMaxLength(8000);
            entity.Property(run => run.ErrorMessage).HasMaxLength(2000);
            entity.Property(run => run.StartedAt).IsRequired();
            entity.Property(run => run.CreatedAt).IsRequired();

            entity.HasIndex(run => new { run.CandidateId, run.StartedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsAiRuns_CandidateId_StartedAt");

            entity.HasOne(run => run.Candidate)
                .WithMany(candidate => candidate.AiRuns)
                .HasForeignKey(run => run.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NewsAgentGuidanceRevisionEntity>(entity =>
        {
            entity.ToTable("NewsAgentGuidanceRevisions");
            entity.HasKey(revision => revision.Id);

            entity.Property(revision => revision.Type)
                .HasConversion(
                    value => NewsAgentGuidanceText.ToStorageType(value),
                    value => NewsAgentGuidanceText.ParseType(value))
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(revision => revision.RevisionNumber).IsRequired();
            entity.Property(revision => revision.Content).HasMaxLength(4000).IsRequired();
            entity.Property(revision => revision.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(revision => revision.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(revision => revision.CreatedAt).IsRequired();
            entity.Property(revision => revision.CreatedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(revision => revision.PublishedByEmail).HasMaxLength(320);
            if (Database.IsSqlServer())
            {
                entity.Property(revision => revision.RowVersion).IsRowVersion();
            }
            else
            {
                // SQLite and in-memory providers do not generate rowversion; persist a client token.
                entity.Property(revision => revision.RowVersion)
                    .IsConcurrencyToken()
                    .IsRequired()
                    .ValueGeneratedNever();
            }

            entity.HasIndex(revision => new { revision.Type, revision.RevisionNumber })
                .IsUnique()
                .HasDatabaseName("UX_NewsAgentGuidanceRevisions_Type_RevisionNumber");

            entity.HasIndex(revision => revision.Type, "UX_NewsAgentGuidanceRevisions_Type_Published")
                .IsUnique()
                .HasFilter("[Status] = 'Published'");

            entity.HasIndex(revision => revision.Type, "UX_NewsAgentGuidanceRevisions_Type_Draft")
                .IsUnique()
                .HasFilter("[Status] = 'Draft'");
        });

        modelBuilder.Entity<NewsAgentDraftEntity>(entity =>
        {
            entity.ToTable("NewsAgentDrafts");
            entity.HasKey(draft => draft.Id);

            entity.Property(draft => draft.ProposedTitle).HasMaxLength(500).IsRequired();
            entity.Property(draft => draft.ProposedSlug).HasMaxLength(200);
            entity.Property(draft => draft.ProposedExcerpt).HasMaxLength(2000).IsRequired();
            entity.Property(draft => draft.ProposedBody).IsRequired();
            entity.Property(draft => draft.AttributionText).HasMaxLength(2000);
            entity.Property(draft => draft.SourceNotes).HasMaxLength(2000);
            entity.Property(draft => draft.ConfidenceNotes).HasMaxLength(2000);
            entity.Property(draft => draft.CreatedAt).IsRequired();
            entity.Property(draft => draft.UpdatedAt).IsRequired();

            entity.HasIndex(draft => draft.CandidateId)
                .IsUnique()
                .HasDatabaseName("IX_NewsAgentDrafts_CandidateId");

            entity.HasOne(draft => draft.Candidate)
                .WithOne(candidate => candidate.Draft)
                .HasForeignKey<NewsAgentDraftEntity>(draft => draft.CandidateId)
                .OnDelete(DeleteBehavior.ClientCascade);

            entity.HasOne(draft => draft.AiRun)
                .WithMany()
                .HasForeignKey(draft => draft.AiRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NewsAgentRunLeaseEntity>(entity =>
        {
            entity.ToTable("NewsAgentRunLeases");
            entity.HasKey(lease => lease.LeaseName);

            entity.Property(lease => lease.LeaseName).HasMaxLength(100).IsRequired();
            entity.Property(lease => lease.HolderId).HasMaxLength(64).IsRequired();
            entity.Property(lease => lease.AcquiredAtUtc).IsRequired();
            entity.Property(lease => lease.ExpiresAtUtc).IsRequired();
        });

        modelBuilder.Entity<NewsAgentRunRequestEntity>(entity =>
        {
            entity.ToTable("NewsAgentRunRequests");
            entity.HasKey(request => request.Id);

            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(request => request.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(request => request.RequestedBy).HasMaxLength(256).IsRequired();
            entity.Property(request => request.RequestedAtUtc).IsRequired();
            entity.Property(request => request.ArticleUrl).HasMaxLength(2000);
            entity.Property(request => request.GenerateDraft).IsRequired();
            entity.Property(request => request.RunnerId).HasMaxLength(100);
            entity.Property(request => request.Summary).HasMaxLength(2000);
            entity.Property(request => request.ErrorMessage).HasMaxLength(2000);
            entity.Property(request => request.ActiveKey).HasMaxLength(20);
            entity.Property(request => request.UpdatedAtUtc).IsRequired();

            entity.HasIndex(request => request.ActiveKey)
                .IsUnique()
                .HasFilter("[ActiveKey] IS NOT NULL")
                .HasDatabaseName("UX_NewsAgentRunRequests_ActiveKey");
            entity.HasIndex(request => new { request.Status, request.RequestedAtUtc })
                .HasDatabaseName("IX_NewsAgentRunRequests_Status_RequestedAtUtc");
        });

        modelBuilder.Entity<NewsAgentRunnerHeartbeatEntity>(entity =>
        {
            entity.ToTable("NewsAgentRunnerHeartbeats");
            entity.HasKey(heartbeat => heartbeat.RunnerId);
            entity.Property(heartbeat => heartbeat.RunnerId).HasMaxLength(100).IsRequired();
            entity.Property(heartbeat => heartbeat.LastSeenAtUtc).IsRequired();
        });

        modelBuilder.Entity<QueenHistoryEventEntity>(entity =>
        {
            entity.ToTable("QueenHistoryEvents");
            entity.HasKey(historyEvent => historyEvent.Id);

            entity.Property(historyEvent => historyEvent.Title).HasMaxLength(200).IsRequired();
            entity.Property(historyEvent => historyEvent.Summary).HasMaxLength(1000).IsRequired();
            entity.Property(historyEvent => historyEvent.EventDate).IsRequired();
            entity.Property(historyEvent => historyEvent.DatePrecision).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(historyEvent => historyEvent.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(historyEvent => historyEvent.Importance).IsRequired();
            entity.Property(historyEvent => historyEvent.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(historyEvent => historyEvent.SourceKey).HasMaxLength(200).IsRequired();
            entity.Property(historyEvent => historyEvent.SourceUrl).HasMaxLength(2000);
            entity.Property(historyEvent => historyEvent.IsPublished).IsRequired();
            entity.Property(historyEvent => historyEvent.CreatedAt).IsRequired();
            entity.Property(historyEvent => historyEvent.UpdatedAt).IsRequired();
            if (Database.IsSqlServer())
            {
                entity.Property(historyEvent => historyEvent.RowVersion).IsRowVersion();
            }
            else
            {
                entity.Property(historyEvent => historyEvent.RowVersion)
                    .IsConcurrencyToken()
                    .IsRequired()
                    .ValueGeneratedNever();
            }

            entity.HasIndex(historyEvent => new { historyEvent.IsPublished, historyEvent.DatePrecision, historyEvent.EventDate })
                .HasDatabaseName("IX_QueenHistoryEvents_Published_Date");

            entity.HasIndex(historyEvent => new { historyEvent.SourceType, historyEvent.SourceKey })
                .IsUnique()
                .HasDatabaseName("IX_QueenHistoryEvents_Source");
        });

        modelBuilder.Entity<PhotoSubmissionEntity>(entity =>
        {
            entity.ToTable("PhotoSubmissions");
            entity.HasKey(submission => submission.Id);

            entity.Property(submission => submission.Title).HasMaxLength(200).IsRequired();
            entity.Property(submission => submission.Description).HasMaxLength(1000);
            entity.Property(submission => submission.SuggestedCategory).HasMaxLength(100);
            entity.Property(submission => submission.ApprovedCategory).HasMaxLength(100);
            entity.Property(submission => submission.BlobPath).HasMaxLength(512).IsRequired();
            entity.Property(submission => submission.WebOptimizedBlobPath).HasMaxLength(512).IsRequired();
            entity.Property(submission => submission.ThumbnailBlobPath).HasMaxLength(512).IsRequired();
            entity.Property(submission => submission.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(submission => submission.MimeType).HasMaxLength(100).IsRequired();
            entity.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
            entity.Property(submission => submission.SubmittedAt).IsRequired();
            entity.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
            entity.Property(submission => submission.ReviewNotes).HasMaxLength(500);
            entity.Property(submission => submission.RejectionReason).HasMaxLength(500);

            entity.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PhotoSubmissions_Status_SubmittedAt");

            entity.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PhotoSubmissions_Submitter_SubmittedAt");

            entity.HasOne(submission => submission.Submitter)
                .WithMany()
                .HasForeignKey(submission => submission.SubmitterMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ArticleSubmissionEntity>(entity =>
        {
            entity.ToTable("ArticleSubmissions");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Title).HasMaxLength(300).IsRequired();
            entity.Property(a => a.Slug).HasMaxLength(300).IsRequired();
            entity.Property(a => a.Excerpt).HasMaxLength(500);
            entity.Property(a => a.Body).IsRequired();
            entity.Property(a => a.CoverImageBlobPath).HasMaxLength(512);
            entity.Property(a => a.Tags).HasMaxLength(500);
            entity.Property(a => a.Status).HasMaxLength(50).IsRequired();
            entity.Property(a => a.ReviewerEmail).HasMaxLength(256);
            entity.Property(a => a.ReviewNotes).HasMaxLength(1000);
            entity.Property(a => a.RejectionReason).HasMaxLength(1000);

            entity.HasIndex(a => a.Slug)
                .HasDatabaseName("IX_ArticleSubmissions_Slug");

            entity.HasIndex(a => new { a.Status, a.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_ArticleSubmissions_Status_SubmittedAt");

            entity.HasIndex(a => new { a.AuthorMemberId, a.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_ArticleSubmissions_Author_SubmittedAt");

            entity.HasOne(a => a.Author)
                .WithMany()
                .HasForeignKey(a => a.AuthorMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EditorialArticleEntity>(entity =>
        {
            entity.ToTable("EditorialArticles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Excerpt).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Body).IsRequired();
            entity.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Tags).HasMaxLength(500);
            entity.Property(x => x.Source).HasMaxLength(2000);
            entity.Property(x => x.ImageBlobKey).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.UpdatedBy).HasMaxLength(320).IsRequired();
            entity.Property(x => x.LiveTitle).HasMaxLength(300);
            entity.Property(x => x.LiveSlug).HasMaxLength(300);
            entity.Property(x => x.LiveExcerpt).HasMaxLength(500);
            entity.Property(x => x.LiveAuthorName).HasMaxLength(200);
            entity.Property(x => x.LiveCategory).HasMaxLength(100);
            entity.Property(x => x.LiveTags).HasMaxLength(500);
            entity.Property(x => x.LiveSource).HasMaxLength(2000);
            entity.Property(x => x.LiveImageBlobKey).HasMaxLength(1000);
            entity.HasIndex(x => x.LegacyArticleId).IsUnique().HasFilter("[LegacyArticleId] IS NOT NULL");
            entity.HasIndex(x => x.SourceSubmissionId).IsUnique().HasFilter("[SourceSubmissionId] IS NOT NULL");
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => x.LiveSlug).IsUnique().HasFilter("[LiveSlug] IS NOT NULL");
        });

        modelBuilder.Entity<SearchDocumentEntity>(entity =>
        {
            entity.ToTable("SearchDocument");
            entity.HasKey(document => document.Id);

            entity.Property(document => document.SourceKey).HasMaxLength(200).IsRequired();
            entity.Property(document => document.ContentType).HasMaxLength(50).IsRequired();
            entity.Property(document => document.Title).HasMaxLength(300).IsRequired();
            entity.Property(document => document.Body).IsRequired();
            entity.Property(document => document.Summary).HasMaxLength(500);
            entity.Property(document => document.Url).HasMaxLength(500).IsRequired();
            entity.Property(document => document.ImageUrl).HasMaxLength(512);
            entity.Property(document => document.Category).HasMaxLength(200);
            entity.Property(document => document.AuthorDisplayName).HasMaxLength(256);
            entity.Property(document => document.IndexedAt).IsRequired();

            entity.HasIndex(document => document.SourceKey)
                .IsUnique()
                .HasDatabaseName("UQ_SearchDocument_SourceKey");

            entity.HasIndex(document => new { document.ContentType, document.PublishedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_SearchDocument_ContentType_PublishedAt");
        });

        modelBuilder.Entity<SearchReindexLeaseEntity>(entity =>
        {
            entity.ToTable("SearchReindexLeases");
            entity.HasKey(lease => lease.LeaseName);

            entity.Property(lease => lease.LeaseName).HasMaxLength(100).IsRequired();
            entity.Property(lease => lease.HolderId).HasMaxLength(64).IsRequired();
            entity.Property(lease => lease.AcquiredAtUtc).IsRequired();
            entity.Property(lease => lease.ExpiresAtUtc).IsRequired();
        });

        modelBuilder.Entity<SearchReindexRunRequestEntity>(entity =>
        {
            entity.ToTable("SearchReindexRunRequests");
            entity.HasKey(request => request.Id);

            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(request => request.RequestedBy).HasMaxLength(256).IsRequired();
            entity.Property(request => request.RequestedAtUtc).IsRequired();
            entity.Property(request => request.RunnerId).HasMaxLength(100);
            entity.Property(request => request.Summary).HasMaxLength(2000);
            entity.Property(request => request.ErrorMessage).HasMaxLength(2000);
            entity.Property(request => request.ActiveKey).HasMaxLength(20);
            entity.Property(request => request.UpdatedAtUtc).IsRequired();

            entity.HasIndex(request => request.ActiveKey)
                .IsUnique()
                .HasFilter("[ActiveKey] IS NOT NULL")
                .HasDatabaseName("UX_SearchReindexRunRequests_ActiveKey");
            entity.HasIndex(request => new { request.Status, request.RequestedAtUtc })
                .HasDatabaseName("IX_SearchReindexRunRequests_Status_RequestedAtUtc");
        });

        modelBuilder.Entity<FanPerformanceSubmissionEntity>(entity =>
        {
            entity.ToTable("FanPerformanceSubmissions");
            entity.HasKey(submission => submission.Id);

            entity.Property(submission => submission.Title).HasMaxLength(200).IsRequired();
            entity.Property(submission => submission.CoveredSong).HasMaxLength(200).IsRequired();
            entity.Property(submission => submission.PerformedBy).HasMaxLength(200).IsRequired();
            entity.Property(submission => submission.Description).HasMaxLength(2000);
            entity.Property(submission => submission.BlobPath).HasMaxLength(512).IsRequired();
            entity.Property(submission => submission.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(submission => submission.MimeType).HasMaxLength(100).IsRequired();
            entity.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
            entity.Property(submission => submission.SubmittedAt).IsRequired();
            entity.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
            entity.Property(submission => submission.ReviewNotes).HasMaxLength(500);
            entity.Property(submission => submission.RejectionReason).HasMaxLength(500);
            entity.Property(submission => submission.RightsDeclaredAt).IsRequired();
            entity.Property(submission => submission.RightsDeclarationVersion).HasMaxLength(32).IsRequired();

            entity.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_FanPerformanceSubmissions_Status_SubmittedAt");

            entity.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_FanPerformanceSubmissions_Submitter_SubmittedAt");

            entity.HasOne(submission => submission.Submitter)
                .WithMany()
                .HasForeignKey(submission => submission.SubmitterMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FanPerformanceReportEntity>(entity =>
        {
            entity.ToTable("FanPerformanceReports");
            entity.HasKey(report => report.Id);

            entity.Property(report => report.Reason)
                .HasMaxLength(FanPerformanceReportLimits.MaxReasonLength)
                .IsRequired();
            entity.Property(report => report.Status)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(report => report.CreatedAt).IsRequired();
            entity.Property(report => report.TitleSnapshot)
                .HasMaxLength(FanPerformanceReportLimits.MaxSnapshotLength);
            entity.Property(report => report.PerformedBySnapshot)
                .HasMaxLength(FanPerformanceReportLimits.MaxSnapshotLength);
            entity.Property(report => report.ReviewedBy).HasMaxLength(256);

            entity.HasIndex(report => new { report.ReporterMemberId, report.StageId })
                .IsUnique()
                .HasFilter("[Status] = 'Open'")
                .HasDatabaseName("UX_FanPerformanceReports_Reporter_Stage_Open");

            entity.HasIndex(report => new { report.Status, report.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_FanPerformanceReports_Status_CreatedAt");

            entity.HasOne(report => report.Reporter)
                .WithMany()
                .HasForeignKey(report => report.ReporterMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FanPerformanceSubmissionAuditLogEntity>(entity =>
        {
            entity.ToTable("FanPerformanceSubmissionAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);

            entity.HasIndex(log => new { log.FanPerformanceSubmissionId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_FanPerformanceSubmissionAuditLog_Submission_OccurredAt");

            entity.HasOne(log => log.Submission)
                .WithMany(submission => submission.AuditLogs)
                .HasForeignKey(log => log.FanPerformanceSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhotoSubmissionAuditLogEntity>(entity =>
        {
            entity.ToTable("PhotoSubmissionAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);

            entity.HasIndex(log => new { log.PhotoSubmissionId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PhotoSubmissionAuditLog_Submission_OccurredAt");

            entity.HasOne(log => log.Submission)
                .WithMany(submission => submission.AuditLogs)
                .HasForeignKey(log => log.PhotoSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhotoAdminAuditLogEntity>(entity =>
        {
            entity.ToTable("PhotoAdminAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);

            entity.HasIndex(log => new { log.PicId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PhotoAdminAuditLog_PicId_OccurredAt");
        });

        modelBuilder.Entity<NewsSuggestionEntity>(entity =>
        {
            entity.ToTable("NewsSuggestions");
            entity.HasKey(suggestion => suggestion.Id);

            entity.Property(suggestion => suggestion.Url).HasMaxLength(2000).IsRequired();
            entity.Property(suggestion => suggestion.UrlHash).HasMaxLength(64).IsRequired();
            entity.Property(suggestion => suggestion.Title).HasMaxLength(300);
            entity.Property(suggestion => suggestion.Notes).HasMaxLength(1000);
            entity.Property(suggestion => suggestion.Status).HasMaxLength(50).IsRequired();
            entity.Property(suggestion => suggestion.SubmittedAt).IsRequired();
            entity.Property(suggestion => suggestion.ReviewerEmail).HasMaxLength(256);
            entity.Property(suggestion => suggestion.ReviewNotes).HasMaxLength(500);

            entity.HasIndex(suggestion => suggestion.UrlHash)
                .IsUnique()
                .HasFilter("[Status] IN ('Pending', 'UnderReview')")
                .HasDatabaseName("IX_NewsSuggestions_UrlHash_Active");

            entity.HasIndex(suggestion => new { suggestion.Status, suggestion.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsSuggestions_Status_SubmittedAt");

            entity.HasIndex(suggestion => new { suggestion.SubmitterMemberId, suggestion.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_NewsSuggestions_Submitter_SubmittedAt");

            entity.HasOne(suggestion => suggestion.Submitter)
                .WithMany()
                .HasForeignKey(suggestion => suggestion.SubmitterMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(suggestion => suggestion.DuplicateCandidate)
                .WithMany()
                .HasForeignKey(suggestion => suggestion.DuplicateCandidateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QueenLinkCheckEntity>(entity =>
        {
            entity.ToTable("QueenLinkChecks");
            entity.HasKey(check => check.QueenFeaturedSiteId);
            entity.Property(check => check.QueenFeaturedSiteId).ValueGeneratedNever();
            entity.Property(check => check.Url).HasMaxLength(500).IsRequired();
            entity.Property(check => check.LastCheckedAtUtc).IsRequired();
            entity.Property(check => check.IsAvailable).IsRequired();
            entity.Property(check => check.IsConfirmedDead).IsRequired();
            entity.Property(check => check.ConsecutiveFailureCount).IsRequired();
            entity.Property(check => check.LastError).HasMaxLength(500);
            entity.HasIndex(check => check.IsConfirmedDead);
            entity.HasIndex(check => check.LastCheckedAtUtc);
        });

        modelBuilder.Entity<PrivateConversationEntity>(entity =>
        {
            entity.ToTable("PrivateConversations");
            entity.HasKey(conversation => conversation.Id);

            entity.Property(conversation => conversation.LastMessagePreview)
                .HasMaxLength(PrivateMessageLimits.PreviewLength)
                .IsRequired();
            entity.Property(conversation => conversation.CreatedAt).IsRequired();
            entity.Property(conversation => conversation.LastMessageAt).IsRequired();
            entity.Property(conversation => conversation.LastMessageSortKey).IsRequired();

            entity.HasIndex(conversation => new { conversation.MemberLowId, conversation.MemberHighId })
                .IsUnique()
                .HasDatabaseName("IX_PrivateConversations_MemberPair");

            entity.HasIndex(conversation => conversation.LastMessageSortKey)
                .IsDescending()
                .HasDatabaseName("IX_PrivateConversations_LastMessageSortKey");

            entity.HasOne(conversation => conversation.MemberLow)
                .WithMany()
                .HasForeignKey(conversation => conversation.MemberLowId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(conversation => conversation.MemberHigh)
                .WithMany()
                .HasForeignKey(conversation => conversation.MemberHighId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrivateConversationParticipantEntity>(entity =>
        {
            entity.ToTable("PrivateConversationParticipants");
            entity.HasKey(participant => new { participant.ConversationId, participant.MemberId });

            entity.Property(participant => participant.IsArchived).IsRequired();
            entity.Property(participant => participant.IsRemoved).IsRequired();

            entity.HasIndex(participant => new { participant.MemberId, participant.IsArchived })
                .HasDatabaseName("IX_PrivateConversationParticipants_Member_Archived");

            entity.HasIndex(participant => new { participant.MemberId, participant.IsRemoved })
                .HasDatabaseName("IX_PrivateConversationParticipants_Member_Removed");

            entity.HasOne(participant => participant.Conversation)
                .WithMany(conversation => conversation.Participants)
                .HasForeignKey(participant => participant.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(participant => participant.Member)
                .WithMany()
                .HasForeignKey(participant => participant.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrivateMessageEntity>(entity =>
        {
            entity.ToTable("PrivateMessages");
            entity.HasKey(message => message.Id);

            entity.Property(message => message.Body)
                .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
                .IsRequired();
            entity.Property(message => message.CreatedAt).IsRequired();
            entity.Property(message => message.SortKey)
                .ValueGeneratedOnAdd()
                .IsRequired();

            entity.HasIndex(message => new { message.ConversationId, message.CreatedAt })
                .HasDatabaseName("IX_PrivateMessages_Conversation_CreatedAt");
            entity.HasIndex(message => new { message.ConversationId, message.SortKey })
                .HasDatabaseName("IX_PrivateMessages_Conversation_SortKey");
            entity.HasIndex(message => new { message.SenderMemberId, message.CreatedAt })
                .HasDatabaseName("IX_PrivateMessages_Sender_CreatedAt");

            entity.HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(message => message.Sender)
                .WithMany()
                .HasForeignKey(message => message.SenderMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrivateMessageReportEntity>(entity =>
        {
            entity.ToTable("PrivateMessageReports");
            entity.HasKey(report => report.Id);

            entity.Property(report => report.Reason)
                .HasMaxLength(PrivateMessageLimits.MaxReportReasonLength);
            entity.Property(report => report.Status)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(report => report.MessageBodySnapshot)
                .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
                .IsRequired();
            entity.Property(report => report.SenderDisplayNameSnapshot)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(report => report.CreatedAt).IsRequired();
            entity.Property(report => report.MessageCreatedAtSnapshot).IsRequired();
            entity.Property(report => report.MessageSortKeySnapshot).IsRequired();

            entity.HasIndex(report => new { report.ReporterMemberId, report.MessageId })
                .IsUnique()
                .HasDatabaseName("IX_PrivateMessageReports_Reporter_Message");

            entity.HasIndex(report => new { report.Status, report.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PrivateMessageReports_Status_CreatedAt");

            entity.HasIndex(report => report.ConversationId)
                .HasDatabaseName("IX_PrivateMessageReports_Conversation");

            entity.HasOne(report => report.Message)
                .WithMany()
                .HasForeignKey(report => report.MessageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(report => report.Conversation)
                .WithMany()
                .HasForeignKey(report => report.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(report => report.Reporter)
                .WithMany()
                .HasForeignKey(report => report.ReporterMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(report => report.Reported)
                .WithMany()
                .HasForeignKey(report => report.ReportedMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrivateMessageReportAuditLogEntity>(entity =>
        {
            entity.ToTable("PrivateMessageReportAuditLog");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);

            // No navigation/FK constraint to PrivateMessageReportEntity: this log must outlive
            // the report's retention-window purge (ADR 0015), so the relationship is
            // application-enforced only.
            entity.HasIndex(log => new { log.ReportId, log.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PrivateMessageReportAuditLog_ReportId_OccurredAt");
        });

        modelBuilder.Entity<ForumPostReportEntity>(entity =>
        {
            entity.ToTable("ForumPostReports");
            entity.HasKey(report => report.Id);
            entity.Property(report => report.Category).HasMaxLength(100).IsRequired();
            entity.Property(report => report.Details).HasMaxLength(ForumPostReportLimits.MaxDetailsLength);
            entity.Property(report => report.Status).HasMaxLength(50).IsRequired();
            entity.Property(report => report.PostBodySnapshot).IsRequired();
            entity.Property(report => report.AuthorDisplayNameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(report => report.ThreadTitleSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(report => report.CreatedAt).IsRequired();
            entity.Property(report => report.PostCreatedAtSnapshot).IsRequired();

            entity.HasIndex(report => new { report.ReporterMemberId, report.PostId })
                .IsUnique().HasDatabaseName("IX_ForumPostReports_Reporter_Post");
            entity.HasIndex(report => new { report.Status, report.CreatedAt })
                .IsDescending(false, true).HasDatabaseName("IX_ForumPostReports_Status_CreatedAt");
            entity.HasIndex(report => report.ReportedMemberId)
                .HasDatabaseName("IX_ForumPostReports_ReportedMember");

            entity.HasOne(report => report.Reporter).WithMany()
                .HasForeignKey(report => report.ReporterMemberId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(report => report.Reported).WithMany()
                .HasForeignKey(report => report.ReportedMemberId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ForumPostReportAuditLogEntity>(entity =>
        {
            entity.ToTable("ForumPostReportAuditLog");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
            entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(log => log.OccurredAt).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);
            entity.HasIndex(log => new { log.ReportId, log.OccurredAt })
                .IsDescending(false, true).HasDatabaseName("IX_ForumPostReportAuditLog_ReportId_OccurredAt");
        });

        modelBuilder.Entity<MemberMessageBlockEntity>(entity =>
        {
            entity.ToTable("MemberMessageBlocks");
            entity.HasKey(block => block.Id);

            entity.Property(block => block.CreatedAt).IsRequired();

            entity.HasIndex(block => new { block.BlockerMemberId, block.BlockedMemberId })
                .IsUnique()
                .HasDatabaseName("IX_MemberMessageBlocks_Blocker_Blocked");

            entity.HasIndex(block => block.BlockedMemberId)
                .HasDatabaseName("IX_MemberMessageBlocks_Blocked");

            entity.HasOne(block => block.Blocker)
                .WithMany()
                .HasForeignKey(block => block.BlockerMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(block => block.Blocked)
                .WithMany()
                .HasForeignKey(block => block.BlockedMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MemberFollowEntity>(entity =>
        {
            entity.ToTable("MemberFollows");
            entity.HasKey(follow => follow.Id);

            entity.Property(follow => follow.CreatedAt).IsRequired();

            entity.HasIndex(follow => new { follow.FollowerMemberId, follow.FollowedMemberId })
                .IsUnique()
                .HasDatabaseName("IX_MemberFollows_Follower_Followed");

            entity.HasIndex(follow => follow.FollowedMemberId)
                .HasDatabaseName("IX_MemberFollows_Followed");

            entity.HasOne(follow => follow.Follower)
                .WithMany()
                .HasForeignKey(follow => follow.FollowerMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(follow => follow.Followed)
                .WithMany()
                .HasForeignKey(follow => follow.FollowedMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HelpRequestEntity>(entity =>
        {
            entity.ToTable("HelpRequests");
            entity.HasKey(request => request.Id);

            entity.Property(request => request.Topic).HasMaxLength(50).IsRequired();
            entity.Property(request => request.Subject).HasMaxLength(200).IsRequired();
            entity.Property(request => request.Message).HasMaxLength(4000).IsRequired();
            entity.Property(request => request.Name).HasMaxLength(100).IsRequired();
            entity.Property(request => request.Email).HasMaxLength(256).IsRequired();
            entity.Property(request => request.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(request => request.Status).HasMaxLength(50).IsRequired();
            entity.Property(request => request.SubmittedAt).IsRequired();
            entity.Property(request => request.ReviewerEmail).HasMaxLength(256);
            entity.Property(request => request.ReviewNotes).HasMaxLength(500);

            entity.HasIndex(request => new { request.Status, request.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_HelpRequests_Status_SubmittedAt");

            entity.HasIndex(request => new { request.NormalizedEmail, request.SubmittedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_HelpRequests_NormalizedEmail_SubmittedAt");

            entity.HasIndex(request => new { request.MemberId, request.SubmittedAt })
                .IsDescending(false, true)
                .HasFilter("[MemberId] IS NOT NULL")
                .HasDatabaseName("IX_HelpRequests_Member_SubmittedAt");

            entity.HasOne(request => request.Member)
                .WithMany()
                .HasForeignKey(request => request.MemberId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MobileAuthAuthorizationCodeEntity>(entity =>
        {
            entity.ToTable("MobileAuthAuthorizationCodes");
            entity.HasKey(code => code.Id);

            entity.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(code => code.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(code => code.RedirectUri).HasMaxLength(500).IsRequired();
            entity.Property(code => code.CodeChallenge).HasMaxLength(128).IsRequired();
            entity.Property(code => code.ExpiresAt).IsRequired();
            entity.Property(code => code.CreatedAt).IsRequired();

            entity.HasIndex(code => code.CodeHash)
                .IsUnique()
                .HasDatabaseName("IX_MobileAuthAuthorizationCodes_CodeHash");

            entity.HasOne(code => code.MemberAccount)
                .WithMany()
                .HasForeignKey(code => code.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MobileAuthRefreshTokenEntity>(entity =>
        {
            entity.ToTable("MobileAuthRefreshTokens");
            entity.HasKey(token => token.Id);

            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(token => token.ExpiresAt).IsRequired();
            entity.Property(token => token.CreatedAt).IsRequired();
            entity.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);

            entity.HasIndex(token => token.TokenHash)
                .IsUnique()
                .HasDatabaseName("IX_MobileAuthRefreshTokens_TokenHash");

            entity.HasIndex(token => new { token.MemberAccountId, token.RevokedAt })
                .HasDatabaseName("IX_MobileAuthRefreshTokens_Member_Revoked");

            entity.HasOne(token => token.MemberAccount)
                .WithMany()
                .HasForeignKey(token => token.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceTokenEntity>(entity =>
        {
            entity.ToTable("DeviceTokens");
            entity.HasKey(token => token.Id);

            entity.Property(token => token.DeviceId).HasMaxLength(200).IsRequired();
            entity.Property(token => token.Platform).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(token => token.Token).HasMaxLength(4000).IsRequired();
            entity.Property(token => token.CreatedAt).IsRequired();
            entity.Property(token => token.UpdatedAt).IsRequired();

            entity.HasIndex(token => token.DeviceId)
                .IsUnique()
                .HasDatabaseName("IX_DeviceTokens_DeviceId");

            entity.HasIndex(token => token.MemberAccountId)
                .HasDatabaseName("IX_DeviceTokens_MemberAccountId");

            entity.HasOne(token => token.MemberAccount)
                .WithMany()
                .HasForeignKey(token => token.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreferenceEntity>(entity =>
        {
            entity.ToTable("NotificationPreferences");
            entity.HasKey(row => new { row.MemberAccountId, row.Category });

            entity.Property(row => row.Category).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(row => row.IsEnabled).IsRequired();
            entity.Property(row => row.UpdatedAt).IsRequired();

            entity.HasIndex(row => new { row.Category, row.IsEnabled })
                .HasDatabaseName("IX_NotificationPreferences_Category_IsEnabled");

            entity.HasOne(row => row.MemberAccount)
                .WithMany()
                .HasForeignKey(row => row.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MemberTopicWatchEntity>(entity =>
        {
            entity.ToTable("MemberTopicWatches");
            entity.HasKey(watch => new { watch.MemberAccountId, watch.TopicId });

            entity.Property(watch => watch.CreatedAt).IsRequired();

            entity.HasIndex(watch => watch.TopicId)
                .HasDatabaseName("IX_MemberTopicWatches_TopicId");

            entity.HasOne(watch => watch.MemberAccount)
                .WithMany()
                .HasForeignKey(watch => watch.MemberAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MemberSocialLinkEntity>(entity =>
        {
            entity.ToTable("MemberSocialLinks");
            entity.HasKey(row => new { row.MemberId, row.Channel });

            entity.Property(row => row.Channel).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Url).HasMaxLength(MemberSocialLinkUrl.MaxUrlLength).IsRequired();

            entity.HasOne(row => row.Member)
                .WithMany()
                .HasForeignKey(row => row.MemberId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HomePollEntity>(entity =>
        {
            entity.ToTable("HomePolls");
            entity.HasKey(poll => poll.Id);
            entity.Property(poll => poll.Question).HasMaxLength(HomePollValidation.QuestionMaxLength).IsRequired();
            entity.Property(poll => poll.CreatedAt).IsRequired();
            entity.HasIndex(poll => poll.IsCurrent)
                .IsUnique()
                .HasFilter("[IsCurrent] = 1")
                .HasDatabaseName("UX_HomePolls_IsCurrent");
            entity.HasMany(poll => poll.Options)
                .WithOne(option => option.Poll)
                .HasForeignKey(option => option.PollId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(poll => poll.Votes)
                .WithOne(vote => vote.Poll)
                .HasForeignKey(vote => vote.PollId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HomePollOptionEntity>(entity =>
        {
            entity.ToTable("HomePollOptions");
            entity.HasKey(option => option.Id);
            entity.Property(option => option.OptionText).HasMaxLength(HomePollValidation.OptionMaxLength).IsRequired();
            entity.HasIndex(option => new { option.PollId, option.DisplayOrder })
                .HasDatabaseName("IX_HomePollOptions_PollId_DisplayOrder");
        });

        modelBuilder.Entity<HomePollVoteEntity>(entity =>
        {
            entity.ToTable("HomePollVotes");
            entity.HasKey(vote => vote.Id);
            entity.Property(vote => vote.VotedAt).IsRequired();
            entity.HasIndex(vote => new { vote.PollId, vote.MemberAccountId })
                .IsUnique()
                .HasDatabaseName("UQ_HomePollVotes_Poll_Member");
            entity.HasOne(vote => vote.Option)
                .WithMany(option => option.Votes)
                .HasForeignKey(vote => vote.OptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuizEntity>(entity =>
        {
            entity.ToTable("Quizzes");
            entity.HasKey(quiz => quiz.Id);
            entity.Property(quiz => quiz.Title).HasMaxLength(QuizValidation.TitleMaxLength).IsRequired();
            entity.Property(quiz => quiz.Description).HasMaxLength(QuizValidation.DescriptionMaxLength);
            entity.Property(quiz => quiz.CreatedAt).IsRequired();
            entity.HasIndex(quiz => quiz.IsPublished)
                .HasDatabaseName("IX_Quizzes_IsPublished");
            entity.HasMany(quiz => quiz.Questions)
                .WithOne(question => question.Quiz)
                .HasForeignKey(question => question.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(quiz => quiz.Attempts)
                .WithOne(attempt => attempt.Quiz)
                .HasForeignKey(attempt => attempt.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizQuestionEntity>(entity =>
        {
            entity.ToTable("QuizQuestions");
            entity.HasKey(question => question.Id);
            entity.Property(question => question.QuestionText).HasMaxLength(QuizValidation.QuestionMaxLength).IsRequired();
            entity.HasIndex(question => new { question.QuizId, question.DisplayOrder })
                .HasDatabaseName("IX_QuizQuestions_QuizId_DisplayOrder");
            entity.HasMany(question => question.Options)
                .WithOne(option => option.Question)
                .HasForeignKey(option => option.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizOptionEntity>(entity =>
        {
            entity.ToTable("QuizOptions");
            entity.HasKey(option => option.Id);
            entity.Property(option => option.OptionText).HasMaxLength(QuizValidation.OptionMaxLength).IsRequired();
            entity.HasIndex(option => new { option.QuestionId, option.DisplayOrder })
                .HasDatabaseName("IX_QuizOptions_QuestionId_DisplayOrder");
        });

        modelBuilder.Entity<QuizAttemptEntity>(entity =>
        {
            entity.ToTable("QuizAttempts");
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.CompletedAt).IsRequired();
            entity.HasIndex(attempt => new { attempt.QuizId, attempt.CompletedAt })
                .HasDatabaseName("IX_QuizAttempts_QuizId_CompletedAt");
            entity.HasIndex(attempt => new { attempt.MemberAccountId, attempt.CompletedAt })
                .HasDatabaseName("IX_QuizAttempts_MemberAccountId_CompletedAt");
        });

        modelBuilder.Entity<IdempotencyReceiptEntity>(entity =>
        {
            entity.ToTable("IdempotencyReceipts");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.OperationKind)
                .HasMaxLength(IdempotencyLimits.OperationKindMaxLength)
                .IsRequired();
            entity.Property(row => row.PayloadHash)
                .HasMaxLength(IdempotencyLimits.PayloadHashLength)
                .IsRequired();
            entity.Property(row => row.Location).HasMaxLength(IdempotencyLimits.LocationMaxLength);
            entity.Property(row => row.ResponseBodyJson).IsRequired();
            entity.Property(row => row.CreatedAt).IsRequired();
            entity.Property(row => row.ExpiresAt).IsRequired();
            entity.HasIndex(row => new { row.MemberId, row.OperationKind, row.OperationId })
                .IsUnique()
                .HasDatabaseName("UX_IdempotencyReceipts_Member_Kind_Operation");
            entity.HasIndex(row => row.ExpiresAt)
                .HasDatabaseName("IX_IdempotencyReceipts_ExpiresAt");
        });
    }
}

using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Configurations;
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

    public DbSet<MemberDeletionBlobEntity> MemberDeletionBlobs => Set<MemberDeletionBlobEntity>();

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

    public DbSet<QuizSprintRunEntity> QuizSprintRuns => Set<QuizSprintRunEntity>();

    public DbSet<QuizQuestionSubmissionEntity> QuizQuestionSubmissions => Set<QuizQuestionSubmissionEntity>();

    public DbSet<QuizQuestionSubmissionOptionEntity> QuizQuestionSubmissionOptions =>
        Set<QuizQuestionSubmissionOptionEntity>();

    public DbSet<QuizQuestionSubmissionAuditLogEntity> QuizQuestionSubmissionAuditLogs =>
        Set<QuizQuestionSubmissionAuditLogEntity>();

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
        var sqlServer = Database.IsSqlServer();
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(QueenZoneDbContext).Assembly,
            type => type != typeof(QueenHistoryEventEntityConfiguration)
                && type != typeof(NewsAgentGuidanceRevisionEntityConfiguration));
        modelBuilder.ApplyConfiguration(new QueenHistoryEventEntityConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new NewsAgentGuidanceRevisionEntityConfiguration(sqlServer));
    }
}

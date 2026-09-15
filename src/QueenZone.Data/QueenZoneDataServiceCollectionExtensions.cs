using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QueenZone.Data;

public static class QueenZoneDataServiceCollectionExtensions
{
    public static IServiceCollection AddQueenZoneLegacyData(
        this IServiceCollection services,
        string connectionString,
        ForumDataOptions? forumDataOptions = null)
    {
        forumDataOptions ??= new ForumDataOptions();

        // IDbContextFactory first (singleton) for independent short-lived contexts used by
        // parallel admin/reporting reads (issue #335). Scoped QueenZoneDbContext is created
        // from the factory so request-scoped repositories still receive one context per scope.
        // Do not also call AddDbContext with the same options: that re-registers options
        // configuration as scoped and breaks singleton factory resolution.
        services.AddDbContextFactory<QueenZoneDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.CommandTimeout(QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds);
                    sql.EnableRetryOnFailure(
                        maxRetryCount: QueenZoneSqlServerOptions.MaxRetryCount,
                        maxRetryDelay: QueenZoneSqlServerOptions.MaxRetryDelay,
                        errorNumbersToAdd: null);
                }));
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<QueenZoneDbContext>>().CreateDbContext());

        // Warms the legacy NEWS_T column-availability probe cache before the app begins serving
        // requests, so EfNewsRepository/EfAdminNewsRepository never block a request thread on it
        // (issue #1161).
        services.AddHostedService<NewsColumnAvailabilityWarmupService>();

        services.AddScoped<INewsRepository, EfNewsRepository>();
        services.AddScoped<IArticlesRepository, EfArticlesRepository>();
        services.AddScoped<IBiographyRepository, EfBiographyRepository>();
        services.AddScoped<IQuoteRepository, EfQuoteRepository>();
        services.AddScoped<ITriviaRepository, EfTriviaRepository>();
        services.AddScoped<ITriviaFactSubmissionRepository, EfTriviaFactSubmissionRepository>();
        if (forumDataOptions.UseModernForumReads)
        {
            services.AddScoped<IForumRepository, ModernForumRepository>();
        }
        else
        {
            services.AddScoped<IForumRepository, LegacyForumRepository>();
        }

        services.AddScoped<IPhotoRepository, EfPhotoRepository>();
        services.AddScoped<IAdminPhotoRepository, EfAdminPhotoRepository>();
        services.AddScoped<IFanPerformanceRepository, EfFanPerformanceRepository>();
        services.AddScoped<IAdminFanPerformanceRepository, EfAdminFanPerformanceRepository>();
        services.AddScoped<ILegacyMemberLookupRepository, EfMemberLookupRepository>();
        services.AddScoped<IDiscographyRepository, EfDiscographyRepository>();
        services.AddScoped<INewsForumDiscussionLookup, EfNewsForumDiscussionLookup>();
        services.AddScoped<IAdminNewsRepository, EfAdminNewsRepository>();
        services.AddScoped<INewsAuditRepository, EfNewsAuditRepository>();
        services.AddScoped<IMemberAccountRepository, EfMemberAccountRepository>();
        services.AddScoped<IForumWriteRepository, EfForumWriteRepository>();
        services.AddScoped<IForumAttachmentRepository, EfForumAttachmentRepository>();
        services.AddScoped<IForumPollRepository, EfForumPollRepository>();
        services.AddScoped<IHomePollRepository, EfHomePollRepository>();
        services.AddScoped<IQuizRepository, EfQuizRepository>();
        services.AddScoped<INewsDiscoveryRepository, EfNewsDiscoveryRepository>();
        services.AddScoped<INewsAgentGuidanceRepository, EfNewsAgentGuidanceRepository>();
        services.AddScoped<INewsAgentRunLeaseService, EfNewsAgentRunLeaseService>();
        services.AddScoped<INewsAgentRunRequestRepository, EfNewsAgentRunRequestRepository>();
        services.AddScoped<IQueenHistoryRepository, EfQueenHistoryRepository>();
        services.AddScoped<IAdminQueenHistoryRepository, EfAdminQueenHistoryRepository>();
        services.AddScoped<IPhotoSubmissionRepository, EfPhotoSubmissionRepository>();
        services.AddScoped<IFanPerformanceSubmissionRepository, EfFanPerformanceSubmissionRepository>();
        services.AddScoped<IFanPerformanceReportRepository, EfFanPerformanceReportRepository>();
        services.AddScoped<IArticleSubmissionRepository, EfArticleSubmissionRepository>();
        services.AddScoped<IEditorialArticleRepository, EfEditorialArticleRepository>();
        services.AddScoped<IArticleRepository, EfArticleRepository>();
        services.AddScoped<INewsSuggestionRepository, EfNewsSuggestionRepository>();
        services.AddScoped<IHelpRequestRepository, EfHelpRequestRepository>();
        services.AddScoped<IPrivateMessageRepository, EfPrivateMessageRepository>();
        services.AddScoped<IForumPostReportRepository, EfForumPostReportRepository>();
        services.AddScoped<IMemberFollowRepository, EfMemberFollowRepository>();
        services.AddScoped<IMemberPublicActivityRepository, EfMemberPublicActivityRepository>();
        services.AddScoped<IForumArchiveAuthorRepository, EfForumArchiveAuthorRepository>();
        services.AddScoped<ILinksRepository, EfLinksRepository>();
        services.AddScoped<IFreddieTributeRepository, EfFreddieTributeRepository>();
        services.AddScoped<IAdminFreddieTributeRepository, EfAdminFreddieTributeRepository>();
        services.AddScoped<ISearchIndexService, EfSearchIndexService>();
        services.AddScoped<ISiteSearchService, EfSiteSearchService>();
        services.AddScoped<ISearchReindexRunLeaseService, EfSearchReindexRunLeaseService>();
        services.AddScoped<ISearchReindexRunRequestRepository, EfSearchReindexRunRequestRepository>();
        services.AddScoped<IMobileAuthGrantRepository, EfMobileAuthGrantRepository>();
        services.AddScoped<IDeviceTokenRepository, EfDeviceTokenRepository>();
        services.AddScoped<INotificationPreferenceRepository, EfNotificationPreferenceRepository>();
        services.AddScoped<ITopicWatchRepository, EfTopicWatchRepository>();
        services.AddScoped<ITopicWatchLookup>(sp => sp.GetRequiredService<ITopicWatchRepository>());
        services.AddScoped<ILiveActivityQueryService, EfLiveActivityQueryService>();
        services.AddScoped<IIdempotencyStore>(sp =>
            new EfIdempotencyStore(
                sp.GetRequiredService<QueenZoneDbContext>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }

    public static IServiceCollection AddQueenZoneInMemoryData(this IServiceCollection services)
    {
        var store = new SharedNewsStore(SampleNewsData.CreateSeedArticles());
        services.AddSingleton(store);
        services.AddSingleton<INewsRepository, InMemoryNewsRepository>();
        services.AddSingleton<IArticlesRepository>(sp => new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles(), sp.GetRequiredService<IEditorialArticleRepository>()));
        var biographyStore = new SharedBiographyStore(SampleBiographyData.CreateSeedChapters());
        services.AddSingleton(biographyStore);
        services.AddSingleton<IBiographyRepository>(_ => new InMemoryBiographyRepository(biographyStore));
        var quoteStore = new SharedQuoteStore(SampleQuoteData.CreateSeedQuotes());
        services.AddSingleton(quoteStore);
        services.AddSingleton<IQuoteRepository>(_ => new InMemoryQuoteRepository(quoteStore));
        var triviaStore = new SharedTriviaStore(SampleTriviaData.CreateSeedFacts());
        services.AddSingleton(triviaStore);
        services.AddSingleton<ITriviaRepository>(_ => new InMemoryTriviaRepository(triviaStore));
        services.AddSingleton<ITriviaFactSubmissionRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryTriviaFactSubmissionRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        var forumWriteRepository = new InMemoryForumWriteRepository();
        var forumAttachmentRepository = new InMemoryForumAttachmentRepository();
        var forumPollRepository = new InMemoryForumPollRepository();
        forumWriteRepository.AttachPollRepository(forumPollRepository);
        services.AddSingleton(forumWriteRepository);
        services.AddSingleton<IForumWriteRepository>(forumWriteRepository);
        services.AddSingleton<IForumAttachmentRepository>(forumAttachmentRepository);
        services.AddSingleton<IForumPollRepository>(forumPollRepository);
        var homePollStore = new SharedHomePollStore();
        services.AddSingleton(homePollStore);
        services.AddSingleton<IHomePollRepository>(sp =>
            new InMemoryHomePollRepository(homePollStore, sp.GetService<TimeProvider>()));
        var quizStore = new SharedQuizStore();
        services.AddSingleton(quizStore);
        services.AddSingleton<IQuizRepository>(sp =>
            new InMemoryQuizRepository(quizStore, sp.GetService<TimeProvider>()));
        services.AddSingleton<IForumRepository>(_ => new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(),
            SampleForumData.CreateSeedStats(),
            forumWriteRepository,
            forumAttachmentRepository));
        services.AddSingleton<INewsForumDiscussionLookup>(
            _ => new InMemoryNewsForumDiscussionLookup(forumWriteRepository));
        var photoStore = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        services.AddSingleton(photoStore);
        services.AddSingleton<IPhotoRepository>(_ => new InMemoryPhotoRepository(photoStore));
        services.AddSingleton<IAdminPhotoRepository>(_ => new InMemoryAdminPhotoRepository(photoStore));
        var fanPerformanceStore = new SharedFanPerformanceStore(SampleFanPerformanceData.CreateSeedPerformances());
        services.AddSingleton(fanPerformanceStore);
        services.AddSingleton<IFanPerformanceRepository>(_ => new InMemoryFanPerformanceRepository(fanPerformanceStore));
        services.AddSingleton<IAdminFanPerformanceRepository>(_ => new InMemoryAdminFanPerformanceRepository(fanPerformanceStore));
        services.AddSingleton<ILegacyMemberLookupRepository>(_ => new InMemoryLegacyMemberLookupRepository(SampleLegacyMemberData.CreateSeedMatches()));
        services.AddSingleton<IDiscographyRepository>(_ => new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()));
        var historyStore = new SharedQueenHistoryStore(SampleQueenHistoryData.CreateSeedEvents());
        services.AddSingleton(historyStore);
        services.AddSingleton<IQueenHistoryRepository>(_ => new InMemoryQueenHistoryRepository(historyStore));
        services.AddSingleton<IAdminQueenHistoryRepository>(_ => new InMemoryAdminQueenHistoryRepository(historyStore));
        services.AddSingleton<IAdminNewsRepository, InMemoryAdminNewsRepository>();
        services.AddSingleton<INewsAuditRepository, InMemoryNewsAuditRepository>();
        services.AddSingleton<IMemberAccountRepository, InMemoryMemberAccountRepository>();
        services.AddSingleton<ILiveActivityQueryService, InMemoryLiveActivityQueryService>();
        var discoveryStore = new SharedNewsDiscoveryStore();
        SampleNewsDiscoveryData.Seed(discoveryStore);
        services.AddSingleton(discoveryStore);
        services.AddSingleton<INewsDiscoveryRepository, InMemoryNewsDiscoveryRepository>();
        var guidanceStore = new SharedNewsAgentGuidanceStore();
        services.AddSingleton(guidanceStore);
        services.AddSingleton<INewsAgentGuidanceRepository, InMemoryNewsAgentGuidanceRepository>();
        services.AddSingleton<SharedNewsAgentLeaseStore>();
        services.AddSingleton<INewsAgentRunLeaseService, InMemoryNewsAgentRunLeaseService>();
        services.AddSingleton<SharedNewsAgentRunRequestStore>();
        services.AddSingleton<INewsAgentRunRequestRepository, InMemoryNewsAgentRunRequestRepository>();
        services.AddSingleton<IPhotoSubmissionRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryPhotoSubmissionRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IFanPerformanceSubmissionRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryFanPerformanceSubmissionRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IFanPerformanceReportRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryFanPerformanceReportRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<INewsSuggestionRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryNewsSuggestionRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IHelpRequestRepository, InMemoryHelpRequestRepository>();
        services.AddSingleton<IPrivateMessageRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryPrivateMessageRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IForumPostReportRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryForumPostReportRepository(
                forumWriteRepository,
                id => members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IMemberFollowRepository, InMemoryMemberFollowRepository>();

        services.AddSingleton<IArticleSubmissionRepository>(sp =>
        {
            var members = sp.GetRequiredService<IMemberAccountRepository>();
            return new InMemoryArticleSubmissionRepository(id =>
                members.FindByIdAsync(id).GetAwaiter().GetResult());
        });
        services.AddSingleton<IEditorialArticleRepository>(sp =>
            new InMemoryEditorialArticleRepository(sp.GetService<TimeProvider>()));
        services.AddSingleton<IArticleRepository>(sp =>
            new InMemoryArticleRepository(sp.GetRequiredService<IArticleSubmissionRepository>(), sp.GetRequiredService<IEditorialArticleRepository>()));
        services.AddSingleton<IMemberPublicActivityRepository, InMemoryMemberPublicActivityRepository>();
        services.AddSingleton<IForumArchiveAuthorRepository, InMemoryForumArchiveAuthorRepository>();
        services.AddSingleton<ILinksRepository>(_ => new InMemoryLinksRepository(SampleLinksData.CreateSeedCategories()));
        services.AddSingleton(_ => new SharedFreddieTributeStore(SampleFreddieTributeData.CreateSeedTributes()));
        services.AddSingleton<IFreddieTributeRepository, InMemoryFreddieTributeRepository>();
        services.AddSingleton<IAdminFreddieTributeRepository, InMemoryAdminFreddieTributeRepository>();
        services.AddSingleton<SharedSearchIndexStore>();
        services.AddSingleton<ISearchIndexService, InMemorySearchIndexService>();
        services.AddSingleton<ISiteSearchService, InMemorySiteSearchService>();
        services.AddSingleton<SharedSearchReindexLeaseStore>();
        services.AddSingleton<ISearchReindexRunLeaseService, InMemorySearchReindexRunLeaseService>();
        services.AddSingleton<SharedSearchReindexRunRequestStore>();
        services.AddSingleton<ISearchReindexRunRequestRepository, InMemorySearchReindexRunRequestRepository>();
        services.AddSingleton<SharedMobileAuthGrantStore>();
        services.AddSingleton<IMobileAuthGrantRepository, InMemoryMobileAuthGrantRepository>();
        services.AddSingleton<SharedDeviceTokenStore>();
        services.AddSingleton<IDeviceTokenRepository, InMemoryDeviceTokenRepository>();
        services.AddSingleton<SharedNotificationPreferenceStore>();
        services.AddSingleton<INotificationPreferenceRepository, InMemoryNotificationPreferenceRepository>();
        services.AddSingleton<ITopicWatchRepository, InMemoryTopicWatchRepository>();
        services.AddSingleton<ITopicWatchLookup>(sp => sp.GetRequiredService<ITopicWatchRepository>());
        services.AddSingleton<IIdempotencyStore>(sp =>
            new InMemoryIdempotencyStore(sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}


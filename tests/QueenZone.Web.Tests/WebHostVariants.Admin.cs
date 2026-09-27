using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.NewsAgent;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public static partial class WebHostVariants
{
    internal static FreddieTribute[] AdminFreddieTributeSeed() =>
    [
        new FreddieTribute(9003, "Duplicate", "Repeated tribute", "UK", "24 November 2001", "09:03"),
        new FreddieTribute(9002, "Duplicate", "Repeated tribute", "UK", "24 November 2001", "09:02"),
        new FreddieTribute(9001, "Moderator", "Prune me from the public page", "US", "24 November 2001", "09:01"),
    ];

    private static void AddIsolatedQuotes(IServiceCollection services, HostServiceContext context)
    {
        context.Quotes ??= new SharedQuoteStore();
        services.RemoveAll<IQuoteRepository>();
        services.AddSingleton<IQuoteRepository>(_ => new InMemoryQuoteRepository(context.Quotes));
    }

    private static void AddIsolatedAdminNews(IServiceCollection services, HostServiceContext context)
    {
        context.AdminNews ??= new SharedNewsStore();
        context.AdminDiscovery ??= new SharedNewsDiscoveryStore();
        context.AdminNewsMutations ??= new AdminNewsMutationOverrides();
        context.ConfigurableDiscovery ??= new ConfigurableNewsDiscoveryRepository(
            new InMemoryNewsDiscoveryRepository(context.AdminDiscovery));

        services.RemoveAll<SharedNewsStore>();
        services.RemoveAll<SharedNewsDiscoveryStore>();
        services.RemoveAll<INewsRepository>();
        services.RemoveAll<IAdminNewsRepository>();
        services.RemoveAll<INewsAuditRepository>();
        services.RemoveAll<INewsDiscoveryRepository>();
        services.AddSingleton(context.AdminNews);
        services.AddSingleton(context.AdminDiscovery);
        services.AddSingleton<INewsRepository>(_ => new InMemoryNewsRepository(context.AdminNews));
        services.AddSingleton<INewsAuditRepository>(_ => new InMemoryNewsAuditRepository(context.AdminNews));
        services.AddSingleton<IAdminNewsRepository>(_ =>
            new GatedAdminNewsRepository(
                new InMemoryAdminNewsRepository(context.AdminNews),
                context.AdminNewsMutations));
        services.AddSingleton<INewsDiscoveryRepository>(context.ConfigurableDiscovery);
    }

    private static void AddIsolatedAdminNewsDiscovery(IServiceCollection services, HostServiceContext context)
    {
        AddIsolatedAdminNews(services, context);
        context.NewsAi ??= new ConfigurableNewsAiClient();
        context.NewsAgentRunRequests ??= new SharedNewsAgentRunRequestStore();
        services.RemoveAll<INewsAiClient>();
        services.AddSingleton<INewsAiClient>(context.NewsAi);
        services.RemoveAll<SharedNewsAgentRunRequestStore>();
        services.RemoveAll<INewsAgentRunRequestRepository>();
        services.AddSingleton(context.NewsAgentRunRequests);
        services.AddSingleton<INewsAgentRunRequestRepository>(_ =>
            new InMemoryNewsAgentRunRequestRepository(context.NewsAgentRunRequests));
    }

    private static void AddIsolatedAdminBiography(IServiceCollection services, HostServiceContext context)
    {
        context.AdminBiography ??= new SharedBiographyStore();
        services.RemoveAll<SharedBiographyStore>();
        services.RemoveAll<IBiographyRepository>();
        services.AddSingleton(context.AdminBiography);
        services.AddSingleton<IBiographyRepository>(_ => new InMemoryBiographyRepository(context.AdminBiography));
    }

    private static void AddIsolatedAdminTimeline(IServiceCollection services, HostServiceContext context)
    {
        context.AdminTimeline ??= new SharedQueenHistoryStore();
        services.RemoveAll<SharedQueenHistoryStore>();
        services.RemoveAll<IQueenHistoryRepository>();
        services.RemoveAll<IAdminQueenHistoryRepository>();
        services.AddSingleton(context.AdminTimeline);
        services.AddSingleton<IQueenHistoryRepository>(_ => new InMemoryQueenHistoryRepository(context.AdminTimeline));
        services.AddSingleton<IAdminQueenHistoryRepository>(_ =>
            new InMemoryAdminQueenHistoryRepository(context.AdminTimeline));
    }

    private static void AddIsolatedAdminFreddieTributes(IServiceCollection services, HostServiceContext context)
    {
        context.AdminFreddieTributes ??= new SharedFreddieTributeStore(AdminFreddieTributeSeed());
        services.RemoveAll<SharedFreddieTributeStore>();
        services.RemoveAll<IFreddieTributeRepository>();
        services.RemoveAll<IAdminFreddieTributeRepository>();
        services.AddSingleton(context.AdminFreddieTributes);
        services.AddSingleton<IFreddieTributeRepository, InMemoryFreddieTributeRepository>();
        services.AddSingleton<IAdminFreddieTributeRepository, InMemoryAdminFreddieTributeRepository>();
    }

    private static void AddIsolatedAdminGuidance(IServiceCollection services, HostServiceContext context)
    {
        context.AdminGuidance ??= new SharedNewsAgentGuidanceStore();
        context.AdminDiscovery ??= new SharedNewsDiscoveryStore();
        services.RemoveAll<SharedNewsAgentGuidanceStore>();
        services.RemoveAll<INewsAgentGuidanceRepository>();
        services.RemoveAll<SharedNewsDiscoveryStore>();
        services.RemoveAll<INewsDiscoveryRepository>();
        services.AddSingleton(context.AdminGuidance);
        services.AddSingleton<INewsAgentGuidanceRepository>(_ =>
            new InMemoryNewsAgentGuidanceRepository(context.AdminGuidance));
        services.AddSingleton(context.AdminDiscovery);
        services.AddSingleton<INewsDiscoveryRepository>(_ =>
            new InMemoryNewsDiscoveryRepository(context.AdminDiscovery));
    }

    private static void AddIsolatedHomePollsThrowingPublish(IServiceCollection services, HostServiceContext context)
    {
        AddIsolatedHomePolls(services, context);
        services.RemoveAll<IHomePollRepository>();
        services.AddSingleton<IHomePollRepository>(_ =>
            new ThrowingPublishHomePollRepository(
                new InMemoryHomePollRepository(context.HomePolls!),
                AdminHomePollPublishFailures.UniqueCurrentConstraint()));
    }

    private static void AddGoogleAnalyticsTraffic(
        IServiceCollection services,
        GoogleAnalyticsTrafficSnapshot snapshot)
    {
        services.RemoveAll<IGoogleAnalyticsTrafficService>();
        services.AddSingleton<IGoogleAnalyticsTrafficService>(new StubGoogleAnalyticsTrafficService(snapshot));
    }

    private static GoogleAnalyticsTrafficSnapshot GaTrafficAvailableSnapshot() =>
        new(
            true,
            SessionsLast7Days: 1234,
            PageViewsLast7Days: 5678,
            ActiveUsersLast7Days: 321,
            TopPagesThisWeek:
            [
                new GoogleAnalyticsTopPage("/news", 456),
                new GoogleAnalyticsTopPage("/forum", 123),
            ],
            DailySessionsLast30Days:
            [
                new GoogleAnalyticsDailySession(DateOnly.FromDateTime(DateTime.UtcNow), 44),
            ]);

    private static void AddTimeoutHideForum(IServiceCollection services)
    {
        var timeout = SqlExceptionFactory.Create(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");
        services.RemoveAll<IForumWriteRepository>();
        services.AddSingleton<IForumWriteRepository>(
            new TimeoutHideForumWriteRepository(new InMemoryForumWriteRepository(), timeout));
    }

    private static void AddThrowingRevokeMobileAuth(IServiceCollection services)
    {
        services.RemoveAll<IMobileAuthGrantRepository>();
        services.AddSingleton<IMobileAuthGrantRepository>(new ThrowingRevokeMobileAuthGrantRepository());
    }
}

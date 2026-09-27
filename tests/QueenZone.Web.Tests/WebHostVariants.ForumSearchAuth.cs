using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public static partial class WebHostVariants
{
    public static readonly WebHostVariant TestingLockedForumTopic1002 = new(
        nameof(TestingLockedForumTopic1002),
        "Testing",
        NoSettings,
        HostServiceProfile.LockedForumTopic1002);

    public static readonly WebHostVariant TestingLegacyForumAttachmentMemoryBlobs = new(
        nameof(TestingLegacyForumAttachmentMemoryBlobs),
        "Testing",
        NoSettings,
        HostServiceProfile.LegacyForumAttachmentMemoryBlobs);

    public static readonly WebHostVariant TestingLegacyForumAttachmentMissingBlob = new(
        nameof(TestingLegacyForumAttachmentMissingBlob),
        "Testing",
        NoSettings,
        HostServiceProfile.LegacyForumAttachmentMissingBlob);

    public static readonly WebHostVariant TestingModernForumAttachmentDownload = new(
        nameof(TestingModernForumAttachmentDownload),
        "Testing",
        NoSettings,
        HostServiceProfile.ModernForumAttachmentDownload);

    public static readonly WebHostVariant TestingForumAttachmentMemoryBlob = new(
        nameof(TestingForumAttachmentMemoryBlob),
        "Testing",
        NoSettings,
        HostServiceProfile.ForumAttachmentMemoryBlob);

    public static readonly WebHostVariant SiteSearchTimeout = new(
        nameof(SiteSearchTimeout),
        "Testing",
        NoSettings,
        HostServiceProfile.SiteSearchTimeout);

    public static readonly WebHostVariant ThrowingSearchIndex = new(
        nameof(ThrowingSearchIndex),
        "Testing",
        NoSettings,
        HostServiceProfile.ThrowingSearchIndex);

    public static readonly WebHostVariant AnalyticsMeasurementConfigured = new(
        nameof(AnalyticsMeasurementConfigured),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{AnalyticsOptions.SectionName}:MeasurementId",
                "G-V2W56BZ3KZ"),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant WarmupThrowingNewsLatest = new(
        nameof(WarmupThrowingNewsLatest),
        "Testing",
        NoSettings,
        HostServiceProfile.WarmupThrowingNewsLatest);

    public static readonly WebHostVariant StrictHostFiltering = new(
        nameof(StrictHostFiltering),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{QueenZoneHostFilteringOptions.SectionName}:AllowedHosts",
                "www.queenzone.org;queenzone.org;*.azurewebsites.net"),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant ThrowingSprintBoardQuiz = new(
        nameof(ThrowingSprintBoardQuiz),
        "Testing",
        NoSettings,
        HostServiceProfile.ThrowingSprintBoardQuiz);

    public static readonly WebHostVariant TestingArchiveAuthorLinkRedirect = new(
        nameof(TestingArchiveAuthorLinkRedirect),
        "Testing",
        NoSettings,
        HostServiceProfile.None);

    public static readonly WebHostVariant ThrowingForumArchiveAuthor = new(
        nameof(ThrowingForumArchiveAuthor),
        "Testing",
        NoSettings,
        HostServiceProfile.ThrowingForumArchiveAuthor);

    public static readonly WebHostVariant TestingArchiveAuthorLinkProfile = new(
        nameof(TestingArchiveAuthorLinkProfile),
        "Testing",
        NoSettings,
        HostServiceProfile.None);

    public static readonly WebHostVariant ExternalCookieMobilePkce = new(
        nameof(ExternalCookieMobilePkce),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieMobilePkce);

    public static readonly WebHostVariant ExternalCookieMobilePkceMutableClock = new(
        nameof(ExternalCookieMobilePkceMutableClock),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieMobilePkceMutableClock);

    public static readonly WebHostVariant ExternalCookieMobilePkceAuthRateLimitIp1 = new(
        nameof(ExternalCookieMobilePkceAuthRateLimitIp1),
        "Testing",
        AuthRateLimitSettings(ipPermitLimit: 1, accountPermitLimit: 10),
        HostServiceProfile.ExternalCookieMobilePkce);

    public static readonly WebHostVariant ExternalCookieMobilePkceAuthRateLimitIp1Password = new(
        nameof(ExternalCookieMobilePkceAuthRateLimitIp1Password),
        "Testing",
        AuthRateLimitSettings(ipPermitLimit: 1, accountPermitLimit: 10),
        HostServiceProfile.ExternalCookieMobilePkce);

    public static readonly WebHostVariant ExternalCookieMobilePkceAuthRateLimitAccount1 = new(
        nameof(ExternalCookieMobilePkceAuthRateLimitAccount1),
        "Testing",
        AuthRateLimitSettings(ipPermitLimit: 30, accountPermitLimit: 1),
        HostServiceProfile.ExternalCookieMobilePkce);

    public static readonly WebHostVariant TestingMutationRateLimitAnonymous1 = new(
        nameof(TestingMutationRateLimitAnonymous1),
        "Testing",
        MutationRateLimitSettings(anonymousLimit: 1, authenticatedLimit: 20, authenticatedIpLimit: 120),
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingMutationRateLimitMember1Ip10 = new(
        nameof(TestingMutationRateLimitMember1Ip10),
        "Testing",
        MutationRateLimitSettings(anonymousLimit: 20, authenticatedLimit: 1, authenticatedIpLimit: 10),
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingMutationRateLimitMember10Ip1 = new(
        nameof(TestingMutationRateLimitMember10Ip1),
        "Testing",
        MutationRateLimitSettings(anonymousLimit: 20, authenticatedLimit: 10, authenticatedIpLimit: 1),
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingRecordingPushDispatch = new(
        nameof(TestingRecordingPushDispatch),
        "Testing",
        NoSettings,
        HostServiceProfile.RecordingPushDispatch);

    public static readonly WebHostVariant TestingRecordingPushDispatchFakeWatch = new(
        nameof(TestingRecordingPushDispatchFakeWatch),
        "Testing",
        NoSettings,
        HostServiceProfile.RecordingPushDispatchFakeWatch);

    public static readonly WebHostVariant TestingRecordingPushDispatchAlwaysWatch = new(
        nameof(TestingRecordingPushDispatchAlwaysWatch),
        "Testing",
        NoSettings,
        HostServiceProfile.RecordingPushDispatchAlwaysWatch);

    public static readonly WebHostVariant TestingThrowingNotificationDispatcher = new(
        nameof(TestingThrowingNotificationDispatcher),
        "Testing",
        NoSettings,
        HostServiceProfile.ThrowingNotificationDispatcher);

    public static readonly WebHostVariant TestingSeedableMemberPageActivity = new(
        nameof(TestingSeedableMemberPageActivity),
        "Testing",
        NoSettings,
        HostServiceProfile.SeedableMemberPageActivity);

    internal const string AnalyticsMeasurementId = "G-V2W56BZ3KZ";

    private static ImmutableSortedDictionary<string, string?> AuthRateLimitSettings(
        int ipPermitLimit,
        int accountPermitLimit) =>
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{AuthRateLimitingOptions.SectionName}:IpPermitLimit",
                ipPermitLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{AuthRateLimitingOptions.SectionName}:IpWindowMinutes",
                "60"),
            KeyValuePair.Create<string, string?>(
                $"{AuthRateLimitingOptions.SectionName}:AccountPermitLimit",
                accountPermitLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{AuthRateLimitingOptions.SectionName}:AccountWindowMinutes",
                "60"),
        ]);

    private static ImmutableSortedDictionary<string, string?> MutationRateLimitSettings(
        int anonymousLimit,
        int authenticatedLimit,
        int authenticatedIpLimit) =>
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AnonymousPermitLimit",
                anonymousLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AnonymousWindowMinutes",
                "60"),
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AuthenticatedMemberPermitLimit",
                authenticatedLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AuthenticatedMemberWindowMinutes",
                "60"),
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AuthenticatedIpPermitLimit",
                authenticatedIpLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{MutationRateLimitingOptions.SectionName}:AuthenticatedIpWindowMinutes",
                "60"),
        ]);

    private static void AddLockedForumTopic1002(IServiceCollection services)
    {
        services.RemoveAll<IForumWriteRepository>();
        services.AddSingleton<IForumWriteRepository>(new LockedForumWriteRepository());
    }

    private static void AddLegacyForumAttachmentMemoryBlobs(IServiceCollection services)
    {
        services.RemoveAll<IBlobUploadService>();
        services.AddSingleton<IBlobUploadService>(MemoryBlobUploadService.WithLegacyForumBlobs());
    }

    private static void AddLegacyForumAttachmentMissingBlob(IServiceCollection services)
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "not-in-storage.bin", 10));
        services.RemoveAll<IForumAttachmentRepository>();
        services.AddSingleton<IForumAttachmentRepository>(repo);
    }

    private static void AddModernForumAttachmentDownload(IServiceCollection services, HostServiceContext context)
    {
        context.FixedForumAttachment ??= new FixedIdAttachmentRepository();
        services.RemoveAll<IForumAttachmentRepository>();
        services.AddSingleton<IForumAttachmentRepository>(context.FixedForumAttachment);
        services.RemoveAll<IBlobUploadService>();
        services.AddSingleton<IBlobUploadService>(MemoryBlobUploadService.WithModernForumAttachment());
    }

    private static void AddForumAttachmentMemoryBlob(IServiceCollection services)
    {
        services.RemoveAll<IBlobUploadService>();
        services.AddSingleton<IBlobUploadService, MemoryBlobUploadService>();
    }

    private static void AddSiteSearchTimeout(IServiceCollection services)
    {
        services.RemoveAll<ISiteSearchService>();
        services.AddSingleton<ISiteSearchService>(new TimeoutSiteSearchService());
    }

    private static void AddThrowingSearchIndex(IServiceCollection services)
    {
        services.RemoveAll<ISearchIndexService>();
        services.AddSingleton<ISearchIndexService>(new ThrowingSearchIndexService());
    }

    private static void AddWarmupThrowingNewsLatest(IServiceCollection services)
    {
        services.RemoveAll<INewsRepository>();
        services.AddSingleton<INewsRepository>(new WarmupThrowingNewsRepository());
    }

    private static void AddThrowingSprintBoardQuiz(IServiceCollection services)
    {
        services.RemoveAll<IQuizRepository>();
        services.AddSingleton<IQuizRepository, ThrowingSprintBoardQuizRepository>();
    }

    private static void AddThrowingForumArchiveAuthor(IServiceCollection services)
    {
        services.RemoveAll<IForumArchiveAuthorRepository>();
        services.AddSingleton<IForumArchiveAuthorRepository>(new ThrowingForumArchiveAuthorRepository());
    }

    private static void AddExternalCookieMobilePkce(IServiceCollection services)
    {
        AddExternalCookie(services);
        foreach (var provider in MemberAuthenticationSchemes.ExternalProviders)
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestOAuthProviderHandler>(provider, _ => { });
        }
    }

    private static void AddExternalCookieMobilePkceMutableClock(IServiceCollection services, HostServiceContext context)
    {
        AddExternalCookieMobilePkce(services);
        AddMutableClock(services, context);
    }

    private static void AddRecordingPushDispatch(IServiceCollection services, HostServiceContext context)
    {
        context.PushTransport ??= new RecordingPushTransport();
        services.RemoveAll<IPushTransport>();
        services.AddSingleton<IPushTransport>(context.PushTransport);
    }

    private static void AddRecordingPushDispatchFakeWatch(IServiceCollection services, HostServiceContext context)
    {
        AddRecordingPushDispatch(services, context);
        context.TopicWatch ??= new FakeTopicWatchLookup();
        services.RemoveAll<ITopicWatchLookup>();
        services.AddSingleton<ITopicWatchLookup>(context.TopicWatch);
    }

    private static void AddRecordingPushDispatchAlwaysWatch(IServiceCollection services, HostServiceContext context)
    {
        AddRecordingPushDispatch(services, context);
        context.AlwaysWatch ??= new ConfigurableAlwaysWatchLookup();
        services.RemoveAll<ITopicWatchLookup>();
        services.AddSingleton<ITopicWatchLookup>(context.AlwaysWatch);
    }

    private static void AddThrowingNotificationDispatcher(IServiceCollection services)
    {
        services.RemoveAll<INotificationDispatcher>();
        services.AddSingleton<INotificationDispatcher>(
            new ThrowingNotificationDispatcher(new InvalidOperationException("dispatcher down")));
    }

    private static void AddSeedableMemberPageActivity(IServiceCollection services, HostServiceContext context)
    {
        context.MemberPageActivity ??= new SeedableMemberPageActivityRepository();
        services.RemoveAll<IMemberPublicActivityRepository>();
        services.AddSingleton<IMemberPublicActivityRepository>(context.MemberPageActivity);
    }
}

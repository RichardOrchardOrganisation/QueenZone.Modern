using System.Collections.Concurrent;
using System.Collections.Immutable;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Declared host variants for Web.Tests. Every configuration a class fixture may cache
/// is a <see cref="static"/> field here — tests do not pass service lambdas as keys.
/// </summary>
public static partial class WebHostVariants
{
    private static readonly ImmutableSortedDictionary<string, string?> NoSettings =
        ImmutableSortedDictionary<string, string?>.Empty;

    public static readonly WebHostVariant Testing = new(
        nameof(Testing),
        "Testing",
        NoSettings,
        HostServiceProfile.None);

    public static readonly WebHostVariant ExternalCookie = new(
        nameof(ExternalCookie),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookie);

    public static readonly WebHostVariant PreviewPublicBaseUrl = new(
        nameof(PreviewPublicBaseUrl),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                "Site:PublicBaseUrl",
                PreviewPublicBaseUrlWebApplicationFactory.PublicBaseUrl),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant ExternalCookieInspectableBlob = new(
        nameof(ExternalCookieInspectableBlob),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieInspectableBlob);

    public static readonly WebHostVariant EmptyNews = new(
        nameof(EmptyNews),
        "Testing",
        NoSettings,
        HostServiceProfile.EmptyNews);

    public static readonly WebHostVariant MemberSubmittedNews = new(
        nameof(MemberSubmittedNews),
        "Testing",
        NoSettings,
        HostServiceProfile.MemberSubmittedNews);

    public static readonly WebHostVariant SourceLinkNews = new(
        nameof(SourceLinkNews),
        "Testing",
        NoSettings,
        HostServiceProfile.SourceLinkNews);

    public static readonly WebHostVariant UnsafeHtmlNews = new(
        nameof(UnsafeHtmlNews),
        "Testing",
        NoSettings,
        HostServiceProfile.UnsafeHtmlNews);

    public static readonly WebHostVariant DuplicateLegacyNews = new(
        nameof(DuplicateLegacyNews),
        "Testing",
        NoSettings,
        HostServiceProfile.DuplicateLegacyNews);

    public static readonly WebHostVariant DateOrderedNews = new(
        nameof(DateOrderedNews),
        "Testing",
        NoSettings,
        HostServiceProfile.DateOrderedNews);

    public static readonly WebHostVariant DeduplicatedPagingNews = new(
        nameof(DeduplicatedPagingNews),
        "Testing",
        NoSettings,
        HostServiceProfile.DeduplicatedPagingNews);

    public static readonly WebHostVariant UgcThumbnailNews = new(
        nameof(UgcThumbnailNews),
        "Testing",
        NoSettings,
        HostServiceProfile.UgcThumbnailNews);

    public static readonly WebHostVariant DetailImageNews = new(
        nameof(DetailImageNews),
        "Testing",
        NoSettings,
        HostServiceProfile.DetailImageNews);

    public static readonly WebHostVariant ExternalCookieThrowingSearchIndex = new(
        nameof(ExternalCookieThrowingSearchIndex),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieThrowingSearchIndex);

    public static readonly WebHostVariant TrackingPromotedNews = new(
        nameof(TrackingPromotedNews),
        "Testing",
        NoSettings,
        HostServiceProfile.TrackingPromotedNews);

    public static readonly WebHostVariant TestingCountingArticles = new(
        nameof(TestingCountingArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.CountingArticles);

    public static readonly WebHostVariant ProductionWithoutMobileAuthSigningKey = new(
        nameof(ProductionWithoutMobileAuthSigningKey),
        "Production",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("MobileAuth:SigningKey", string.Empty),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant IsolatedQuizzes = new(
        nameof(IsolatedQuizzes),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedQuizzes);

    public static readonly WebHostVariant IsolatedQuizzesWithClock = new(
        nameof(IsolatedQuizzesWithClock),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedQuizzesWithClock);

    public static readonly WebHostVariant IsolatedHomePolls = new(
        nameof(IsolatedHomePolls),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedHomePolls);

    public static readonly WebHostVariant IsolatedTrivia = new(
        nameof(IsolatedTrivia),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedTrivia);

    public static readonly WebHostVariant ThrowOnReadBlob = new(
        nameof(ThrowOnReadBlob),
        "Testing",
        NoSettings,
        HostServiceProfile.ThrowOnReadBlob);

    public static readonly WebHostVariant EmptyQuotes = new(
        nameof(EmptyQuotes),
        "Testing",
        NoSettings,
        HostServiceProfile.EmptyQuotes);

    public static readonly WebHostVariant SequentialTrivia = new(
        nameof(SequentialTrivia),
        "Testing",
        NoSettings,
        HostServiceProfile.SequentialTrivia);

    public static readonly WebHostVariant FixedUtc20260713 = new(
        nameof(FixedUtc20260713),
        "Testing",
        NoSettings,
        HostServiceProfile.FixedUtc20260713);

    public static readonly WebHostVariant FixedUtc20260712 = new(
        nameof(FixedUtc20260712),
        "Testing",
        NoSettings,
        HostServiceProfile.FixedUtc20260712);

    public static readonly WebHostVariant FixedUtc20260827 = new(
        nameof(FixedUtc20260827),
        "Testing",
        NoSettings,
        HostServiceProfile.FixedUtc20260827);

    public static readonly WebHostVariant TimelineDeepOffPage = new(
        nameof(TimelineDeepOffPage),
        "Testing",
        NoSettings,
        HostServiceProfile.TimelineDeepOffPage);

    public static readonly WebHostVariant UnpublishedTimelineEvent = new(
        nameof(UnpublishedTimelineEvent),
        "Testing",
        NoSettings,
        HostServiceProfile.UnpublishedTimelineEvent);

    public static readonly WebHostVariant NewsDecadeFilter2000s = new(
        nameof(NewsDecadeFilter2000s),
        "Testing",
        NoSettings,
        HostServiceProfile.NewsDecadeFilter2000s);

    public static readonly WebHostVariant NewsOnly2026 = new(
        nameof(NewsOnly2026),
        "Testing",
        NoSettings,
        HostServiceProfile.NewsOnly2026);

    public static readonly WebHostVariant NewsYearBeatsDecade = new(
        nameof(NewsYearBeatsDecade),
        "Testing",
        NoSettings,
        HostServiceProfile.NewsYearBeatsDecade);

    public static readonly WebHostVariant NewsYears2006To2026 = new(
        nameof(NewsYears2006To2026),
        "Testing",
        NoSettings,
        HostServiceProfile.NewsYears2006To2026);

    public static readonly WebHostVariant IsolatedNewsDiscussion = new(
        nameof(IsolatedNewsDiscussion),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedNewsDiscussion);

    public static readonly WebHostVariant OfficialQueenOnlineLinks = new(
        nameof(OfficialQueenOnlineLinks),
        "Testing",
        NoSettings,
        HostServiceProfile.OfficialQueenOnlineLinks);

    public static readonly WebHostVariant HiddenUnavailableLinks = new(
        nameof(HiddenUnavailableLinks),
        "Testing",
        NoSettings,
        HostServiceProfile.HiddenUnavailableLinks);

    public static readonly WebHostVariant DeadOnlyLinks = new(
        nameof(DeadOnlyLinks),
        "Testing",
        NoSettings,
        HostServiceProfile.DeadOnlyLinks);

    public static readonly WebHostVariant BareLegacyUrlLinks = new(
        nameof(BareLegacyUrlLinks),
        "Testing",
        NoSettings,
        HostServiceProfile.BareLegacyUrlLinks);

    public static readonly WebHostVariant MalformedMailtoLinks = new(
        nameof(MalformedMailtoLinks),
        "Testing",
        NoSettings,
        HostServiceProfile.MalformedMailtoLinks);

    public static readonly WebHostVariant PreviewPublicBaseUrlMutableCommunityArticles = new(
        nameof(PreviewPublicBaseUrlMutableCommunityArticles),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                "Site:PublicBaseUrl",
                PreviewPublicBaseUrlWebApplicationFactory.PublicBaseUrl),
        ]),
        HostServiceProfile.MutableCommunityArticles);

    public static readonly WebHostVariant EmptyArticles = new(
        nameof(EmptyArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.EmptyArticles);

    public static readonly WebHostVariant OverlayImageArticle = new(
        nameof(OverlayImageArticle),
        "Testing",
        NoSettings,
        HostServiceProfile.OverlayImageArticle);

    public static readonly WebHostVariant SourceLinkArticles = new(
        nameof(SourceLinkArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.SourceLinkArticles);

    public static readonly WebHostVariant UnsafeHtmlArticle = new(
        nameof(UnsafeHtmlArticle),
        "Testing",
        NoSettings,
        HostServiceProfile.UnsafeHtmlArticle);

    public static readonly WebHostVariant DateOrderedArticles = new(
        nameof(DateOrderedArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.DateOrderedArticles);

    public static readonly WebHostVariant HtmlSummaryBiography = new(
        nameof(HtmlSummaryBiography),
        "Testing",
        NoSettings,
        HostServiceProfile.HtmlSummaryBiography);

    public static readonly WebHostVariant CountingBiography = new(
        nameof(CountingBiography),
        "Testing",
        NoSettings,
        HostServiceProfile.CountingBiography);

    public static readonly WebHostVariant UnsafeHtmlBiography = new(
        nameof(UnsafeHtmlBiography),
        "Testing",
        NoSettings,
        HostServiceProfile.UnsafeHtmlBiography);

    public static readonly WebHostVariant EmptyBiography = new(
        nameof(EmptyBiography),
        "Testing",
        NoSettings,
        HostServiceProfile.EmptyBiography);

    public static readonly WebHostVariant PhotosWithoutFreddieCategory = new(
        nameof(PhotosWithoutFreddieCategory),
        "Testing",
        NoSettings,
        HostServiceProfile.PhotosWithoutFreddieCategory);

    public static readonly WebHostVariant EmptyFreddieTributes = new(
        nameof(EmptyFreddieTributes),
        "Testing",
        NoSettings,
        HostServiceProfile.EmptyFreddieTributes);

    public static readonly WebHostVariant IsolatedQuotes = new(
        nameof(IsolatedQuotes),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedQuotes);

    public static readonly WebHostVariant IsolatedFanPerformanceSubmissions = new(
        nameof(IsolatedFanPerformanceSubmissions),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedFanPerformanceSubmissions);

    public static readonly WebHostVariant IsolatedAdminNews = new(
        nameof(IsolatedAdminNews),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedAdminNews);

    public static readonly WebHostVariant IsolatedAdminNewsDiscovery = new(
        nameof(IsolatedAdminNewsDiscovery),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("OpenRouter:ApiKey", "test-key"),
        ]),
        HostServiceProfile.IsolatedAdminNewsDiscovery);

    public static readonly WebHostVariant IsolatedAdminBiography = new(
        nameof(IsolatedAdminBiography),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedAdminBiography);

    public static readonly WebHostVariant IsolatedAdminTimeline = new(
        nameof(IsolatedAdminTimeline),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedAdminTimeline);

    public static readonly WebHostVariant IsolatedAdminFreddieTributes = new(
        nameof(IsolatedAdminFreddieTributes),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedAdminFreddieTributes);

    public static readonly WebHostVariant IsolatedAdminGuidance = new(
        nameof(IsolatedAdminGuidance),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedAdminGuidance);

    public static readonly WebHostVariant IsolatedHomePollsThrowingPublish = new(
        nameof(IsolatedHomePollsThrowingPublish),
        "Testing",
        NoSettings,
        HostServiceProfile.IsolatedHomePollsThrowingPublish);

    public static readonly WebHostVariant GaTrafficAvailable = new(
        nameof(GaTrafficAvailable),
        "Testing",
        NoSettings,
        HostServiceProfile.GaTrafficAvailable);

    public static readonly WebHostVariant GaTrafficUnavailable = new(
        nameof(GaTrafficUnavailable),
        "Testing",
        NoSettings,
        HostServiceProfile.GaTrafficUnavailable);

    public static readonly WebHostVariant ExternalCookieTimeoutHideForum = new(
        nameof(ExternalCookieTimeoutHideForum),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieTimeoutHideForum);

    public static readonly WebHostVariant ExternalCookieThrowingRevokeMobileAuth = new(
        nameof(ExternalCookieThrowingRevokeMobileAuth),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieThrowingRevokeMobileAuth);

    private static readonly ImmutableSortedDictionary<string, string?> AppleOAuthSettings =
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("Authentication:Apple:ClientId", "org.queenzone.web"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:TeamId", "TEAM123456"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:KeyId", "KEY1234567"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:PrivateKey", "test-private-key"),
        ]);

    private static readonly ImmutableSortedDictionary<string, string?> AllMobileOAuthSettings =
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("Authentication:Google:ClientId", "google-test-client"),
            KeyValuePair.Create<string, string?>("Authentication:Google:ClientSecret", "google-test-secret"),
            KeyValuePair.Create<string, string?>("Authentication:Microsoft:ClientId", "ms-test-client"),
            KeyValuePair.Create<string, string?>("Authentication:Microsoft:ClientSecret", "ms-test-secret"),
            KeyValuePair.Create<string, string?>("Authentication:Discord:ClientId", "discord-test-client"),
            KeyValuePair.Create<string, string?>("Authentication:Discord:ClientSecret", "discord-test-secret"),
            KeyValuePair.Create<string, string?>("Authentication:GitHub:ClientId", "github-test-client"),
            KeyValuePair.Create<string, string?>("Authentication:GitHub:ClientSecret", "github-test-secret"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:ClientId", "apple-test-client"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:TeamId", "TEAMID"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:KeyId", "KEYID"),
            KeyValuePair.Create<string, string?>("Authentication:Apple:PrivateKey", "test-apple-private-key"),
        ]);

    public static readonly WebHostVariant TestingAppleOAuth = new(
        nameof(TestingAppleOAuth),
        "Testing",
        AppleOAuthSettings,
        HostServiceProfile.AppleOAuth);

    public static readonly WebHostVariant TestingAllMobileOAuthProviders = new(
        nameof(TestingAllMobileOAuthProviders),
        "Testing",
        AllMobileOAuthSettings,
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingLegacyClaimableEmail = new(
        nameof(TestingLegacyClaimableEmail),
        "Testing",
        NoSettings,
        HostServiceProfile.MutableLegacyLookup);

    public static readonly WebHostVariant PhotoUploadQuota1 = new(
        nameof(PhotoUploadQuota1),
        "Testing",
        NoSettings,
        HostServiceProfile.PhotoUploadQuota1);

    public static readonly WebHostVariant PhotoUploadQuota0 = new(
        nameof(PhotoUploadQuota0),
        "Testing",
        NoSettings,
        HostServiceProfile.PhotoUploadQuota0);

    public static readonly WebHostVariant ExternalCookieHelpMemberRateLimit1 = new(
        nameof(ExternalCookieHelpMemberRateLimit1),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("HelpRequests:MaxPerMemberPerMinute", "1"),
            KeyValuePair.Create<string, string?>("HelpRequests:MaxAnonymousPerIpPerHour", "10"),
        ]),
        HostServiceProfile.ExternalCookie);

    public static readonly WebHostVariant TestingHelpAnonymousRateLimit1 = new(
        nameof(TestingHelpAnonymousRateLimit1),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("HelpRequests:MaxAnonymousPerIpPerHour", "1"),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingHelpMemberRateLimit1 = new(
        nameof(TestingHelpMemberRateLimit1),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>("HelpRequests:MaxPerMemberPerMinute", "1"),
            KeyValuePair.Create<string, string?>("HelpRequests:MaxAnonymousPerIpPerHour", "10"),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant TestingPrivateMessageRateLimit1 = new(
        nameof(TestingPrivateMessageRateLimit1),
        "Testing",
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{PrivateMessageRateLimitOptions.SectionName}:MaxMessagesPerWindow",
                "1"),
            KeyValuePair.Create<string, string?>(
                $"{PrivateMessageRateLimitOptions.SectionName}:NewAccountMaxMessagesPerWindow",
                "1"),
        ]),
        HostServiceProfile.None);

    public static readonly WebHostVariant ExternalCookieFanPerformanceAudioLimit1 = new(
        nameof(ExternalCookieFanPerformanceAudioLimit1),
        "Testing",
        FanPerformanceRateLimitSettings(audioPermitLimit: 1, browsePermitLimit: 60),
        HostServiceProfile.ExternalCookie);

    public static readonly WebHostVariant ExternalCookieFanPerformanceBrowseLimit1 = new(
        nameof(ExternalCookieFanPerformanceBrowseLimit1),
        "Testing",
        FanPerformanceRateLimitSettings(audioPermitLimit: 10, browsePermitLimit: 1),
        HostServiceProfile.ExternalCookie);

    public static readonly WebHostVariant ExternalCookieFanPerformanceBrowseLimit5 = new(
        nameof(ExternalCookieFanPerformanceBrowseLimit5),
        "Testing",
        FanPerformanceRateLimitSettings(audioPermitLimit: 10, browsePermitLimit: 5),
        HostServiceProfile.ExternalCookie);

    public static readonly WebHostVariant ExternalCookieFanPerformanceCatalog25 = new(
        nameof(ExternalCookieFanPerformanceCatalog25),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieFanPerformanceCatalog25);

    public static readonly WebHostVariant ExternalCookieFailingNewsSuggestionPromoteCreate = new(
        nameof(ExternalCookieFailingNewsSuggestionPromoteCreate),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieFailingNewsSuggestionPromoteCreate);

    public static readonly WebHostVariant ExternalCookieNewsSuggestionPromoteReturnsNull = new(
        nameof(ExternalCookieNewsSuggestionPromoteReturnsNull),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieNewsSuggestionPromoteReturnsNull);

    public static readonly WebHostVariant ExternalCookieNewsSuggestionPromoteConcurrency = new(
        nameof(ExternalCookieNewsSuggestionPromoteConcurrency),
        "Testing",
        NoSettings,
        HostServiceProfile.ExternalCookieNewsSuggestionPromoteConcurrency);

    public static readonly WebHostVariant TestingStaleFanPerformanceSubmissions = new(
        nameof(TestingStaleFanPerformanceSubmissions),
        "Testing",
        NoSettings,
        HostServiceProfile.StaleFanPerformanceSubmissions);

    public static readonly WebHostVariant TestingStubEditorBlob = new(
        nameof(TestingStubEditorBlob),
        "Testing",
        NoSettings,
        HostServiceProfile.StubEditorBlob);

    public static readonly WebHostVariant TestingRecordingMemberActivity = new(
        nameof(TestingRecordingMemberActivity),
        "Testing",
        NoSettings,
        HostServiceProfile.RecordingMemberActivity);

    public static readonly WebHostVariant TestingMutableCommunityArticles = new(
        nameof(TestingMutableCommunityArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.MutableCommunityArticles);

    public static readonly WebHostVariant TestingSqlFailingCommunityArticles = new(
        nameof(TestingSqlFailingCommunityArticles),
        "Testing",
        NoSettings,
        HostServiceProfile.SqlFailingCommunityArticles);

    internal const string LegacyClaimableEmail = "legacy-me@example.com";

    private static ImmutableSortedDictionary<string, string?> FanPerformanceRateLimitSettings(
        int audioPermitLimit,
        int browsePermitLimit) =>
        ImmutableSortedDictionary.CreateRange(
        [
            KeyValuePair.Create<string, string?>(
                $"{FanPerformanceRateLimitingOptions.SectionName}:AudioPermitLimit",
                audioPermitLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{FanPerformanceRateLimitingOptions.SectionName}:AudioSlidingWindowSeconds",
                "3600"),
            KeyValuePair.Create<string, string?>(
                $"{FanPerformanceRateLimitingOptions.SectionName}:BrowsePermitLimit",
                browsePermitLimit.ToString()),
            KeyValuePair.Create<string, string?>(
                $"{FanPerformanceRateLimitingOptions.SectionName}:BrowseWindowSeconds",
                "3600"),
        ]);

    internal static readonly Guid MemberSubmittedNewsSubmitterId =
        Guid.Parse("6c8f2d11-4a7b-4e90-9c3a-1f5d8b2e7a44");

    internal static void Apply(
        HostServiceProfile profile,
        IServiceCollection services,
        HostServiceContext? context)
    {
        switch (profile)
        {
            case HostServiceProfile.None:
                break;
            case HostServiceProfile.ExternalCookie:
                AddExternalCookie(services);
                break;
            case HostServiceProfile.ExternalCookieInspectableBlob:
                AddExternalCookie(services);
                AddInspectableBlob(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.EmptyNews:
                ReplaceNews(services, []);
                break;
            case HostServiceProfile.MemberSubmittedNews:
                ReplaceNews(services, MemberSubmittedNewsItems());
                break;
            case HostServiceProfile.SourceLinkNews:
                ReplaceNews(services, SourceLinkNewsItems());
                break;
            case HostServiceProfile.UnsafeHtmlNews:
                ReplaceNews(services, UnsafeHtmlNewsItems());
                break;
            case HostServiceProfile.DuplicateLegacyNews:
                ReplaceNews(services, DuplicateLegacyNewsItems());
                break;
            case HostServiceProfile.DateOrderedNews:
                ReplaceNews(services, DateOrderedNewsItems());
                break;
            case HostServiceProfile.DeduplicatedPagingNews:
                ReplaceNews(services, DeduplicatedPagingNewsItems());
                break;
            case HostServiceProfile.UgcThumbnailNews:
                ReplaceNews(services, UgcThumbnailNewsItems());
                break;
            case HostServiceProfile.DetailImageNews:
                ReplaceNews(services, DetailImageNewsItems());
                break;
            case HostServiceProfile.ExternalCookieThrowingSearchIndex:
                AddExternalCookie(services);
                services.RemoveAll<ISearchIndexService>();
                services.AddSingleton<ISearchIndexService>(new ThrowingSearchIndexService());
                break;
            case HostServiceProfile.TrackingPromotedNews:
                var hostContext = RequireContext(context, profile);
                AddExternalCookie(services);
                AddInspectableBlob(services, hostContext);
                var tracking = new TrackingNewsRepository(new FixedNewsRepository(TrackingPromotedNewsItems()));
                hostContext.TrackingNews = tracking;
                services.RemoveAll<INewsRepository>();
                services.AddSingleton<INewsRepository>(tracking);
                break;
            case HostServiceProfile.CountingArticles:
                var countingContext = RequireContext(context, profile);
                countingContext.CountingArticles ??= new CountingArticlesRepository();
                services.RemoveAll<IArticlesRepository>();
                services.AddSingleton<IArticlesRepository>(countingContext.CountingArticles);
                break;
            case HostServiceProfile.IsolatedQuizzes:
                AddIsolatedQuizzes(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedQuizzesWithClock:
                AddIsolatedQuizzes(services, RequireContext(context, profile));
                AddMutableClock(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedHomePolls:
                AddIsolatedHomePolls(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedTrivia:
                AddIsolatedTrivia(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.ThrowOnReadBlob:
                services.RemoveAll<IBlobUploadService>();
                services.AddSingleton<IBlobUploadService, ThrowOnReadBlobService>();
                break;
            case HostServiceProfile.EmptyQuotes:
                services.RemoveAll<IQuoteRepository>();
                services.AddSingleton<IQuoteRepository>(new InMemoryQuoteRepository([]));
                break;
            case HostServiceProfile.SequentialTrivia:
                AddSequentialTrivia(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.FixedUtc20260713:
                AddFixedClock(services, new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
                break;
            case HostServiceProfile.FixedUtc20260712:
                AddFixedClock(services, new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
                break;
            case HostServiceProfile.FixedUtc20260827:
                AddFixedClock(services, new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
                break;
            case HostServiceProfile.TimelineDeepOffPage:
                ReplaceHistory(services, TimelineDeepOffPageItems());
                break;
            case HostServiceProfile.UnpublishedTimelineEvent:
                ReplaceHistory(services, [TimelineEvent(13, "Draft event", new DateTime(1975, 10, 31, 0, 0, 0, DateTimeKind.Utc), isPublished: false)]);
                break;
            case HostServiceProfile.NewsDecadeFilter2000s:
                ReplaceNews(services, NewsDecadeFilter2000sItems());
                break;
            case HostServiceProfile.NewsOnly2026:
                ReplaceNews(services, [
                    new NewsItem(1, "Only 2026 article", "Ex", "Body", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true),
                ]);
                break;
            case HostServiceProfile.NewsYearBeatsDecade:
                ReplaceNews(services, [
                    new NewsItem(1, "2008 article", "Ex", "Body", new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true),
                    new NewsItem(2, "2015 article", "Ex", "Body", new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true),
                ]);
                break;
            case HostServiceProfile.NewsYears2006To2026:
                ReplaceNews(services, [
                    new NewsItem(1, "Oldest", "Ex", "Body", new DateTime(2006, 5, 1, 0, 0, 0, DateTimeKind.Utc), null, true),
                    new NewsItem(2, "Newest", "Ex", "Body", new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc), null, true),
                    new NewsItem(3, "Hidden", "Ex", "Body", new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, false),
                ]);
                break;
            case HostServiceProfile.IsolatedNewsDiscussion:
                AddIsolatedNewsDiscussion(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.OfficialQueenOnlineLinks:
                ReplaceLinks(services, OfficialQueenOnlineLinkCategories());
                break;
            case HostServiceProfile.HiddenUnavailableLinks:
                ReplaceLinks(services, HiddenUnavailableLinkCategories(), HiddenUnavailableLinkChecks());
                break;
            case HostServiceProfile.DeadOnlyLinks:
                ReplaceLinks(services, DeadOnlyLinkCategories(), DeadOnlyLinkChecks());
                break;
            case HostServiceProfile.BareLegacyUrlLinks:
                ReplaceLinks(services, BareLegacyUrlLinkCategories());
                break;
            case HostServiceProfile.MalformedMailtoLinks:
                ReplaceLinks(services, MalformedMailtoLinkCategories());
                break;
            case HostServiceProfile.EmptyArticles:
                services.RemoveAll<IArticlesRepository>();
                services.AddSingleton<IArticlesRepository>(new QueenZone.Data.InMemoryArticlesRepository([]));
                break;
            case HostServiceProfile.OverlayImageArticle:
                AddOverlayImageArticle(services);
                break;
            case HostServiceProfile.SourceLinkArticles:
                services.RemoveAll<IArticlesRepository>();
                services.AddSingleton<IArticlesRepository>(new QueenZone.Data.InMemoryArticlesRepository(SourceLinkArticleItems()));
                break;
            case HostServiceProfile.UnsafeHtmlArticle:
                services.RemoveAll<IArticlesRepository>();
                services.AddSingleton<IArticlesRepository>(new QueenZone.Data.InMemoryArticlesRepository(UnsafeHtmlArticleItems()));
                break;
            case HostServiceProfile.DateOrderedArticles:
                services.RemoveAll<IArticlesRepository>();
                services.AddSingleton<IArticlesRepository>(new QueenZone.Data.InMemoryArticlesRepository(DateOrderedArticleItems()));
                break;
            case HostServiceProfile.HtmlSummaryBiography:
                ReplaceBiography(services, HtmlSummaryBiographyChapters());
                break;
            case HostServiceProfile.CountingBiography:
                AddCountingBiography(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.UnsafeHtmlBiography:
                ReplaceBiography(services, UnsafeHtmlBiographyChapters());
                break;
            case HostServiceProfile.EmptyBiography:
                ReplaceBiography(services, []);
                break;
            case HostServiceProfile.PhotosWithoutFreddieCategory:
                ReplacePhotos(services, PhotosWithoutFreddieCategorySeed());
                break;
            case HostServiceProfile.EmptyFreddieTributes:
                ReplaceFreddieTributes(services, []);
                break;
            case HostServiceProfile.IsolatedQuotes:
                AddIsolatedQuotes(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedFanPerformanceSubmissions:
                AddStaleFanPerformanceSubmissions(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminNews:
                AddIsolatedAdminNews(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminNewsDiscovery:
                AddIsolatedAdminNewsDiscovery(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminBiography:
                AddIsolatedAdminBiography(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminTimeline:
                AddIsolatedAdminTimeline(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminFreddieTributes:
                AddIsolatedAdminFreddieTributes(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedAdminGuidance:
                AddIsolatedAdminGuidance(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.IsolatedHomePollsThrowingPublish:
                AddIsolatedHomePollsThrowingPublish(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.GaTrafficAvailable:
                AddGoogleAnalyticsTraffic(services, GaTrafficAvailableSnapshot());
                break;
            case HostServiceProfile.GaTrafficUnavailable:
                AddGoogleAnalyticsTraffic(
                    services,
                    GoogleAnalyticsTrafficSnapshot.Unavailable("Google Analytics traffic is unavailable."));
                break;
            case HostServiceProfile.ExternalCookieTimeoutHideForum:
                AddExternalCookie(services);
                AddTimeoutHideForum(services);
                break;
            case HostServiceProfile.ExternalCookieThrowingRevokeMobileAuth:
                AddExternalCookie(services);
                AddThrowingRevokeMobileAuth(services);
                break;
            case HostServiceProfile.AppleOAuth:
                AddAppleOAuth(services);
                break;
            case HostServiceProfile.MutableLegacyLookup:
                AddMutableLegacyLookup(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.PhotoUploadQuota1:
                AddPhotoUploadQuota(services, RequireContext(context, profile), maxUploadsPerDay: 1);
                break;
            case HostServiceProfile.PhotoUploadQuota0:
                AddPhotoUploadQuota(services, RequireContext(context, profile), maxUploadsPerDay: 0);
                break;
            case HostServiceProfile.ExternalCookieFanPerformanceCatalog25:
                AddExternalCookie(services);
                ReplaceFanPerformances(services, FanPerformanceCatalog25Items());
                break;
            case HostServiceProfile.ExternalCookieFailingNewsSuggestionPromoteCreate:
                AddExternalCookie(services);
                AddFailingNewsSuggestionPromoteCreate(services);
                break;
            case HostServiceProfile.ExternalCookieNewsSuggestionPromoteReturnsNull:
                AddExternalCookie(services);
                AddConfigurableNewsSuggestion(
                    services,
                    RequireContext(context, profile),
                    (_, _, _, _, _) => Task.FromResult<NewsSuggestion?>(null));
                break;
            case HostServiceProfile.ExternalCookieNewsSuggestionPromoteConcurrency:
                AddExternalCookie(services);
                AddConfigurableNewsSuggestion(
                    services,
                    RequireContext(context, profile),
                    (_, _, _, _, _) => throw new OptimisticConcurrencyException());
                break;
            case HostServiceProfile.StaleFanPerformanceSubmissions:
                AddStaleFanPerformanceSubmissions(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.StubEditorBlob:
                AddStubEditorBlob(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.RecordingMemberActivity:
                AddRecordingMemberActivity(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.MutableCommunityArticles:
                AddMutableCommunityArticles(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.SqlFailingCommunityArticles:
                services.RemoveAll<IArticleRepository>();
                services.AddSingleton<IArticleRepository>(new SqlFailingCommunityArticleRepository());
                break;
            case HostServiceProfile.LockedForumTopic1002:
                AddLockedForumTopic1002(services);
                break;
            case HostServiceProfile.LegacyForumAttachmentMemoryBlobs:
                AddLegacyForumAttachmentMemoryBlobs(services);
                break;
            case HostServiceProfile.LegacyForumAttachmentMissingBlob:
                AddLegacyForumAttachmentMissingBlob(services);
                break;
            case HostServiceProfile.ModernForumAttachmentDownload:
                AddModernForumAttachmentDownload(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.ForumAttachmentMemoryBlob:
                AddForumAttachmentMemoryBlob(services);
                break;
            case HostServiceProfile.SiteSearchTimeout:
                AddSiteSearchTimeout(services);
                break;
            case HostServiceProfile.ThrowingSearchIndex:
                AddThrowingSearchIndex(services);
                break;
            case HostServiceProfile.WarmupThrowingNewsLatest:
                AddWarmupThrowingNewsLatest(services);
                break;
            case HostServiceProfile.ThrowingSprintBoardQuiz:
                AddThrowingSprintBoardQuiz(services);
                break;
            case HostServiceProfile.ExternalCookieMobilePkce:
                AddExternalCookieMobilePkce(services);
                break;
            case HostServiceProfile.ExternalCookieMobilePkceMutableClock:
                AddExternalCookieMobilePkceMutableClock(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.RecordingPushDispatch:
                AddRecordingPushDispatch(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.RecordingPushDispatchFakeWatch:
                AddRecordingPushDispatchFakeWatch(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.RecordingPushDispatchAlwaysWatch:
                AddRecordingPushDispatchAlwaysWatch(services, RequireContext(context, profile));
                break;
            case HostServiceProfile.ThrowingNotificationDispatcher:
                AddThrowingNotificationDispatcher(services);
                break;
            case HostServiceProfile.SeedableMemberPageActivity:
                AddSeedableMemberPageActivity(services, RequireContext(context, profile));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown host service profile.");
        }
    }

    private static HostServiceContext RequireContext(HostServiceContext? context, HostServiceProfile profile) =>
        context ?? throw new InvalidOperationException($"Host service profile '{profile}' requires a fixture context.");

    private static void AddExternalCookie(IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                MemberAuthenticationSchemes.ExternalCookie, _ => { });
    }

    private static void AddInspectableBlob(IServiceCollection services, HostServiceContext context)
    {
        services.RemoveAll<IBlobUploadService>();
        services.AddSingleton<IBlobUploadService>(_ =>
            new AzureBlobUploadService(context.BlobBackend, Options.Create(new BlobUploadOptions())));
        services.RemoveAll<ILegacyMemberLookupRepository>();
        services.AddSingleton<ILegacyMemberLookupRepository>(context.LegacyLookup);
    }

    private static void ReplaceNews(IServiceCollection services, IEnumerable<NewsItem> items)
    {
        services.RemoveAll<INewsRepository>();
        services.AddSingleton<INewsRepository>(new FixedNewsRepository(items));
    }

    private static void AddIsolatedQuizzes(IServiceCollection services, HostServiceContext context)
    {
        context.QuizStore ??= new SharedQuizStore();
        context.QuizQuestionSubmissions ??= new InMemoryQuizQuestionSubmissionRepository();
        services.RemoveAll<SharedQuizStore>();
        services.RemoveAll<IQuizRepository>();
        services.AddSingleton(context.QuizStore);
        services.AddSingleton<IQuizRepository>(_ => new InMemoryQuizRepository(context.QuizStore));
        services.RemoveAll<IQuizQuestionSubmissionRepository>();
        services.AddSingleton<IQuizQuestionSubmissionRepository>(context.QuizQuestionSubmissions);
    }

    private static void AddMutableClock(IServiceCollection services, HostServiceContext context)
    {
        context.Clock ??= new MutableTimeProvider();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(context.Clock);
    }

    private static void AddFixedClock(IServiceCollection services, DateTimeOffset utcNow)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
    }

    private static void AddIsolatedHomePolls(IServiceCollection services, HostServiceContext context)
    {
        context.HomePolls ??= new SharedHomePollStore();
        services.RemoveAll<SharedHomePollStore>();
        services.RemoveAll<IHomePollRepository>();
        services.AddSingleton(context.HomePolls);
        services.AddSingleton<IHomePollRepository>(_ => new InMemoryHomePollRepository(context.HomePolls));
    }

    private static void AddIsolatedTrivia(IServiceCollection services, HostServiceContext context)
    {
        context.Trivia ??= new SharedTriviaStore();
        services.RemoveAll<SharedTriviaStore>();
        services.RemoveAll<ITriviaRepository>();
        services.AddSingleton(context.Trivia);
        services.AddSingleton<ITriviaRepository>(_ => new InMemoryTriviaRepository(context.Trivia));
    }

    private static void AddSequentialTrivia(IServiceCollection services, HostServiceContext context)
    {
        context.SequentialTrivia ??= new SequentialTriviaRepository(
            new TriviaFactItem(41, "First published Queen trivia fact", DateTime.UtcNow, true, "Band", TriviaDifficulty.Easy, null),
            new TriviaFactItem(43, "Unpublished draft fact must never render", DateTime.UtcNow, false, "Band", TriviaDifficulty.Hard, "Draft"));
        services.RemoveAll<ITriviaRepository>();
        services.AddSingleton<ITriviaRepository>(context.SequentialTrivia);
    }

    private static void ReplaceHistory(IServiceCollection services, IReadOnlyList<QueenHistoryEvent> events)
    {
        services.RemoveAll<IQueenHistoryRepository>();
        services.AddSingleton<IQueenHistoryRepository>(new InMemoryQueenHistoryRepository(events));
    }

    private static void AddIsolatedNewsDiscussion(IServiceCollection services, HostServiceContext context)
    {
        context.SeedableNews ??= new SeedableNewsRepository();
        context.SeedableDiscussion ??= new SeedableDiscussionLookup();
        services.RemoveAll<INewsRepository>();
        services.AddSingleton<INewsRepository>(context.SeedableNews);
        services.RemoveAll<INewsForumDiscussionLookup>();
        services.AddSingleton<INewsForumDiscussionLookup>(context.SeedableDiscussion);
    }

    private static void ReplaceLinks(
        IServiceCollection services,
        IReadOnlyList<QueenLinkCategory> categories,
        IReadOnlyList<QueenLinkCheckUpdate>? checks = null)
    {
        var repository = new InMemoryLinksRepository(categories);
        if (checks is { Count: > 0 })
        {
            repository.UpsertCheckResultsAsync(checks).GetAwaiter().GetResult();
        }

        services.RemoveAll<ILinksRepository>();
        services.AddSingleton<ILinksRepository>(repository);
    }

    private static void AddOverlayImageArticle(IServiceCollection services)
    {
        var editorial = new InMemoryEditorialArticleRepository();
        var articles = new QueenZone.Data.InMemoryArticlesRepository(
            [
                new ArticleItem(
                    5004,
                    "Legacy archive title",
                    "Legacy excerpt.",
                    "<p>Legacy body.</p>",
                    new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc),
                    null,
                    "Features",
                    true),
            ],
            editorial);
        var draft = editorial.SaveDraftAsync(
            new EditorialArticleDraft(
                null,
                5004,
                null,
                "Overlay archive title",
                null,
                "Overlay excerpt.",
                "<p>Overlay body.</p>",
                "Overlay Author",
                "Features",
                "overlay,tags",
                null,
                "editors/admin/overlay.webp",
                DateTimeOffset.Parse("2026-05-04T09:00:00Z")),
            "admin").GetAwaiter().GetResult();
        editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin").GetAwaiter().GetResult();
        services.RemoveAll<IArticlesRepository>();
        services.AddSingleton<IArticlesRepository>(articles);
    }

    private static void ReplaceBiography(IServiceCollection services, IReadOnlyList<BiographyChapterItem> chapters)
    {
        services.RemoveAll<IBiographyRepository>();
        services.AddSingleton<IBiographyRepository>(new InMemoryBiographyRepository(chapters));
    }

    private static void AddCountingBiography(IServiceCollection services, HostServiceContext context)
    {
        context.CountingBiography ??= new CountingBiographyRepository(
        [
            new BiographyChapterItem(1, "First", "First summary", "First body", 1, DateTime.UtcNow),
            new BiographyChapterItem(2, "Second", "Second summary", "Second body", 2, DateTime.UtcNow),
        ]);
        services.RemoveAll<IBiographyRepository>();
        services.AddSingleton<IBiographyRepository>(context.CountingBiography);
    }

    private static void ReplacePhotos(IServiceCollection services, IReadOnlyList<PhotoCategorySeed> seed)
    {
        services.RemoveAll<SharedPhotoStore>();
        services.RemoveAll<IPhotoRepository>();
        services.AddSingleton(_ => new SharedPhotoStore(seed));
        services.AddSingleton<IPhotoRepository, InMemoryPhotoRepository>();
    }

    private static void ReplaceFreddieTributes(IServiceCollection services, IReadOnlyList<FreddieTribute> tributes)
    {
        services.RemoveAll<SharedFreddieTributeStore>();
        services.RemoveAll<IFreddieTributeRepository>();
        services.AddSingleton(_ => new SharedFreddieTributeStore(tributes));
        services.AddSingleton<IFreddieTributeRepository, InMemoryFreddieTributeRepository>();
    }

    private static void AddAppleOAuth(IServiceCollection services)
    {
        services.AddAuthentication().AddApple(MemberAuthenticationSchemes.Apple, options =>
        {
            options.ClientId = "org.queenzone.web";
            options.TeamId = "TEAM123456";
            options.KeyId = "KEY1234567";
            options.GenerateClientSecret = true;
            options.PrivateKey = (_, _) =>
                Task.FromResult<ReadOnlyMemory<char>>("test-private-key".AsMemory());
        });
    }

    private static void AddMutableLegacyLookup(IServiceCollection services, HostServiceContext context)
    {
        services.RemoveAll<ILegacyMemberLookupRepository>();
        services.AddSingleton<ILegacyMemberLookupRepository>(context.LegacyLookup);
    }

    private static void AddPhotoUploadQuota(IServiceCollection services, HostServiceContext context, int maxUploadsPerDay)
    {
        context.UploadQuota ??= new MemberUploadQuotaService(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new UploadQuotaOptions
            {
                Enabled = true,
                MaxUploadsPerDay = maxUploadsPerDay,
                MaxBytesPerDay = 100L * 1024 * 1024,
            }));
        services.RemoveAll<MemberUploadQuotaService>();
        services.AddSingleton(context.UploadQuota);
    }

    private static void ReplaceFanPerformances(IServiceCollection services, IReadOnlyList<FanPerformance> performances)
    {
        services.RemoveAll<IFanPerformanceRepository>();
        services.AddSingleton<IFanPerformanceRepository>(new InMemoryFanPerformanceRepository(performances));
    }

    private static void AddFailingNewsSuggestionPromoteCreate(IServiceCollection services)
    {
        var store = new SharedNewsStore();
        services.RemoveAll<SharedNewsStore>();
        services.RemoveAll<IAdminNewsRepository>();
        services.AddSingleton(store);
        services.AddSingleton<IAdminNewsRepository>(_ =>
            new FailingCreateAdminNewsRepository(
                new InMemoryAdminNewsRepository(store),
                new InvalidOperationException("Simulated suggestion promote create failure.")));
    }

    private static void AddConfigurableNewsSuggestion(
        IServiceCollection services,
        HostServiceContext context,
        Func<Guid, int, string, string?, CancellationToken, Task<NewsSuggestion?>> promoteHandler)
    {
        context.ConfigurableNewsSuggestions = new ConfigurableNewsSuggestionRepository(new InMemoryNewsSuggestionRepository())
        {
            PromoteHandler = promoteHandler,
        };
        services.RemoveAll<INewsSuggestionRepository>();
        services.AddSingleton<INewsSuggestionRepository>(context.ConfigurableNewsSuggestions);
    }

    private static void AddStaleFanPerformanceSubmissions(IServiceCollection services, HostServiceContext context)
    {
        // Assign the context fake during Apply so Get()/ResetAsync expose it before any
        // IFanPerformanceSubmissionRepository resolve. Isolated credit names still need
        // IMemberAccountRepository, which is only available after the host is built.
        context.FanPerformanceSubmissions ??= new InMemoryFanPerformanceSubmissionRepository(id =>
            context.FanPerformanceMembers?.FindByIdAsync(id).GetAwaiter().GetResult());
        services.RemoveAll<IFanPerformanceSubmissionRepository>();
        services.AddSingleton<IFanPerformanceSubmissionRepository>(sp =>
        {
            context.FanPerformanceMembers ??= sp.GetRequiredService<IMemberAccountRepository>();
            return context.FanPerformanceSubmissions;
        });
    }

    private static void AddStubEditorBlob(IServiceCollection services, HostServiceContext context)
    {
        context.EditorBlob ??= new EditorStubBlobUploadService();
        services.RemoveAll<IBlobUploadService>();
        services.AddSingleton<IBlobUploadService>(context.EditorBlob);
    }

    private static void AddRecordingMemberActivity(IServiceCollection services, HostServiceContext context)
    {
        context.MemberActivity ??= new RecordingMemberPublicActivityRepository();
        services.RemoveAll<IMemberPublicActivityRepository>();
        services.AddSingleton<IMemberPublicActivityRepository>(context.MemberActivity);
    }

    private static void AddMutableCommunityArticles(IServiceCollection services, HostServiceContext context)
    {
        context.CommunityArticles ??= new MutableCommunityArticleRepository();
        services.RemoveAll<IArticleRepository>();
        services.AddSingleton<IArticleRepository>(context.CommunityArticles);
    }

    private static FanPerformance[] FanPerformanceCatalog25Items() =>
        Enumerable.Range(1, 25)
            .Select(index => new FanPerformance(
                index,
                $"Track {index}",
                "Performer",
                "Cover",
                $"{index}.mp3",
                1024,
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)))
            .ToArray();

    private static NewsItem[] MemberSubmittedNewsItems() =>
    [
        new(
            5100,
            "Member submitted news",
            "Member-submitted excerpt.",
            "Member-submitted body.",
            new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc),
            null,
            true,
            SubmitterMemberId: MemberSubmittedNewsSubmitterId,
            SubmitterDisplayName: "News Contributor"),
    ];

    private static NewsItem[] SourceLinkNewsItems() =>
    [
        new(
            5001,
            "Article with source",
            "Excerpt with source.",
            "Published body.",
            new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            "https://example.com/original-story",
            true),
        new(
            5002,
            "Article with unsafe source",
            "Unsafe source excerpt.",
            "Published body.",
            new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc),
            "javascript:alert(1)",
            true),
    ];

    private static NewsItem[] UnsafeHtmlNewsItems() =>
    [
        new(
            5003,
            "Unsafe HTML article",
            "Unsafe excerpt.",
            "<script>alert('xss')</script><p>Safe <strong>legacy</strong> paragraph</p>",
            new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc),
            null,
            true),
    ];

    private static NewsItem[] DuplicateLegacyNewsItems() =>
    [
        new(
            4242,
            "Latest duplicate title",
            "Latest excerpt",
            "<p>Latest duplicate body</p>",
            new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc),
            null,
            true),
        new(
            4242,
            "Older duplicate title",
            "Older excerpt",
            "<p>Older duplicate body</p>",
            new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            null,
            true),
    ];

    private static NewsItem[] DateOrderedNewsItems() =>
    [
        new(
            3001,
            "Oldest article",
            "Oldest excerpt.",
            "Oldest body.",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true),
        new(
            3002,
            "Newest article",
            "Newest excerpt.",
            "Newest body.",
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true),
        new(
            3003,
            "Middle article",
            "Middle excerpt.",
            "Middle body.",
            new DateTime(2022, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            null,
            true),
    ];

    private static List<NewsItem> DeduplicatedPagingNewsItems()
    {
        var duplicateItems = Enumerable.Range(1, 25)
            .Select(id => new NewsItem(
                id,
                $"Published article {id}",
                $"Excerpt {id}",
                $"Body {id}",
                new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc),
                null,
                true))
            .ToList();

        duplicateItems.Add(new NewsItem(
            5,
            "Duplicate copy of article 5",
            "Older duplicate excerpt",
            "Older duplicate body",
            new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true));

        duplicateItems.Add(new NewsItem(
            99,
            "Hidden duplicate candidate",
            "Should not render",
            "Should not render",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            false));

        return duplicateItems;
    }

    private static NewsItem[] UgcThumbnailNewsItems() =>
    [
        new(
            6100,
            "Article with uploaded image",
            "Has a UGC thumbnail.",
            "Body",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ImageBlobKey: "editors/me/hero.webp"),
        new(
            6101,
            "Article without image",
            "Uses the placeholder.",
            "Body",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true),
        new(
            6102,
            "Article with gallery pick",
            "Falls back until the PIC row is resolved.",
            "Body",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ImageBlobKey: "gallery:3120",
            ImageGalleryPicId: 3120),
    ];

    private static NewsItem[] DetailImageNewsItems() =>
    [
        new(
            6200,
            "Detail with image",
            "Excerpt",
            "Body",
            new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ImageBlobKey: "editors/me/hero.webp"),
    ];

    internal static NewsItem[] TrackingPromotedNewsItems() =>
    [
        new(
            1002,
            "First promoted story",
            "First excerpt",
            "First body",
            new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc),
            null,
            true,
            "first-promoted-story"),
        new(
            1003,
            "Second promoted story",
            "Second excerpt",
            "Second body",
            new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc),
            null,
            true,
            "second-promoted-story"),
    ];

    private static List<NewsItem> NewsDecadeFilter2000sItems()
    {
        var items = new List<NewsItem>();
        for (var i = 0; i < 25; i++)
        {
            items.Add(new NewsItem(
                2000 + i,
                $"2020s article {i}",
                "Excerpt",
                "Body",
                new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                null,
                true));
        }

        items.Add(new NewsItem(
            9999,
            "Old article from the 2000s",
            "Excerpt",
            "Body",
            new DateTime(2008, 3, 4, 0, 0, 0, DateTimeKind.Utc),
            null,
            true));
        return items;
    }

    private static QueenHistoryEvent[] TimelineDeepOffPageItems() =>
    [
        TimelineEvent(1, "First page event", new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        TimelineEvent(9999, "Deep off-page event", new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc)),
    ];

    internal static QueenHistoryEvent TimelineEvent(
        int id,
        string title,
        DateTime eventDate,
        bool isPublished = true) =>
        new(
            id,
            title,
            "Queen play Live Aid.",
            eventDate,
            QueenHistoryDatePrecision.ExactDate,
            QueenHistoryEventCategory.Concert,
            100,
            QueenHistoryEventSourceType.Wikipedia,
            $"event-{id}",
            "https://en.wikipedia.org/wiki/Live_Aid",
            isPublished);

    private static QueenLinkCategory[] OfficialQueenOnlineLinkCategories() =>
    [
        new QueenLinkCategory(
            1,
            "Official",
            [
                new QueenLink(1, "Queen Online", "https://www.queenonline.com/", "Official Queen site.", 1, true),
            ]),
    ];

    private static QueenLinkCategory[] HiddenUnavailableLinkCategories() =>
    [
        new QueenLinkCategory(
            1,
            "Official",
            [
                new QueenLink(1, "Queen Online", "https://www.queenonline.com/", "Official Queen site.", 1, true),
                new QueenLink(2, "Missing Site", "https://missing.example.test/", "Gone.", 1, false),
            ]),
        new QueenLinkCategory(
            2,
            "Dead Category",
            [
                new QueenLink(3, "Dead Only", "https://dead.example.test/", "Gone.", 2, false),
            ]),
    ];

    private static QueenLinkCheckUpdate[] HiddenUnavailableLinkChecks() =>
    [
        new QueenLinkCheckUpdate(2, "https://missing.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
        new QueenLinkCheckUpdate(3, "https://dead.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
    ];

    private static QueenLinkCategory[] DeadOnlyLinkCategories() =>
    [
        new QueenLinkCategory(
            1,
            "Dead Category",
            [
                new QueenLink(1, "Dead Only", "https://dead.example.test/", "Gone.", 1, false),
            ]),
    ];

    private static QueenLinkCheckUpdate[] DeadOnlyLinkChecks() =>
    [
        new QueenLinkCheckUpdate(1, "https://dead.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
    ];

    private static QueenLinkCategory[] BareLegacyUrlLinkCategories() =>
    [
        new QueenLinkCategory(
            1,
            "Official",
            [
                new QueenLink(1, "Queen Online", "www.queenonline.com", "Official Queen site.", 1, true),
            ]),
    ];

    private static QueenLinkCategory[] MalformedMailtoLinkCategories() =>
    [
        new QueenLinkCategory(
            1,
            "Broken",
            [
                new QueenLink(1, "Malformed", "mailto:someone@example.test", "Not a public web link.", 1, false),
            ]),
    ];

    private static ArticleItem[] SourceLinkArticleItems() =>
    [
        new ArticleItem(
            5001,
            "Article with source link",
            "Excerpt with source.",
            "<p>Published body.</p>",
            new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            "https://example.com/original-story",
            "Features",
            true),
        new ArticleItem(
            5002,
            "Article with attribution",
            "Attribution excerpt.",
            "<p>Published body.</p>",
            new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc),
            "Queen Magazine",
            "Features",
            true),
    ];

    private static ArticleItem[] UnsafeHtmlArticleItems() =>
    [
        new ArticleItem(
            5003,
            "Unsafe HTML article",
            "Unsafe excerpt.",
            "<script>alert('xss')</script><p>Safe <strong>legacy</strong> paragraph</p>",
            new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc),
            null,
            null,
            true),
    ];

    private static ArticleItem[] DateOrderedArticleItems() =>
    [
        new ArticleItem(3001, "Oldest article", "Oldest excerpt.", "<p>Oldest body.</p>", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, null, true),
        new ArticleItem(3002, "Newest article", "Newest excerpt.", "<p>Newest body.</p>", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, null, true),
        new ArticleItem(3003, "Middle article", "Middle excerpt.", "<p>Middle body.</p>", new DateTime(2022, 3, 15, 0, 0, 0, DateTimeKind.Utc), null, null, true),
    ];

    private static BiographyChapterItem[] HtmlSummaryBiographyChapters() =>
    [
        new BiographyChapterItem(
            8001,
            "1946 - 1969",
            "<p>A founding chapter <strong>summary</strong> with HTML.</p>",
            "<p>Body text.</p>",
            1,
            new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
    ];

    private static BiographyChapterItem[] UnsafeHtmlBiographyChapters() =>
    [
        new BiographyChapterItem(
            7001,
            "2026",
            string.Empty,
            "<script>alert('xss')</script><p>Safe <strong>legacy</strong> paragraph</p>",
            1,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
    ];

    private static PhotoCategorySeed[] PhotosWithoutFreddieCategorySeed() =>
    [
        new PhotoCategorySeed(9, "Brian May",
        [
            new PhotoItemSeed(101, "Brian", "/Brian_May/img-101.jpg", "/Brian_May/img-101-t.jpg", new DateTime(1986, 7, 12)),
        ]),
    ];
}

/// <summary>Value-equal key for a cached web host. Service swaps are an enum, not a lambda.</summary>
public sealed record WebHostVariant(
    string Name,
    string Environment,
    ImmutableSortedDictionary<string, string?> Settings,
    HostServiceProfile Services);

public enum HostServiceProfile
{
    None = 0,
    ExternalCookie,
    ExternalCookieInspectableBlob,
    EmptyNews,
    MemberSubmittedNews,
    SourceLinkNews,
    UnsafeHtmlNews,
    DuplicateLegacyNews,
    DateOrderedNews,
    DeduplicatedPagingNews,
    UgcThumbnailNews,
    DetailImageNews,
    ExternalCookieThrowingSearchIndex,
    TrackingPromotedNews,
    CountingArticles,
    IsolatedQuizzes,
    IsolatedQuizzesWithClock,
    IsolatedHomePolls,
    IsolatedTrivia,
    ThrowOnReadBlob,
    EmptyQuotes,
    SequentialTrivia,
    FixedUtc20260713,
    FixedUtc20260712,
    FixedUtc20260827,
    TimelineDeepOffPage,
    UnpublishedTimelineEvent,
    NewsDecadeFilter2000s,
    NewsOnly2026,
    NewsYearBeatsDecade,
    NewsYears2006To2026,
    IsolatedNewsDiscussion,
    OfficialQueenOnlineLinks,
    HiddenUnavailableLinks,
    DeadOnlyLinks,
    BareLegacyUrlLinks,
    MalformedMailtoLinks,
    EmptyArticles,
    OverlayImageArticle,
    SourceLinkArticles,
    UnsafeHtmlArticle,
    DateOrderedArticles,
    HtmlSummaryBiography,
    CountingBiography,
    UnsafeHtmlBiography,
    EmptyBiography,
    PhotosWithoutFreddieCategory,
    EmptyFreddieTributes,
    IsolatedQuotes,
    IsolatedFanPerformanceSubmissions,
    IsolatedAdminNews,
    IsolatedAdminNewsDiscovery,
    IsolatedAdminBiography,
    IsolatedAdminTimeline,
    IsolatedAdminFreddieTributes,
    IsolatedAdminGuidance,
    IsolatedHomePollsThrowingPublish,
    GaTrafficAvailable,
    GaTrafficUnavailable,
    ExternalCookieTimeoutHideForum,
    ExternalCookieThrowingRevokeMobileAuth,
    AppleOAuth,
    MutableLegacyLookup,
    PhotoUploadQuota1,
    PhotoUploadQuota0,
    ExternalCookieFanPerformanceCatalog25,
    ExternalCookieFailingNewsSuggestionPromoteCreate,
    ExternalCookieNewsSuggestionPromoteReturnsNull,
    ExternalCookieNewsSuggestionPromoteConcurrency,
    StaleFanPerformanceSubmissions,
    StubEditorBlob,
    RecordingMemberActivity,
    MutableCommunityArticles,
    SqlFailingCommunityArticles,
    LockedForumTopic1002,
    LegacyForumAttachmentMemoryBlobs,
    LegacyForumAttachmentMissingBlob,
    ModernForumAttachmentDownload,
    ForumAttachmentMemoryBlob,
    SiteSearchTimeout,
    ThrowingSearchIndex,
    WarmupThrowingNewsLatest,
    ThrowingSprintBoardQuiz,
    ExternalCookieMobilePkce,
    ExternalCookieMobilePkceMutableClock,
    RecordingPushDispatch,
    RecordingPushDispatchFakeWatch,
    RecordingPushDispatchAlwaysWatch,
    ThrowingNotificationDispatcher,
    SeedableMemberPageActivity,
}

public interface IResettableHostFixture
{
    Task ResetAsync();
}

public static class TestIds
{
    public static string For(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"{name}-{Guid.NewGuid():N}";
    }
}

internal sealed class HostServiceContext
{
    public InMemoryBlobStorageBackend BlobBackend { get; } = new();

    public MutableLegacyMemberLookupRepository LegacyLookup { get; } = new();

    public TrackingNewsRepository? TrackingNews { get; set; }

    public CountingArticlesRepository? CountingArticles { get; set; }

    public SharedQuizStore? QuizStore { get; set; }

    public InMemoryQuizQuestionSubmissionRepository? QuizQuestionSubmissions { get; set; }

    public MemberUploadQuotaService? UploadQuota { get; set; }

    public ConfigurableNewsSuggestionRepository? ConfigurableNewsSuggestions { get; set; }

    public InMemoryFanPerformanceSubmissionRepository? FanPerformanceSubmissions { get; set; }

    public IMemberAccountRepository? FanPerformanceMembers { get; set; }

    public EditorStubBlobUploadService? EditorBlob { get; set; }

    public RecordingMemberPublicActivityRepository? MemberActivity { get; set; }

    public MutableCommunityArticleRepository? CommunityArticles { get; set; }

    public SharedHomePollStore? HomePolls { get; set; }

    public SharedTriviaStore? Trivia { get; set; }

    public SequentialTriviaRepository? SequentialTrivia { get; set; }

    public MutableTimeProvider? Clock { get; set; }

    public SeedableNewsRepository? SeedableNews { get; set; }

    public SeedableDiscussionLookup? SeedableDiscussion { get; set; }

    public CountingBiographyRepository? CountingBiography { get; set; }

    public SharedQuoteStore? Quotes { get; set; }

    public SharedNewsStore? AdminNews { get; set; }

    public SharedNewsDiscoveryStore? AdminDiscovery { get; set; }

    public AdminNewsMutationOverrides? AdminNewsMutations { get; set; }

    public ConfigurableNewsDiscoveryRepository? ConfigurableDiscovery { get; set; }

    public ConfigurableNewsAiClient? NewsAi { get; set; }

    public SharedNewsAgentRunRequestStore? NewsAgentRunRequests { get; set; }

    public SharedBiographyStore? AdminBiography { get; set; }

    public SharedQueenHistoryStore? AdminTimeline { get; set; }

    public SharedFreddieTributeStore? AdminFreddieTributes { get; set; }

    public SharedNewsAgentGuidanceStore? AdminGuidance { get; set; }

    public FixedIdAttachmentRepository? FixedForumAttachment { get; set; }

    public RecordingPushTransport? PushTransport { get; set; }

    public FakeTopicWatchLookup? TopicWatch { get; set; }

    public ConfigurableAlwaysWatchLookup? AlwaysWatch { get; set; }

    public SeedableMemberPageActivityRepository? MemberPageActivity { get; set; }

    public void Reset()
    {
        BlobBackend.Clear();
        LegacyLookup.Reset();
        TrackingNews?.Reset();
        CountingArticles?.Reset();
        QuizStore?.Clear();
        QuizQuestionSubmissions?.Clear();
        FanPerformanceSubmissions?.Clear();
        EditorBlob?.Reset();
        MemberActivity?.Reset();
        CommunityArticles?.Reset();
        HomePolls?.Clear();
        Trivia?.Clear();
        SequentialTrivia?.Reset();
        Clock?.Reset();
        SeedableNews?.Reset();
        SeedableDiscussion?.Reset();
        CountingBiography?.Reset();
        Quotes?.Clear();
        AdminNews?.Clear();
        AdminDiscovery?.Clear();
        AdminNewsMutations?.Reset();
        ConfigurableDiscovery?.ResetHandlers();
        NewsAi?.Reset();
        NewsAgentRunRequests?.Clear();
        AdminBiography?.Clear();
        AdminTimeline?.Clear();
        AdminFreddieTributes?.Reset();
        AdminGuidance?.Clear();
        FixedForumAttachment?.Reset();
        PushTransport?.Reset();
        TopicWatch?.Reset();
        AlwaysWatch?.Reset();
        MemberPageActivity?.Reset();
    }
}

internal sealed class MutableLegacyMemberLookupRepository : ILegacyMemberLookupRepository
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<LegacyMemberMatch>> matches =
        new(StringComparer.OrdinalIgnoreCase);

    public MutableLegacyMemberLookupRepository() => Reset();

    public void Seed(string email, IReadOnlyList<LegacyMemberMatch> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        matches[email] = items
            .OrderBy(match => match.Username, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.UserId)
            .ToList();
    }

    public void Reset()
    {
        matches.Clear();
        foreach (var pair in SampleLegacyMemberData.CreateSeedMatches())
        {
            matches[pair.Key] = [pair.Value];
        }
    }

    public Task<IReadOnlyList<LegacyMemberMatch>> FindAllByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (matches.TryGetValue(email, out var found))
        {
            return Task.FromResult(found);
        }

        return Task.FromResult<IReadOnlyList<LegacyMemberMatch>>([]);
    }

    public async Task<LegacyMemberMatch?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var found = await FindAllByEmailAsync(email, cancellationToken);
        return found.FirstOrDefault();
    }

    public Task<LegacyMemberMatch?> FindByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var match = matches.Values
            .SelectMany(static items => items)
            .FirstOrDefault(item => item.UserId == userId);
        return Task.FromResult(match);
    }
}

public sealed class CountingArticlesRepository : IArticlesRepository
{
    private readonly ArticleItem article = new(
        7801,
        "Cached archive article",
        "Output cache test article.",
        "<p>Output cache test body.</p>",
        new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
        null,
        "Testing",
        true);

    public int ArchivePageCallCount { get; private set; }

    public int PublishedCountCallCount { get; private set; }

    public void Reset()
    {
        ArchivePageCallCount = 0;
        PublishedCountCallCount = 0;
    }

    public Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArticleItem>>([article]);

    public Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArchivePageCallCount++;
        return Task.FromResult<IReadOnlyList<ArticleItem>>([article]);
    }

    public Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
    {
        PublishedCountCallCount++;
        return Task.FromResult(1);
    }

    public Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ArticleItem?>(id == article.Id ? article : null);

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SitemapContentEntry>>(
            [new SitemapContentEntry(article.Id, article.Title, article.PublishedAt)]);
}

internal sealed class TrackingNewsRepository(INewsRepository inner) : INewsRepository
{
    public int GetByIdCallCount { get; private set; }

    public int GetByIdsCallCount { get; private set; }

    public IReadOnlyList<int> LastRequestedIds { get; private set; } = [];

    public void Reset()
    {
        GetByIdCallCount = 0;
        GetByIdsCallCount = 0;
        LastRequestedIds = [];
    }

    public Task<IReadOnlyList<NewsItem>> GetLatestAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(count, cancellationToken);

    public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        inner.GetArchivePageAsync(page, pageSize, filter, cancellationToken);

    public Task<int> GetPublishedCountAsync(
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        inner.GetPublishedCountAsync(filter, cancellationToken);

    public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
        inner.GetArchiveYearRangeAsync(cancellationToken);

    public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        GetByIdCallCount++;
        return inner.GetByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        GetByIdsCallCount++;
        LastRequestedIds = ids.ToArray();
        return inner.GetByIdsAsync(ids, cancellationToken);
    }

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
        CancellationToken cancellationToken = default) =>
        inner.GetPublishedSitemapEntriesAsync(cancellationToken);

    public Task<NewsSearchPage> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.SearchAsync(query, page, pageSize, cancellationToken);
}

internal sealed class ThrowingSearchIndexService : ISearchIndexService
{
    public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated index failure.");

    public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated index failure.");

    public Task ReplaceContentTypeAsync(
        string contentType,
        IReadOnlyList<SearchDocumentEntity> documents,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated index failure.");

    public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated index failure.");
}

internal sealed class EditorStubBlobUploadService : IBlobUploadService
{
    public string? LastContainer { get; private set; }

    public int UploadCount { get; private set; }

    public void Reset()
    {
        LastContainer = null;
        UploadCount = 0;
    }

    public Task<BlobUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string containerName,
        BlobUploadContext? context = null,
        CancellationToken cancellationToken = default)
    {
        LastContainer = containerName;
        UploadCount++;
        var blobName = context?.PreferredBlobName ?? originalFileName;
        return Task.FromResult(new BlobUploadResult
        {
            Container = containerName,
            BlobName = blobName,
            ContentType = UgcProxyPaths.WebpContentType,
            SizeBytes = content.CanSeek ? content.Length : 8,
            PublicUrl = $"https://cdn.test/{containerName}/{blobName}",
        });
    }

    public Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<BlobContent?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<BlobContent?>(null);
}

internal sealed class RecordingMemberPublicActivityRepository : IMemberPublicActivityRepository
{
    private IReadOnlyList<MemberPublicActivityItem> items = [];

    public int FeedPageCalls { get; private set; }

    public int SinglePageCalls { get; private set; }

    public IReadOnlyList<Guid> LastFeedAuthorIds { get; private set; } = [];

    public void Seed(IReadOnlyList<MemberPublicActivityItem> activity)
    {
        items = activity;
        ResetCounters();
    }

    public void Reset()
    {
        items = [];
        ResetCounters();
    }

    private void ResetCounters()
    {
        FeedPageCalls = 0;
        SinglePageCalls = 0;
        LastFeedAuthorIds = [];
    }

    public Task<MemberPublicActivityPage> GetPageAsync(
        Guid memberId,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        SinglePageCalls++;
        throw new InvalidOperationException("Following feed must not N+1 GetPageAsync per follow.");
    }

    public Task<MemberPublicActivityPage> GetFeedPageAsync(
        IReadOnlyCollection<Guid> memberIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        FeedPageCalls++;
        LastFeedAuthorIds = memberIds.ToList();
        var matching = items
            .Where(item => item.AuthorId is Guid authorId && memberIds.Contains(authorId))
            .OrderByDescending(item => item.PublishedAt)
            .ToList();
        var pageItems = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new MemberPublicActivityPage(pageItems, matching.Count, page, pageSize));
    }
}

internal sealed class MutableCommunityArticleRepository : IArticleRepository
{
    private List<PublishedArticleSubmission> items = [];

    public void Seed(IEnumerable<PublishedArticleSubmission> seed) =>
        items = [.. seed.OrderByDescending(article => article.PublishedAt)];

    public void Reset() => items = [];

    private static bool HasTag(string? tags, string tag) =>
        !string.IsNullOrWhiteSpace(tags) &&
        ("," + tags + ",").Contains("," + tag + ",", StringComparison.OrdinalIgnoreCase);

    public Task<int> GetCountAsync(string? tag = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(tag) ? items.Count : items.Count(article => HasTag(article.Tags, tag)));

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page,
        int pageSize,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PublishedArticleSubmission> result = (string.IsNullOrWhiteSpace(tag)
            ? items
            : items.Where(article => HasTag(article.Tags, tag)))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<PublishedArticleSubmission?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(article =>
            string.Equals(article.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default)
    {
        var previous = items.FirstOrDefault(article => article.PublishedAt < publishedAt);
        var next = items.LastOrDefault(article => article.PublishedAt > publishedAt);
        return Task.FromResult<(PublishedArticleSubmission?, PublishedArticleSubmission?)>((previous, next));
    }

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(items);
}

internal sealed class SqlFailingCommunityArticleRepository : IArticleRepository
{
    public Task<int> GetCountAsync(string? tag = null, CancellationToken cancellationToken = default) =>
        throw SqlExceptionFactory.Create(208, "Invalid object name 'ArticleSubmissions'.");

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page,
        int pageSize,
        string? tag = null,
        CancellationToken cancellationToken = default) =>
        throw SqlExceptionFactory.Create(208, "Invalid object name 'ArticleSubmissions'.");

    public Task<PublishedArticleSubmission?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        throw SqlExceptionFactory.Create(208, "Invalid object name 'ArticleSubmissions'.");

    public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default) =>
        throw SqlExceptionFactory.Create(208, "Invalid object name 'ArticleSubmissions'.");

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(
        CancellationToken cancellationToken = default) =>
        throw SqlExceptionFactory.Create(208, "Invalid object name 'ArticleSubmissions'.");
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset current = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => current;

    public void SetUtcNow(DateTimeOffset utcNow) => current = utcNow;

    public void Advance(TimeSpan duration) => current += duration;

    public void Reset() => current = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class ThrowOnReadBlobService : IBlobUploadService
{
    public Task<BlobUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string containerName,
        BlobUploadContext? context = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BlobContent?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("List must not open audio blobs.");
}

internal sealed class SequentialTriviaRepository(params TriviaFactItem[] facts) : ITriviaRepository
{
    public int AllCallCount { get; private set; }

    public int RandomCallCount { get; private set; }

    public void Reset()
    {
        AllCallCount = 0;
        RandomCallCount = 0;
    }

    public Task<IReadOnlyList<TriviaFactItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        AllCallCount++;
        return Task.FromResult<IReadOnlyList<TriviaFactItem>>(facts);
    }

    public Task<TriviaFactItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(facts.SingleOrDefault(fact => fact.Id == id));

    public Task<TriviaFactItem?> GetRandomPublishedAsync(CancellationToken cancellationToken = default)
    {
        RandomCallCount++;
        return Task.FromResult<TriviaFactItem?>(null);
    }

    public Task<int> CreateAsync(AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task UpdateAsync(int id, AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetPublishedAsync(int id, bool isPublished, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class SeedableNewsRepository : INewsRepository
{
    private FixedNewsRepository inner = new([]);

    public void Seed(params NewsItem[] items) => inner = new FixedNewsRepository(items);

    public void Reset() => inner = new FixedNewsRepository([]);

    public Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(count, cancellationToken);

    public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        inner.GetArchivePageAsync(page, pageSize, filter, cancellationToken);

    public Task<int> GetPublishedCountAsync(
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        inner.GetPublishedCountAsync(filter, cancellationToken);

    public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
        inner.GetArchiveYearRangeAsync(cancellationToken);

    public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default) =>
        inner.GetByIdsAsync(ids, cancellationToken);

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
        CancellationToken cancellationToken = default) =>
        inner.GetPublishedSitemapEntriesAsync(cancellationToken);

    public Task<NewsSearchPage> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.SearchAsync(query, page, pageSize, cancellationToken);
}

internal sealed class SeedableDiscussionLookup : INewsForumDiscussionLookup
{
    private int? topicId;
    private int replyCount;
    private IReadOnlyList<NewsDiscussionPreview> preview = [];

    public void Seed(int? seededTopicId, int seededReplyCount, IReadOnlyList<NewsDiscussionPreview> seededPreview)
    {
        topicId = seededTopicId;
        replyCount = seededReplyCount;
        preview = seededPreview;
    }

    public void Reset()
    {
        topicId = null;
        replyCount = 0;
        preview = [];
    }

    public Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
        IReadOnlyList<int> topicIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, int> counts = topicId is int id && topicIds.Contains(id)
            ? new Dictionary<int, int> { [id] = replyCount }
            : new Dictionary<int, int>();
        return Task.FromResult(counts);
    }

    public Task<(int ReplyCount, IReadOnlyList<NewsDiscussionPreview> Preview)> GetDiscussionAsync(
        int requestedTopicId,
        int previewCount,
        CancellationToken cancellationToken = default)
    {
        if (topicId != requestedTopicId)
        {
            return Task.FromResult<(int, IReadOnlyList<NewsDiscussionPreview>)>((0, []));
        }

        return Task.FromResult((replyCount, preview));
    }
}

internal sealed class CountingBiographyRepository(IReadOnlyList<BiographyChapterItem> chapters) : IBiographyRepository
{
    public int ListCallCount { get; private set; }

    public int DetailCallCount { get; private set; }

    public int AdjacentCallCount { get; private set; }

    public void Reset()
    {
        ListCallCount = 0;
        DetailCallCount = 0;
        AdjacentCallCount = 0;
    }

    public Task<IReadOnlyList<BiographyChapterItem>> GetChaptersAsync(CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        return Task.FromResult(chapters);
    }

    public Task<BiographyChapterItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        DetailCallCount++;
        return Task.FromResult(chapters.SingleOrDefault(chapter => chapter.Id == id));
    }

    public Task<BiographyChapterNav> GetAdjacentChaptersAsync(int id, CancellationToken cancellationToken = default)
    {
        AdjacentCallCount++;
        return Task.FromResult(new BiographyChapterNav(null, null));
    }

    public Task<int> CreateAsync(AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task UpdateAsync(int id, AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

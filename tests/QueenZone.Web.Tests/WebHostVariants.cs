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
public static class WebHostVariants
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
        context.FanPerformanceSubmissions ??= new InMemoryFanPerformanceSubmissionRepository();
        services.RemoveAll<IFanPerformanceSubmissionRepository>();
        services.AddSingleton<IFanPerformanceSubmissionRepository>(context.FanPerformanceSubmissions);
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

    public EditorStubBlobUploadService? EditorBlob { get; set; }

    public RecordingMemberPublicActivityRepository? MemberActivity { get; set; }

    public MutableCommunityArticleRepository? CommunityArticles { get; set; }

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

using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;
using QueenZone.Storage;
using QueenZone.Web.Health;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web;

public static class QueenZoneWebServiceCollectionExtensions
{
    public static IServiceCollection AddQueenZoneWebOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AdminOptions>()
            .Bind(configuration.GetSection(AdminOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdminOptions>, AdminOptionsValidator>();

        services.AddOptions<SiteOptions>()
            .Bind(configuration.GetSection(SiteOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();

        services.AddOptions<QueenZoneHostFilteringOptions>()
            .Bind(configuration.GetSection(QueenZoneHostFilteringOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<QueenZoneHostFilteringOptions>, QueenZoneHostFilteringOptionsValidator>();

        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnalyticsOptions>, AnalyticsOptionsValidator>();

        services.AddOptions<SitemapOptions>()
            .Bind(configuration.GetSection(SitemapOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SitemapOptions>, SitemapOptionsValidator>();

        services.AddOptions<MemberAuthenticationOptions>()
            .Bind(configuration.GetSection(MemberAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MemberAuthenticationOptions>, MemberAuthenticationOptionsValidator>();

        services.AddOptions<ForumDataOptions>()
            .Bind(configuration.GetSection(ForumDataOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForumDataOptions>, ForumDataOptionsValidator>();

        services.AddOptions<ForumOptions>()
            .Bind(configuration.GetSection(ForumOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForumOptions>, ForumOptionsValidator>();

        services.AddOptions<NewsForumOptions>()
            .Bind(configuration.GetSection(NewsForumOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NewsForumOptions>, NewsForumOptionsValidator>();

        services.AddOptions<PublicQueryCacheOptions>()
            .Bind(configuration.GetSection(PublicQueryCacheOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PublicQueryCacheOptions>, PublicQueryCacheOptionsValidator>();

        services.AddOptions<ForumAttachmentOptions>()
            .Bind(configuration.GetSection(ForumAttachmentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForumAttachmentOptions>, ForumAttachmentOptionsValidator>();

        services.AddOptions<UploadQuotaOptions>()
            .Bind(configuration.GetSection(UploadQuotaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<UploadQuotaOptions>, UploadQuotaOptionsValidator>();

        services.AddOptions<BlobUploadOptions>()
            .Bind(configuration.GetSection(BlobUploadOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BlobUploadOptions>, BlobUploadOptionsValidator>();

        services.AddOptions<NewsSuggestionOptions>()
            .Bind(configuration.GetSection(NewsSuggestionOptions.SectionName));

        services.AddOptions<FanPerformanceSubmissionOptions>()
            .Bind(configuration.GetSection(FanPerformanceSubmissionOptions.SectionName));

        services.AddOptions<HelpRequestOptions>()
            .Bind(configuration.GetSection(HelpRequestOptions.SectionName));

        services.AddOptions<PrivateMessageRateLimitOptions>()
            .Bind(configuration.GetSection(PrivateMessageRateLimitOptions.SectionName));

        services.AddOptions<MobileAuthOptions>()
            .Bind(configuration.GetSection(MobileAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MobileAuthOptions>, MobileAuthOptionsValidator>();

        services.AddOptions<GalleryOrphanSweepOptions>()
            .Bind(configuration.GetSection(GalleryOrphanSweepOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GalleryOrphanSweepOptions>, GalleryOrphanSweepOptionsValidator>();

        services.AddOptions<PushNotificationOptions>()
            .Bind(configuration.GetSection(PushNotificationOptions.SectionName));

        return services;
    }

    public static IServiceCollection AddQueenZoneRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<FanPerformanceRateLimitingOptions>()
            .Bind(configuration.GetSection(FanPerformanceRateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FanPerformanceRateLimitingOptions>, FanPerformanceRateLimitingOptionsValidator>();
        services.AddOptions<AuthRateLimitingOptions>()
            .Bind(configuration.GetSection(AuthRateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthRateLimitingOptions>, AuthRateLimitingOptionsValidator>();
        services.AddSingleton<MobileAuthAccountRateLimiter>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = AuthRateLimitRejection.WriteAsync;

            limiter.AddPolicy(FanPerformanceRateLimitingOptions.AudioPolicy, context =>
            {
                var opts = context.RequestServices
                    .GetRequiredService<IOptions<FanPerformanceRateLimitingOptions>>().Value;
                return RateLimitPartition.GetSlidingWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = opts.AudioPermitLimit,
                        Window = TimeSpan.FromSeconds(opts.AudioSlidingWindowSeconds),
                        SegmentsPerWindow = Math.Max(1, opts.AudioSlidingWindowSeconds / 60),
                        QueueLimit = 0,
                    });
            });

            limiter.AddPolicy(FanPerformanceRateLimitingOptions.BrowsePolicy, context =>
            {
                var opts = context.RequestServices
                    .GetRequiredService<IOptions<FanPerformanceRateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.BrowsePermitLimit,
                        Window = TimeSpan.FromSeconds(opts.BrowseWindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Auth challenges (OAuth start + /api/v1/auth) — IP only; per-member mobile
            // caps live in MobileAuthAccountRateLimiter. Soft trust in X-Forwarded-For
            // behind Cloudflare. Process-local; correct on a single B1 worker.
            limiter.AddPolicy(QueenZoneRateLimitPolicies.Auth, context =>
            {
                var opts = context.RequestServices
                    .GetRequiredService<IOptions<AuthRateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIpPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.IpPermitLimit,
                        Window = TimeSpan.FromMinutes(opts.IpWindowMinutes),
                        QueueLimit = 0,
                    });
            });

            // Member submissions — prefer member id, fall back to IP.
            limiter.AddPolicy(QueenZoneRateLimitPolicies.MemberWrite, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetMemberOrIpPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Uploads (editor image, avatar) — member-centric.
            limiter.AddPolicy(QueenZoneRateLimitPolicies.Upload, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetMemberOrIpPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Public search — IP partition (soft).
            limiter.AddPolicy(QueenZoneRateLimitPolicies.Search, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIpPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    private static string GetClientIpPartition(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string GetMemberOrIpPartition(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue(ClaimTypes.Name)
        ?? GetClientIpPartition(context);

    public static IServiceCollection AddQueenZoneCaching(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOutputCache(options =>
        {
            options.AddPolicy(PublicOutputCachePolicies.PublicSitemaps, policy => policy
                .With(context => PublicOutputCachePolicies.IsPublicReadOnlyRequest(context.HttpContext))
                .Expire(PublicOutputCachePolicies.SitemapDuration)
                .SetVaryByRouteValue("*")
                .Tag(PublicOutputCachePolicies.PublicSitemapTag));

            options.AddPolicy(PublicOutputCachePolicies.PublicHtml, policy => policy
                .With(context => PublicOutputCachePolicies.IsCacheablePublicHtmlRequest(context.HttpContext))
                .Expire(PublicOutputCachePolicies.HtmlDuration)
                .SetVaryByRouteValue("*")
                .SetVaryByQuery("*")
                .Tag(PublicOutputCachePolicies.PublicHtmlTag));
        });
        services.AddScoped<PublicQueryCacheService>();
        return services;
    }

    public static IServiceCollection AddQueenZoneSitemaps(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        // Scoped: sitemap builders depend on EF-backed content repositories (scoped DbContext).
        services.AddScoped<CoreSitemapBuilder>();
        services.AddScoped<CoreSitemapService>();
        services.AddScoped<ForumSitemapBuilder>();
        services.AddScoped<SitemapIndexBuilder>();
        return services;
    }

    public static IServiceCollection AddQueenZoneWebAppServices(this IServiceCollection services)
    {
        services.AddScoped<MemberAccountService>();
        services.AddHostedService<MemberAccountDeletionHostedService>();
        services.AddScoped<PrivateMessageRateLimiter>();
        services.AddScoped<PrivateMessageService>();
        services.AddHostedService<PrivateMessageReportPurgeHostedService>();
        services.AddScoped<MemberFollowService>();
        services.AddScoped<TopicWatchService>();
        services.AddScoped<PhotoSubmissionService>();
        services.AddScoped<FanPerformanceSubmissionService>();
        services.AddScoped<FanPerformanceCreditResolver>();
        services.AddScoped<FanPerformanceReportService>();
        services.AddScoped<AdminPhotoService>();
        services.AddScoped<AdminFanPerformanceWriteService>();
        services.AddScoped<PhotoSubmissionPromotionService>();
        services.AddScoped<FanPerformanceSubmissionPromotionService>();
        services.AddScoped<FanPerformanceSubmissionPurgeService>();
        services.AddHostedService<FanPerformanceSubmissionPurgeHostedService>();
        services.AddScoped<GalleryOrphanSweepService>();
        services.AddHostedService<GalleryOrphanSweepHostedService>();
        services.AddScoped<NewsSuggestionService>();
        services.AddSingleton<HelpRequestFormStamp>();
        services.AddSingleton<HelpRequestRateLimiter>();
        services.AddScoped<HelpRequestService>();
        services.AddScoped<PublicWarmupService>();
        services.AddScoped<UgcHtml>();
        services.AddScoped<FanPerformanceDurationResolver>();
        services.AddScoped<FanPerformanceAudioUploadService>();
        services.AddScoped<ForumPostRateLimiter>();
        services.AddSingleton<MemberUploadQuotaService>();
        services.AddScoped<ForumAttachmentValidator>();
        services.AddScoped<ForumAttachmentUploadService>();
        services.AddScoped<ForumPostWriteService>();
        services.AddScoped<ForumPostReportService>();
        services.AddScoped<HomePollVoteService>();
        services.AddSingleton<IFcmAccessTokenProvider, GoogleFcmAccessTokenProvider>();
        services.AddHttpClient(DirectPushTransport.ApnsClientName, client =>
        {
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient(DirectPushTransport.FcmClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IPushTransport, DirectPushTransport>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<INewsForumTopicService, NewsForumTopicService>();
        services.AddScoped<NewsDiscussionComposer>();
        services.AddScoped<AdminNewsWriteService>();
        services.AddScoped<AdminMemberSuspendService>();
        services.AddScoped<AdminReviewerAccountService>();
        services.AddScoped<NewsArticleImageService>();
        services.AddSingleton<IGoogleAnalyticsDataClient, GoogleAnalyticsDataClient>();
        services.AddScoped<IGoogleAnalyticsTrafficService, GoogleAnalyticsTrafficService>();
        services.AddScoped<AdminDashboardService>();
        services.AddSingleton<MobileAuthAuthorizationSessionStore>();
        services.AddSingleton<MobileAuthTokenIssuer>();
        services.AddScoped<MobileAuthService>();
        // Header name used by the rich-text editor fetch() upload helper.
        services.AddAntiforgery(options =>
        {
            options.HeaderName = EditorImageUploadEndpoints.AntiforgeryHeaderName;
        });
        // Per-field cap. MultipartBodyLengthLimit is a different knob; page-level
        // [RequestFormLimits] is not applied until after UseAntiforgery reads the form.
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.ValueLengthLimit = 16 * 1024 * 1024;
        });
        return services;
    }

    public static IServiceCollection AddQueenZoneData(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // WebApplicationFactory tests must never inherit a developer or CI shell's
        // production connection string. Opt-in live probes construct their SQL-backed
        // repositories directly and do not use the Testing web host composition. E2E falls
        // through to the real legacy data branch below, but only after E2EConnectionGuard
        // confirms the connection string targets the disposable SQL Express mirror.
        services.AddScoped<SearchReindexBuilder>();
        services.AddScoped<Search.ForumSearchIndexSynchronizer>();
        // In-process single-flight job for /admin/search (single-instance hosting).
        services.AddSingleton<Search.SearchReindexJobService>();

        if (QueenZoneEnvironments.UsesInMemoryData(environment))
        {
            services.AddQueenZoneInMemoryData();
            services.AddHostedService<Search.SearchIndexSeedHostedService>();
            services.AddHostedService<SampleGalleryImageSeedHostedService>();
            return services;
        }

        var legacyConnectionString = configuration.GetConnectionString("QueenZoneLegacy");
        if (environment.IsEnvironment(QueenZoneEnvironments.E2E))
        {
            E2EConnectionGuard.EnsureSafe(legacyConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(legacyConnectionString))
        {
            var forumDataOptions = configuration
                .GetSection(ForumDataOptions.SectionName)
                .Get<ForumDataOptions>() ?? new ForumDataOptions();

            services.AddQueenZoneLegacyData(legacyConnectionString, forumDataOptions);
        }
        else
        {
            services.AddQueenZoneInMemoryData();
            services.AddHostedService<Search.SearchIndexSeedHostedService>();
            services.AddHostedService<SampleGalleryImageSeedHostedService>();
        }

        return services;
    }

    public static IServiceCollection AddQueenZoneWebComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddQueenZoneWebOptions(configuration);
        services.AddQueenZoneCaching();
        services.AddQueenZoneRateLimiting(configuration);
        services.AddQueenZoneSitemaps();
        services.AddQueenZoneWebAppServices();

        if (ResponseCompressionBootstrap.IsEnabled(environment))
        {
            ResponseCompressionBootstrap.ConfigureServices(services);
        }

        services.AddQueenZoneData(configuration, environment);
        if (QueenZoneEnvironments.UsesInMemoryBlobStorage(environment))
        {
            services.AddQueenZoneFunctionalInMemoryStorage(configuration);
        }
        else
        {
            services.AddQueenZoneStorage(configuration);
        }

        services.AddQueenZoneHealthChecks();
        // Admin draft regenerate only — discovery fetchers/worker stay on NewsAgent.Worker (#336).
        services.AddQueenZoneNewsAgentWeb(configuration);
        services.AddQueenZoneAuth(configuration, environment);
        services.AddQueenZoneAuthorization(configuration, environment);
        services.AddQueenZoneJsonApi();
        services.AddMobileApiContractHost(environment);

        return services;
    }

    /// <summary>
    /// Opt-in Testing-only contract-host bootstrap (#869). Never registered for E2E or
    /// production-like environments, and never unless
    /// <see cref="MobileApiContractHost.EnableEnvironmentVariable"/> is set.
    /// </summary>
    public static IServiceCollection AddMobileApiContractHost(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        if (MobileApiContractHost.IsEnabled(environment))
        {
            services.AddHostedService<MobileApiContractHostedService>();
        }

        return services;
    }
}

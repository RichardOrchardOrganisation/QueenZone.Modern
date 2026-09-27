using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Tests;

public sealed class QueenZoneWebCompositionTests
{
    [Fact]
    public void AddQueenZoneWebComposition_registers_core_services_and_validates_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
                ["Sitemap:CacheHours"] = "24",
                ["PublicQueryCache:NewsCacheDuration"] = "00:05:00",
                ["PublicQueryCache:ArticleCountCacheDuration"] = "00:30:00",
                ["PublicQueryCache:ForumStatsCacheDuration"] = "00:30:00",
                ["PublicQueryCache:OnThisDayCacheDuration"] = "12:00:00",
                ["OpenRouter:DryRun"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Testing"));
        services.AddQueenZoneWebComposition(configuration, new FakeHostEnvironment("Testing"));

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(provider.GetRequiredService<INewsRepository>());
        // Testing/E2E use a functional in-memory-backed AzureBlobUploadService (#546) so
        // member upload flows (photo/article/avatar submission) actually succeed, not the
        // NullBlobUploadService fail-loud fallback used for a truly unconfigured local dev.
        Assert.IsType<AzureBlobUploadService>(provider.GetRequiredService<IBlobUploadService>());
        using (var scope = provider.CreateScope())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBiographyRepository>());
            Assert.IsType<InMemoryBiographyRepository>(
                scope.ServiceProvider.GetRequiredService<IBiographyRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<CoreSitemapService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<PublicQueryCacheService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<PublicWarmupService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<MemberAccountService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILinksRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHelpRequestRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMobileAuthGrantRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<MobileAuthService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<MobileAuthAccountRateLimiter>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<INotificationDispatcher>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPushTransport>());
            Assert.IsType<InMemoryTopicWatchRepository>(
                scope.ServiceProvider.GetRequiredService<ITopicWatchLookup>());
            Assert.Same(
                scope.ServiceProvider.GetRequiredService<ITopicWatchLookup>(),
                scope.ServiceProvider.GetRequiredService<ITopicWatchRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<AdminNewsWriteService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAdminFanPerformanceRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<AdminFanPerformanceWriteService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceAudioUploadService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFanPerformanceSubmissionRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceSubmissionService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceCreditResolver>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceReportService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFanPerformanceReportRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceSubmissionPromotionService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceSubmissionPurgeService>());
        }

        Assert.Equal(["admin@test.local"], provider.GetRequiredService<IOptions<AdminOptions>>().Value.AllowedEmails);
        Assert.Equal("https://www.queenzone.org", provider.GetRequiredService<IOptions<SiteOptions>>().Value.PublicBaseUrl);
        Assert.Equal(50, provider.GetRequiredService<IOptions<UploadQuotaOptions>>().Value.MaxUploadsPerDay);
        Assert.True(provider.GetRequiredService<IOptions<ForumDataOptions>>().Value.UseModernForumReads);
        Assert.Equal(60, provider.GetRequiredService<IOptions<ForumOptions>>().Value.PostEditWindowMinutes);
        Assert.Equal(5, provider.GetRequiredService<IOptions<ForumAttachmentOptions>>().Value.MaxFilesPerPost);
        Assert.Equal(60, provider.GetRequiredService<IOptions<AnalyticsOptions>>().Value.TrafficCacheMinutes);
        Assert.Null(provider.GetRequiredService<IOptions<MemberAuthenticationOptions>>().Value.Google?.ClientId);
        Assert.Equal(60, provider.GetRequiredService<IOptions<FanPerformanceRateLimitingOptions>>().Value.AudioPermitLimit);
        Assert.Equal(7, provider.GetRequiredService<IOptions<FanPerformanceSubmissionOptions>>().Value.StaleAfterDays);
        Assert.Equal(30, provider.GetRequiredService<IOptions<AuthRateLimitingOptions>>().Value.IpPermitLimit);
        Assert.Equal(10, provider.GetRequiredService<IOptions<AuthRateLimitingOptions>>().Value.AccountPermitLimit);
        Assert.Equal(
            20,
            provider.GetRequiredService<IOptions<MutationRateLimitingOptions>>().Value.AnonymousPermitLimit);
        Assert.Equal(
            20,
            provider.GetRequiredService<IOptions<MutationRateLimitingOptions>>()
                .Value.AuthenticatedMemberPermitLimit);
        Assert.Equal(
            120,
            provider.GetRequiredService<IOptions<MutationRateLimitingOptions>>()
                .Value.AuthenticatedIpPermitLimit);
        Assert.Equal(10 * 1024 * 1024, provider.GetRequiredService<IOptions<BlobUploadOptions>>().Value.DefaultMaxBytes);
        Assert.Equal(
            25 * 1024 * 1024,
            provider.GetRequiredService<IOptions<BlobUploadOptions>>().Value
                .Containers[BlobUploadContainers.FanPerformances].MaxBytes);

        // #336: web composition uses editorial AI surface only (not discovery worker pipeline).
        using (var newsScope = provider.CreateScope())
        {
            Assert.NotNull(newsScope.ServiceProvider.GetRequiredService<NewsDraftGenerationService>());
            Assert.Null(newsScope.ServiceProvider.GetService<DiscoverNewsWorker>());
            Assert.Null(newsScope.ServiceProvider.GetService<NewsDiscoveryService>());
            Assert.Empty(newsScope.ServiceProvider.GetServices<INewsSourceFetcher>());
        }
    }

    [Fact]
    public void AdminOptionsValidator_rejects_empty_allowed_emails_in_production()
    {
        var result = new AdminOptionsValidator(new FakeHostEnvironment("Production"))
            .Validate(null, new AdminOptions { AllowedEmails = [] });
        Assert.True(result.Failed);
        Assert.Contains("Admin__AllowedEmails", result.FailureMessage);
    }

    [Fact]
    public void AdminOptionsValidator_allows_empty_allowed_emails_in_development()
    {
        var result = new AdminOptionsValidator(new FakeHostEnvironment("Development"))
            .Validate(null, new AdminOptions { AllowedEmails = [] });
        Assert.False(result.Failed);
    }

    [Fact]
    public void AdminOptionsValidator_rejects_blank_entries()
    {
        var result = new AdminOptionsValidator(new FakeHostEnvironment("Development"))
            .Validate(null, new AdminOptions { AllowedEmails = ["admin@test.local", "  "] });
        Assert.True(result.Failed);
    }

    [Fact]
    public void AddQueenZoneWebOptions_rejects_production_without_blob_or_member_oauth()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
                ["Analytics:MeasurementId"] = "G-V2W56BZ3KZ",
            })
            .Build();

        var environment = new FakeHostEnvironment("Production");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddQueenZoneWebOptions(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var blobEx = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BlobUploadOptions>>().Value);
        Assert.Contains("ConnectionStrings:BlobStorage", blobEx.Message);

        var authEx = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MemberAuthenticationOptions>>().Value);
        Assert.Contains("OAuth provider", authEx.Message);

        // Mobile PKCE signing is optional at startup. A missing App Service
        // MobileAuth__SigningKey must not prevent the public site from booting.
        Assert.Equal(
            MobileAuthOptions.DefaultClientId,
            provider.GetRequiredService<IOptions<MobileAuthOptions>>().Value.ClientId);
        Assert.True(string.IsNullOrWhiteSpace(
            provider.GetRequiredService<IOptions<MobileAuthOptions>>().Value.SigningKey));
    }

    [Fact]
    public void AddQueenZoneWebOptions_accepts_complete_production_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
                ["Analytics:MeasurementId"] = "G-V2W56BZ3KZ",
                ["Authentication:Google:ClientId"] = "google-client",
                ["Authentication:Google:ClientSecret"] = "google-secret",
                ["MobileAuth:SigningKey"] = "testing-mobile-auth-signing-key-32b!",
                ["ConnectionStrings:BlobStorage"] =
                    "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net",
            })
            .Build();

        var environment = new FakeHostEnvironment("Production");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddQueenZoneWebOptions(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.Equal(
            "G-V2W56BZ3KZ",
            provider.GetRequiredService<IOptions<AnalyticsOptions>>().Value.MeasurementId);
        Assert.Equal(
            "google-client",
            provider.GetRequiredService<IOptions<MemberAuthenticationOptions>>().Value.Google?.ClientId);
        Assert.Equal(
            10 * 1024 * 1024,
            provider.GetRequiredService<IOptions<BlobUploadOptions>>().Value.DefaultMaxBytes);
        Assert.Equal(
            "testing-mobile-auth-signing-key-32b!",
            provider.GetRequiredService<IOptions<MobileAuthOptions>>().Value.SigningKey);
    }

    [Fact]
    public void AddQueenZoneWebComposition_registers_upload_quota_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
                ["Sitemap:CacheHours"] = "24",
                ["PublicQueryCache:NewsCacheDuration"] = "00:05:00",
                ["PublicQueryCache:ArticleCountCacheDuration"] = "00:30:00",
                ["PublicQueryCache:ForumStatsCacheDuration"] = "00:30:00",
                ["PublicQueryCache:OnThisDayCacheDuration"] = "12:00:00",
                ["OpenRouter:DryRun"] = "true",
                ["UploadQuotas:MaxUploadsPerDay"] = "25",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Testing"));
        services.AddQueenZoneWebComposition(configuration, new FakeHostEnvironment("Testing"));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var quota = provider.GetRequiredService<MemberUploadQuotaService>();
        Assert.NotNull(quota);
        Assert.Equal(25, provider.GetRequiredService<IOptions<UploadQuotaOptions>>().Value.MaxUploadsPerDay);
    }

    [Fact]
    public void AddQueenZoneWebComposition_uses_in_memory_data_in_testing_even_when_sql_is_configured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
                ["ConnectionStrings:QueenZoneLegacy"] =
                    "Server=production.example;Database=QueenZone;User Id=test;Password=do-not-connect",
                ["ConnectionStrings:BlobStorage"] =
                    "DefaultEndpointsProtocol=https;AccountName=production;AccountKey=do-not-connect;EndpointSuffix=core.windows.net",
                ["OpenRouter:DryRun"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Testing"));
        services.AddQueenZoneWebComposition(configuration, new FakeHostEnvironment("Testing"));

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<InMemoryArticleSubmissionRepository>(
            provider.GetRequiredService<IArticleSubmissionRepository>());
        Assert.Null(provider.GetService<QueenZoneDbContext>());
        // Testing/E2E use a functional in-memory-backed AzureBlobUploadService (#546) so
        // member upload flows (photo/article/avatar submission) actually succeed, not the
        // NullBlobUploadService fail-loud fallback used for a truly unconfigured local dev.
        Assert.IsType<AzureBlobUploadService>(provider.GetRequiredService<IBlobUploadService>());
        Assert.IsType<NullGalleryPhotoBlobService>(provider.GetRequiredService<IGalleryPhotoBlobService>());
    }

    [Fact]
    public void SiteOptionsValidator_rejects_non_absolute_url()
    {
        var result = new SiteOptionsValidator().Validate(null, new SiteOptions { PublicBaseUrl = "not-a-url" });
        Assert.True(result.Failed);
    }

    [Fact]
    public void SitemapOptionsValidator_rejects_non_positive_cache_hours()
    {
        var result = new SitemapOptionsValidator().Validate(null, new SitemapOptions { CacheHours = 0 });
        Assert.True(result.Failed);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

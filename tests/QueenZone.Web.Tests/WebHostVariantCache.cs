using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Per-class cache of named web hosts. Each variant is built once; <see cref="DisposeAsync"/>
/// disposes every host the class created.
/// </summary>
public sealed class WebHostVariantCache : IAsyncDisposable
{
    private readonly ConcurrentDictionary<WebHostVariant, Lazy<VariantWebApplicationFactory>> hosts = new();
    private readonly object gate = new();

    public VariantWebApplicationFactory Get(WebHostVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        Lazy<VariantWebApplicationFactory> lazy;
        lock (gate)
        {
            foreach (var existing in hosts.Keys)
            {
                if (string.Equals(existing.Name, variant.Name, StringComparison.Ordinal)
                    && !existing.Equals(variant))
                {
                    throw new InvalidOperationException(
                        $"Web host variant name '{variant.Name}' is already used by a different configuration.");
                }
            }

            lazy = hosts.GetOrAdd(
                variant,
                static value => new Lazy<VariantWebApplicationFactory>(
                    () => new VariantWebApplicationFactory(value),
                    LazyThreadSafetyMode.ExecutionAndPublication));
        }

        var factory = lazy.Value;
        _ = factory.Services;
        return factory;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in hosts.Values)
        {
            if (lazy.IsValueCreated)
            {
                await lazy.Value.DisposeAsync();
            }
        }

        hosts.Clear();
    }
}

/// <summary>Testing (or named-environment) host built from a declared <see cref="WebHostVariant"/>.</summary>
public class VariantWebApplicationFactory : QueenZoneWebApplicationFactory, IResettableHostFixture
{
    private readonly WebHostVariant variant;
    private readonly HostServiceContext context = new();

    public VariantWebApplicationFactory(WebHostVariant variant)
    {
        this.variant = variant ?? throw new ArgumentNullException(nameof(variant));
    }

    internal InMemoryBlobStorageBackend BlobBackend => context.BlobBackend;

    internal MutableLegacyMemberLookupRepository LegacyLookup => context.LegacyLookup;

    internal TrackingNewsRepository? TrackingNews => context.TrackingNews;

    internal CountingArticlesRepository? CountingArticles => context.CountingArticles;

    internal SharedQuizStore? QuizStore => context.QuizStore;

    internal InMemoryQuizQuestionSubmissionRepository? QuizQuestionSubmissions => context.QuizQuestionSubmissions;

    internal MemberUploadQuotaService? UploadQuota => context.UploadQuota;

    internal ConfigurableNewsSuggestionRepository? ConfigurableNewsSuggestions => context.ConfigurableNewsSuggestions;

    internal InMemoryFanPerformanceSubmissionRepository? FanPerformanceSubmissions => context.FanPerformanceSubmissions;

    internal EditorStubBlobUploadService? EditorBlob => context.EditorBlob;

    internal RecordingMemberPublicActivityRepository? MemberActivity => context.MemberActivity;

    internal MutableCommunityArticleRepository? CommunityArticles => context.CommunityArticles;

    internal SharedHomePollStore? HomePolls => context.HomePolls;

    internal SharedTriviaStore? Trivia => context.Trivia;

    internal SequentialTriviaRepository? SequentialTrivia => context.SequentialTrivia;

    internal MutableTimeProvider? Clock => context.Clock;

    internal SeedableNewsRepository? SeedableNews => context.SeedableNews;

    internal SeedableDiscussionLookup? SeedableDiscussion => context.SeedableDiscussion;

    internal CountingBiographyRepository? CountingBiography => context.CountingBiography;

    internal SharedQuoteStore? Quotes => context.Quotes;

    internal SharedNewsStore? AdminNews => context.AdminNews;

    internal SharedNewsDiscoveryStore? AdminDiscovery => context.AdminDiscovery;

    internal AdminNewsMutationOverrides? AdminNewsMutations => context.AdminNewsMutations;

    internal ConfigurableNewsDiscoveryRepository? ConfigurableDiscovery => context.ConfigurableDiscovery;

    internal ConfigurableNewsAiClient? NewsAi => context.NewsAi;

    internal SharedNewsAgentRunRequestStore? NewsAgentRunRequests => context.NewsAgentRunRequests;

    internal SharedBiographyStore? AdminBiography => context.AdminBiography;

    internal SharedQueenHistoryStore? AdminTimeline => context.AdminTimeline;

    internal SharedFreddieTributeStore? AdminFreddieTributes => context.AdminFreddieTributes;

    internal SharedNewsAgentGuidanceStore? AdminGuidance => context.AdminGuidance;

    internal FixedIdAttachmentRepository? FixedForumAttachment => context.FixedForumAttachment;

    internal RecordingPushTransport? PushTransport => context.PushTransport;

    internal FakeTopicWatchLookup? TopicWatch => context.TopicWatch;

    internal ConfigurableAlwaysWatchLookup? AlwaysWatch => context.AlwaysWatch;

    internal SeedableMemberPageActivityRepository? MemberPageActivity => context.MemberPageActivity;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(variant.Environment);
        if (string.Equals(variant.Environment, "Development", StringComparison.Ordinal))
        {
            builder.UseSetting(QueenZoneDevelopmentHost.SkipLocalSettingsKey, "true");
        }

        if (string.Equals(variant.Environment, "Production", StringComparison.Ordinal))
        {
            ProductionHostSettings.Apply(builder);
        }

        if (variant.Settings.Count > 0)
        {
            foreach (var pair in variant.Settings)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(variant.Settings));
        }

        builder.ConfigureTestServices(services => WebHostVariants.Apply(variant.Services, services, context));
    }

    public override async Task ResetAsync()
    {
        // Start the host (via Services in the base reset) so context fakes exist, then
        // clear mutable seed. Sitemap RSS stays cached on Testing hosts, so eviction lives
        // on the base reset rather than Production-only.
        await base.ResetAsync();
        context.Reset();
    }
}

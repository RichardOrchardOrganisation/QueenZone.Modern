using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
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

    public void Reset()
    {
        BlobBackend.Clear();
        LegacyLookup.Reset();
        TrackingNews?.Reset();
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

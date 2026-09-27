using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> for deterministic Web.Tests hosts.
/// Always uses the Testing environment so sample/in-memory data and test auth stay enabled.
/// </summary>
public class QueenZoneWebApplicationFactory : WebApplicationFactory<Program>, IResettableHostFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        ConfigureTestServices(builder);
    }

    /// <summary>Override to replace services for a scenario without re-setting Testing.</summary>
    protected virtual void ConfigureTestServices(IWebHostBuilder builder)
    {
    }

    public HttpClient CreateAnonymousClient(bool allowAutoRedirect = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true,
        });

    public HttpClient CreateAdminClient(string? email = null, bool allowAutoRedirect = false) =>
        AdminHttpTestHelpers.CreateClient(this, email ?? AdminHttpTestHelpers.AdminEmail);

    public virtual async Task ResetAsync()
    {
        Services.GetService<HelpRequestRateLimiter>()?.Reset();
        if (Services.GetService<IEditorialArticleRepository>() is InMemoryEditorialArticleRepository editorial)
        {
            editorial.Clear();
        }

        Services.GetService<SharedSearchIndexStore>()?.Clear();
        if (Services.GetService<IMemberAccountRepository>() is InMemoryMemberAccountRepository members)
        {
            members.Clear();
        }

        Services.GetService<SharedDeviceTokenStore>()?.Clear();
        if (Services.GetService<IBlobStorageBackend>() is InMemoryBlobStorageBackend blobs)
        {
            blobs.Clear();
        }

        if (Services.GetService<IBlobUploadService>() is MemoryBlobUploadService memoryBlobs)
        {
            memoryBlobs.Reset();
        }

        if (Services.GetService<IForumWriteRepository>() is InMemoryForumWriteRepository forum)
        {
            forum.Clear();
        }

        var publicQueries = Services.GetService<PublicQueryCacheService>();
        publicQueries?.InvalidateTriviaCache();
        publicQueries?.InvalidateQuotesCache();
        publicQueries?.InvalidateNewsCache();
        publicQueries?.InvalidateArticlesCache();
        publicQueries?.InvalidateBiographyCache();
        publicQueries?.InvalidateHistoryCache();
        publicQueries?.InvalidateFanPerformanceCache();
        publicQueries?.InvalidatePhotoCache();
        publicQueries?.InvalidateDiscographyCache();
        publicQueries?.InvalidateForumStatsCache();
        if (Services.GetService<IOutputCacheStore>() is not { } outputCache)
        {
            return;
        }

        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicSitemapTag, CancellationToken.None);
    }
}

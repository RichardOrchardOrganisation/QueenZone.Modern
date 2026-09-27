using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
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

    /// <summary>
    /// Creates a factory that applies additional DI configuration on top of Testing defaults.
    /// </summary>
    public static QueenZoneWebApplicationFactory WithServices(Action<IServiceCollection> configureServices) =>
        new ConfiguredFactory(configureServices);

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
        var publicQueries = Services.GetService<PublicQueryCacheService>();
        publicQueries?.InvalidateTriviaCache();
        publicQueries?.InvalidateQuotesCache();
        publicQueries?.InvalidateNewsCache();
        publicQueries?.InvalidateArticlesCache();
        publicQueries?.InvalidateBiographyCache();
        publicQueries?.InvalidateHistoryCache();
        if (Services.GetService<IOutputCacheStore>() is not { } outputCache)
        {
            return;
        }

        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicSitemapTag, CancellationToken.None);
    }

    private sealed class ConfiguredFactory(Action<IServiceCollection> configureServices) : QueenZoneWebApplicationFactory
    {
        protected override void ConfigureTestServices(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}

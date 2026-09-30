using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// The only shared xUnit collection in Web.Tests: one Production-environment host for
/// output-cache, compression, static-asset, and mobile-auth startup cases.
/// Classes in this collection run serially with each other and in parallel with everything else.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProductionHostCollection : ICollectionFixture<ProductionHostFixture>
{
    public const string Name = "Production host";
}

public sealed class ProductionHostFixture : IAsyncLifetime, IResettableHostFixture
{
    private readonly ProductionCountingWebApplicationFactory factory = new();
    private readonly WebHostVariantCache variants = new();

    public ProductionWebApplicationFactory Factory => factory;

    public CountingArticlesRepository Articles => factory.Articles;

    public VariantWebApplicationFactory WithoutMobileAuthSigningKey =>
        variants.Get(WebHostVariants.ProductionWithoutMobileAuthSigningKey);

    public async Task ResetAsync()
    {
        // Accessing Services starts the host (and SearchIndexSeedHostedService) before
        // eviction so later counter resets are not immediately dirtied by startup reads.
        var outputCache = factory.Services.GetRequiredService<IOutputCacheStore>();
        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicOutputCachePolicies.PublicSitemapTag, CancellationToken.None);
        Articles.Reset();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await variants.DisposeAsync();
    }
}

internal sealed class ProductionCountingWebApplicationFactory : ProductionWebApplicationFactory
{
    public CountingArticlesRepository Articles { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IArticlesRepository>();
            services.AddSingleton<IArticlesRepository>(Articles);
            services.RemoveAll<IArticleRepository>();
            services.AddSingleton<IArticleRepository>(new CacheVariantArticleRepository());
        });
    }
}

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

        return lazy.Value;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(variant.Environment);
        if (string.Equals(variant.Environment, "Development", StringComparison.Ordinal))
        {
            builder.UseSetting(QueenZoneDevelopmentHost.SkipLocalSettingsKey, "true");
        }

        if (variant.Settings.Count > 0)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(variant.Settings));
        }

        builder.ConfigureTestServices(services => WebHostVariants.Apply(variant.Services, services, context));
    }

    public Task ResetAsync()
    {
        context.Reset();
        if (Services.GetService<IEditorialArticleRepository>() is InMemoryEditorialArticleRepository editorial)
        {
            editorial.Clear();
        }

        Services.GetService<SharedSearchIndexStore>()?.Clear();
        return Task.CompletedTask;
    }
}

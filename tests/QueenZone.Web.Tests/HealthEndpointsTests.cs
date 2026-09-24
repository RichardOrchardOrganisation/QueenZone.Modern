using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web.Health;

namespace QueenZone.Web.Tests;

public sealed class HealthEndpointsTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public HealthEndpointsTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Liveness_health_returns_ok_without_dependency_details()
    {
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readiness_health_is_healthy_for_in_memory_sample_data()
    {
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());

        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal("Healthy", entries.GetProperty("sql").GetProperty("status").GetString());
        Assert.Equal("Healthy", entries.GetProperty("blob").GetProperty("status").GetString());
        Assert.Contains(
            "not configured",
            entries.GetProperty("sql").GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "not configured",
            entries.GetProperty("blob").GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);

        // No connection-string shaped secrets in the payload.
        var raw = doc.RootElement.GetRawText();
        Assert.DoesNotContain("Password=", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey=", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warmup_returns_ok_for_in_memory_sample_data()
    {
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/warmup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warmup_failure_returns_minimal_unhealthy_response()
    {
        var appFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INewsRepository>();
                services.AddSingleton<INewsRepository>(new ThrowingNewsRepository());
            }));
        var client = appFactory.CreateClient();

        using var response = await client.GetAsync("/warmup");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated repository failure", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latest-news", body, StringComparison.OrdinalIgnoreCase);
    }

    // Regression for #666: the middleware pipeline ran authentication, authorization,
    // rate limiting, output caching, and antiforgery before dispatching to probe paths,
    // so a slow/hung default authentication handler on a cold container blocked the
    // platform's own startup gate regardless of which probe path it targeted. Program.cs
    // now wraps that whole chain in a UseWhen branch that probe paths skip; the branch
    // stamps a diagnostic header as its first step, so its absence proves the branch —
    // and everything in it, including authentication — never ran for these paths.
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/warmup")]
    public async Task Probe_paths_bypass_the_authenticated_pipeline(string path)
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.False(response.Headers.Contains("X-QueenZone-Pipeline"));
    }

    [Fact]
    public async Task Non_probe_paths_still_run_the_authenticated_pipeline()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.True(response.Headers.TryGetValues("X-QueenZone-Pipeline", out var values));
        Assert.Equal("full", Assert.Single(values!));
    }

    [Fact]
    public async Task Liveness_probe_bypasses_host_filter_for_azure_internal_host()
    {
        using var strictFactory = CreateStrictHostFactory();
        var client = strictFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "169.254.130.4:8080";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_probe_path_rejects_azure_internal_host()
    {
        using var strictFactory = CreateStrictHostFactory();
        var client = strictFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/news");
        request.Headers.Host = "169.254.130.4:8080";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-QueenZone-Pipeline"));
    }

    [Fact]
    public async Task Non_probe_path_accepts_configured_wildcard_host()
    {
        using var strictFactory = CreateStrictHostFactory();
        var client = strictFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/news");
        request.Headers.Host = "queenzone-dev.azurewebsites.net";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("www.queenzone.org", true)]
    [InlineData("WWW.QUEENZONE.ORG", true)]
    [InlineData("queenzone-dev.azurewebsites.net", true)]
    [InlineData("queenzone-dev.azurewebsites.net.", true)]
    [InlineData("azurewebsites.net", false)]
    [InlineData("evilazurewebsites.net", false)]
    [InlineData("169.254.130.4", false)]
    public void Host_filter_matches_exact_and_wildcard_hosts_without_suffix_bypass(string host, bool expected)
    {
        string[] allowedHosts = ["www.queenzone.org", "queenzone.org", "*.azurewebsites.net"];

        Assert.Equal(expected, QueenZoneHostFilteringMiddleware.IsAllowed(host, allowedHosts));
    }

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/warmup", true)]
    [InlineData("/", false)]
    [InlineData("/news", false)]
    public void IsProbePath_identifies_infrastructure_probe_paths(string path, bool expected)
    {
        Assert.Equal(expected, QueenZoneHealthEndpoints.IsProbePath(path));
    }

    [Fact]
    public async Task SqlReadyHealthCheck_without_dbcontext_is_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var check = new SqlReadyHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("not configured", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlReadyHealthCheck_timeout_returns_unhealthy_without_waiting_for_retry_policy()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromMilliseconds(50),
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            });

        var started = Stopwatch.StartNew();
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        started.Stop();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("timed out", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            started.Elapsed < TimeSpan.FromSeconds(2),
            $"SQL check took {started.Elapsed} instead of failing at the 50ms bound.");
    }

    [Fact]
    public async Task SqlReadyHealthCheck_default_connect_failure_is_unhealthy()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("SQL check failed.", result.Description);
    }

    [Fact]
    public async Task SqlReadyHealthCheck_reachable_database_is_healthy()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromSeconds(1),
            (_, _) => Task.FromResult(true));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("reachable", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlReadyHealthCheck_unreachable_database_is_unhealthy()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromSeconds(1),
            (_, _) => Task.FromResult(false));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("cannot connect", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlReadyHealthCheck_failure_does_not_leak_exception_text()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromSeconds(1),
            (_, _) => throw new InvalidOperationException("Password=supersecret;Server=tcp:example"));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("SQL check failed.", result.Description);
        Assert.DoesNotContain("Password=", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supersecret", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlReadyHealthCheck_request_cancellation_is_propagated()
    {
        await using var provider = CreateSqlCheckProvider();
        var check = new SqlReadyHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromSeconds(5),
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            });

        using var timeout = new CancellationTokenSource();
        timeout.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            check.CheckHealthAsync(new HealthCheckContext(), timeout.Token));
    }

    private static ServiceProvider CreateSqlCheckProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().Options));
        return services.BuildServiceProvider();
    }

    private WebApplicationFactory<Program> CreateStrictHostFactory() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QueenZoneHostFiltering:AllowedHosts"] =
                    "www.queenzone.org;queenzone.org;*.azurewebsites.net",
            })));

    [Fact]
    public async Task BlobReadyHealthCheck_with_null_service_is_healthy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBlobUploadService, NullBlobUploadService>();
        await using var provider = services.BuildServiceProvider();
        var check = new BlobReadyHealthCheck(
            provider.GetRequiredService<IBlobUploadService>(),
            provider);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("not configured", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BlobReadyHealthCheck_failure_is_unhealthy_and_logged()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBlobUploadService>(new StubConfiguredBlobUploadService());
        var clientOptions = new Azure.Storage.Blobs.BlobClientOptions();
        clientOptions.Retry.MaxRetries = 0;
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(5);
        services.AddSingleton(new Azure.Storage.Blobs.BlobServiceClient(new Uri("https://127.0.0.1:1/"), clientOptions));
        await using var provider = services.BuildServiceProvider();
        var logger = new CollectingLogger<BlobReadyHealthCheck>();
        var check = new BlobReadyHealthCheck(
            provider.GetRequiredService<IBlobUploadService>(),
            provider,
            logger);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), timeout.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Blob storage check failed.", result.Description);
        Assert.Contains(logger.Entries, entry => entry.Exception is not null);
    }

    private sealed class StubConfiguredBlobUploadService : IBlobUploadService
    {
        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingNewsRepository : INewsRepository
    {
        public Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated repository failure");

        public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
            int page,
            int pageSize,
            NewsArchiveFilter filter = default,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);

        public Task<int> GetPublishedCountAsync(NewsArchiveFilter filter = default, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NewsArchiveYearRange(null, null));

        public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<NewsItem?>(null);

        public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitemapContentEntry>>([]);

        public Task<NewsSearchPage> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NewsSearchPage([], 0, page, pageSize));
    }
}

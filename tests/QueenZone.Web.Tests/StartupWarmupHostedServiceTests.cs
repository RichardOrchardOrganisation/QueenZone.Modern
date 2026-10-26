using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Tests;

public sealed class StartupWarmupHostedServiceTests
{
    [Theory]
    [InlineData("http://[::]:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://+:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://*:5000", "http://127.0.0.1:5000/")]
    [InlineData("http://0.0.0.0:80", "http://127.0.0.1/")]
    [InlineData("http://localhost:5100", "http://localhost:5100/")]
    public void ResolveLoopbackAddress_maps_wildcard_hosts_to_loopback(string address, string expected) =>
        Assert.Equal(new Uri(expected), StartupWarmupHostedService.ResolveLoopbackAddress([address]));

    [Fact]
    public void ResolveLoopbackAddress_skips_https_unbound_ports_and_garbage()
    {
        Assert.Null(StartupWarmupHostedService.ResolveLoopbackAddress([]));
        Assert.Null(StartupWarmupHostedService.ResolveLoopbackAddress(
            ["https://localhost:5001", "http://127.0.0.1:0", "not a url"]));
        Assert.Equal(
            new Uri("http://127.0.0.1:8080/"),
            StartupWarmupHostedService.ResolveLoopbackAddress(["https://localhost:5001", "http://*:8080"]));
    }

    [Theory]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, false)]
    [InlineData("Development", true, false)]
    [InlineData("Testing", true, false)]
    public async Task StartAsync_schedules_the_warmup_only_for_enabled_production(
        string environmentName,
        bool enabled,
        bool expectScheduled)
    {
        var lifetime = new FakeLifetime();
        var service = CreateService(lifetime, environmentName, enabled, new RecordingHandler());

        await service.StartAsync(CancellationToken.None);
        lifetime.Start();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(expectScheduled, service.CacheWarms == 1);
    }

    [Fact]
    public async Task RunAsync_primes_caches_then_renders_each_path_as_the_public_host_over_loopback()
    {
        var handler = new RecordingHandler();
        var service = CreateService(new FakeLifetime(), "Production", true, handler, paths: ["/", "/news"]);

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, service.CacheWarms);
        Assert.Equal(["/", "/news"], handler.Requests.Select(request => request.RequestUri!.AbsolutePath));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal(8080, request.RequestUri.Port);
            Assert.Equal("www.example.test", request.Headers.Host);
            Assert.Equal("https", Assert.Single(request.Headers.GetValues("X-Forwarded-Proto")));
        });
    }

    [Fact]
    public async Task RunAsync_keeps_going_when_a_page_fails_or_the_cache_warm_throws()
    {
        var handler = new RecordingHandler { FailPath = "/forum" };
        var service = CreateService(
            new FakeLifetime(), "Production", true, handler, paths: ["/", "/forum", "/news"]);
        service.ThrowOnCacheWarm = true;

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(["/", "/forum", "/news"], handler.Requests.Select(request => request.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task RunAsync_skips_page_renders_when_the_server_has_no_loopback_address()
    {
        var handler = new RecordingHandler();
        var service = CreateService(new FakeLifetime(), "Production", true, handler, addresses: []);

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, service.CacheWarms);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RunAsync_stops_quietly_when_shutdown_is_requested()
    {
        var handler = new RecordingHandler();
        var service = CreateService(new FakeLifetime(), "Production", true, handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await service.RunAsync(cancelled.Token);

        Assert.Empty(handler.Requests);
    }

    private static TestableWarmup CreateService(
        FakeLifetime lifetime,
        string environmentName,
        bool enabled,
        RecordingHandler handler,
        string[]? paths = null,
        string[]? addresses = null) =>
        new(
            lifetime,
            new FakeEnvironment(environmentName),
            new FakeServer(addresses ?? ["http://[::]:8080"]),
            Options.Create(new StartupWarmupOptions { Enabled = enabled, Paths = paths ?? ["/"] }),
            Options.Create(new SiteOptions { PublicBaseUrl = "https://www.example.test" }),
            handler);

    private sealed class TestableWarmup(
        FakeLifetime lifetime,
        IHostEnvironment environment,
        IServer server,
        IOptions<StartupWarmupOptions> options,
        IOptions<SiteOptions> siteOptions,
        HttpMessageHandler handler)
        : StartupWarmupHostedService(
            lifetime,
            environment,
            server,
            null!,
            options,
            siteOptions,
            NullLogger<StartupWarmupHostedService>.Instance,
            handler)
    {
        public int CacheWarms { get; private set; }

        public bool ThrowOnCacheWarm { get; set; }

        protected override Task PrimeCachesAsync(CancellationToken cancellationToken)
        {
            CacheWarms++;
            return ThrowOnCacheWarm
                ? Task.FromException(new InvalidOperationException("cache warm failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public string? FailPath { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return request.RequestUri!.AbsolutePath == FailPath
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("boom"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource started = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => started.Cancel();

        public void StopApplication()
        {
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "QueenZone.Web";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FakeServer : IServer
    {
        public FakeServer(IEnumerable<string> addresses)
        {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses)
            {
                feature.Addresses.Add(address);
            }

            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }

        private sealed class ServerAddressesFeature : IServerAddressesFeature
        {
            public ICollection<string> Addresses { get; } = new List<string>();

            public bool PreferHostingUrls { get; set; }
        }
    }
}

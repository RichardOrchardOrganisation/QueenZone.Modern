using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class AdminSearchIndexTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    // Failure-only guard; nothing relies on it elapsing.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly WebApplicationFactory<Program> factory;

    public AdminSearchIndexTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AdminSearchIndexPage_RendersDocumentCounts()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var body = await client.GetStringAsync("/admin/search");

        Assert.Contains("Search index", body);
        Assert.Contains("documents indexed", body);
        Assert.Contains("background", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminSearchIndexPage_StartsReindexInBackgroundOnPost()
    {
        // Dedicated host so this test owns the in-process job singleton.
        await using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var client = AdminHttpTestHelpers.CreateClient(host, AdminHttpTestHelpers.AdminEmail);

        var response = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/search",
            "/admin/search?handler=Reindex",
            []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var body = await client.GetStringAsync("/admin/search");
        // Flash may say "started" or, if the in-memory rebuild already finished, the job banner shows success.
        Assert.True(
            body.Contains("Reindex started in the background", StringComparison.Ordinal)
            || body.Contains("Search index rebuilt.", StringComparison.Ordinal)
            || body.Contains("A reindex is already in progress.", StringComparison.Ordinal),
            "Expected a reindex start, completion, or already-running message.");

        await host.Services.GetRequiredService<SearchReindexJobService>().WaitForCurrentRunAsync();
        var status = await GetStatusAsync(client);
        Assert.False(status.GetProperty("isRunning").GetBoolean());
        Assert.Equal("Succeeded", status.GetProperty("phase").GetString());
        Assert.True(status.GetProperty("totalCount").GetInt32() > 0);
    }

    [Fact]
    public async Task AdminSearchIndexPage_StatusEndpointReturnsJson()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await client.GetAsync("/admin/search?handler=Status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.TryGetProperty("phase", out _));
        Assert.True(status.TryGetProperty("totalCount", out _));
        Assert.True(status.TryGetProperty("contentTypeCounts", out _));
        Assert.True(status.TryGetProperty("isRunning", out _));
    }

    [Fact]
    public async Task SearchReindexJobService_RejectsConcurrentStart()
    {
        var gate = new GatedSearchIndexService();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchIndexService>();
                services.AddSingleton<ISearchIndexService>(sp =>
                {
                    gate.Inner = new InMemorySearchIndexService(sp.GetRequiredService<SharedSearchIndexStore>());
                    return gate;
                });
            });
        });
        // Force host construction so the singleton job service is available.
        _ = AdminHttpTestHelpers.CreateClient(host, AdminHttpTestHelpers.AdminEmail);
        var jobService = host.Services.GetRequiredService<SearchReindexJobService>();

        gate.Arm();
        Assert.True(jobService.TryStart());

        // The first ReplaceContentTypeAsync is parked on the gate, so the job is provably still running.
        await gate.FirstReplaceStarted.WaitAsync(HangGuard);
        Assert.Equal(SearchReindexJobPhase.Running, jobService.GetSnapshot().Phase);
        Assert.False(jobService.TryStart());

        gate.Release();
        await jobService.WaitForCurrentRunAsync();
        Assert.Equal(SearchReindexJobPhase.Succeeded, jobService.GetSnapshot().Phase);

        // After completion a new run is allowed (the gate stays open for later calls).
        Assert.True(jobService.TryStart());
        await jobService.WaitForCurrentRunAsync();
        Assert.Equal(SearchReindexJobPhase.Succeeded, jobService.GetSnapshot().Phase);
    }

    [Fact]
    public async Task AdminSearchIndexPage_RequiresAdminAuthentication()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory);

        var response = await client.GetAsync("/admin/search");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminSearchIndexStatus_RequiresAdminAuthentication()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory);

        var response = await client.GetAsync("/admin/search?handler=Status");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/admin/search?handler=Status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Once armed, parks the first <see cref="ReplaceContentTypeAsync"/> call until <see cref="Release"/> so a
    /// test can observe the job mid-run. Passes straight through before arming (the startup seed runs first)
    /// and after the first parked call.
    /// </summary>
    private sealed class GatedSearchIndexService : ISearchIndexService
    {
        private readonly TaskCompletionSource firstReplaceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool armed;

        public ISearchIndexService Inner { get; set; } = null!;

        public void Arm() => armed = true;

        public Task FirstReplaceStarted => firstReplaceStarted.Task;

        public void Release() => released.TrySetResult();

        public async Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default)
        {
            if (armed)
            {
                firstReplaceStarted.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }

            await Inner.ReplaceContentTypeAsync(contentType, documents, cancellationToken);
        }

        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Inner.UpsertAsync(document, cancellationToken);

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Inner.RemoveAsync(sourceKey, cancellationToken);

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default) =>
            Inner.GetContentTypeCountsAsync(cancellationToken);
    }
}

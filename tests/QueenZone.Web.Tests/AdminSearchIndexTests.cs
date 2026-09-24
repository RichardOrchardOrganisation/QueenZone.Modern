using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class AdminSearchIndexTests : IClassFixture<QueenZoneWebApplicationFactory>
{
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

        var status = await WaitForReindexIdleAsync(client);
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
        await using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        // Force host construction so the singleton job service is available.
        _ = AdminHttpTestHelpers.CreateClient(host, AdminHttpTestHelpers.AdminEmail);
        var jobService = host.Services.GetRequiredService<SearchReindexJobService>();

        await WaitForJobIdleAsync(jobService);

        var first = jobService.TryStart();
        Assert.True(first);

        // In-memory reindex is fast; only assert rejection while the first job is still running.
        var second = jobService.TryStart();
        if (jobService.GetSnapshot().Phase == SearchReindexJobPhase.Running)
        {
            Assert.False(second);
        }

        await WaitForJobIdleAsync(jobService);
        Assert.Equal(SearchReindexJobPhase.Succeeded, jobService.GetSnapshot().Phase);

        // After completion a new run is allowed.
        Assert.True(jobService.TryStart());
        await WaitForJobIdleAsync(jobService);
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

    private static async Task<JsonElement> WaitForReindexIdleAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetAsync("/admin/search?handler=Status");
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!status.GetProperty("isRunning").GetBoolean())
            {
                return status;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for search reindex job to finish.");
    }

    private static async Task WaitForJobIdleAsync(SearchReindexJobService jobService)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (jobService.GetSnapshot().Phase != SearchReindexJobPhase.Running)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for SearchReindexJobService to become idle.");
    }
}

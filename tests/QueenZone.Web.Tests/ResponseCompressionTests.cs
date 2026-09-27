using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(ProductionHostCollection.Name)]
public sealed class ResponseCompressionTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> productionFactory;
    private readonly WebApplicationFactory<Program> testingFactory;

    public ResponseCompressionTests(
        ProductionHostFixture production,
        QueenZoneWebApplicationFactory testingFactory)
    {
        productionFactory = production.Factory;
        this.testingFactory = testingFactory;
    }

    [Fact]
    public async Task HtmlResponse_IsBrotliCompressedInProduction()
    {
        using var response = await SendProductionRequestAsync("/news", "br");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", GetContentEncoding(response));
    }

    [Fact]
    public async Task CssResponse_IsGzipCompressedInProduction()
    {
        using var response = await SendProductionRequestAsync("/css/site.css", "gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gzip", GetContentEncoding(response));
    }

    [Fact]
    public async Task JavaScriptResponse_IsBrotliCompressedInProduction()
    {
        using var response = await SendProductionRequestAsync("/js/site.js", "br");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", GetContentEncoding(response));
    }

    [Fact]
    public async Task HtmlResponse_IsNotCompressedInTesting()
    {
        using var response = await SendTestingRequestAsync("/news", "br, gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(GetContentEncoding(response));
    }

    [Fact]
    public async Task CompressedHtmlResponse_StillIncludesSecurityHeaders()
    {
        using var response = await SendProductionRequestAsync("/news", "br");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
        Assert.Equal(SecurityHeaders.PermissionsPolicy, response.Headers.GetValues("Permissions-Policy").First());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task HealthEndpoint_RemainsAvailableWhenCompressionEnabled()
    {
        var client = productionFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "br, gzip");

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    [Fact]
    public async Task Production_health_probe_accepts_azure_internal_host_before_explicit_filter()
    {
        var client = new HttpClient(productionFactory.Server.CreateHandler())
        {
            BaseAddress = new Uri("https://localhost"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "169.254.130.4:8080";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private Task<HttpResponseMessage> SendProductionRequestAsync(string path, string acceptEncoding) =>
        SendRequestAsync(productionFactory, path, acceptEncoding);

    private Task<HttpResponseMessage> SendTestingRequestAsync(string path, string acceptEncoding) =>
        SendRequestAsync(testingFactory, path, acceptEncoding);

    private static async Task<HttpResponseMessage> SendRequestAsync(
        WebApplicationFactory<Program> factory,
        string path,
        string acceptEncoding)
    {
        var client = new HttpClient(factory.Server.CreateHandler())
        {
            BaseAddress = new Uri("https://localhost"),
        };

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
    }

    private static string? GetContentEncoding(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentEncoding.Count > 0)
        {
            return response.Content.Headers.ContentEncoding.First();
        }

        return response.Headers.TryGetValues("Content-Encoding", out var values)
            ? values.FirstOrDefault()
            : null;
    }
}

public sealed class ResponseCompressionBootstrapTests
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public void IsEnabled_matchesEnvironment(string environmentName, bool expected)
    {
        var environment = new TestHostEnvironment { EnvironmentName = environmentName };

        Assert.Equal(expected, ResponseCompressionBootstrap.IsEnabled(environment));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

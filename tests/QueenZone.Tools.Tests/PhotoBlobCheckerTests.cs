using System.Net;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class ToolsLocalSettingsTests
{
    [Fact]
    public void TryLoad_ReadsConnectionStringsFromExplicitPath()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "ConnectionStrings": {
                "QueenZoneLegacyLive": "Server=live;",
                "BlobStorage": "UseDevelopmentStorage=true",
              },
            }
            """);

        try
        {
            var settings = ToolsLocalSettings.TryLoad(settingsPath);

            Assert.NotNull(settings);
            Assert.Equal("Server=live;", settings.QueenZoneLegacyLive);
            Assert.Equal("UseDevelopmentStorage=true", settings.BlobStorage);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void TryLoad_ReturnsNull_WhenConnectionStringsSectionMissing()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, """{ "Logging": { "LogLevel": { "Default": "Information" } } }""");

        try
        {
            Assert.Null(ToolsLocalSettings.TryLoad(settingsPath));
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }
}

public sealed class HttpPhotoBlobCheckerTests
{
    [Fact]
    public async Task CheckAsync_ReturnsSuccessStatus_WhenHeadRequestSucceeds()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var checker = new HttpPhotoBlobChecker(new HttpClient(handler));

        var result = await checker.CheckAsync("https://queenzoneprod.blob.core.windows.net/queen/a.jpg", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.Equal("200", result.Status);
        Assert.Equal(HttpMethod.Head, handler.LastMethod);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNotFoundStatus_WhenHeadRequestFails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var checker = new HttpPhotoBlobChecker(new HttpClient(handler));

        var result = await checker.CheckAsync("https://queenzoneprod.blob.core.windows.net/queen/missing.jpg", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Equal("404", result.Status);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            return Task.FromResult(responder(request));
        }
    }
}

public sealed class AzureBlobPhotoCheckerTests
{
    [Fact]
    public async Task CheckAsync_ReturnsInvalidUrl_WhenBlobLocationCannotBeParsed()
    {
        var checker = new AzureBlobPhotoChecker("UseDevelopmentStorage=true");

        var result = await checker.CheckAsync("not-a-valid-blob-url", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Equal("invalid-url", result.Status);
    }
}

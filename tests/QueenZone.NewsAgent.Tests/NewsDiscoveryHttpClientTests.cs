using System.Net;
using System.Text;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsDiscoveryHttpClientTests
{
    [Fact]
    public async Task GetAsync_reads_the_response_body()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Queen news\nline two", Encoding.UTF8, "text/plain"),
        });
        using var http = new HttpClient(handler);
        var client = new NewsDiscoveryHttpClient(http);

        var response = await client.GetAsync("https://example.com/queen-feed");

        Assert.Equal("https://example.com/queen-feed", response.FinalUrl);
        Assert.Contains("text/plain", response.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Queen news\nline two", response.Body);
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class SsrfSafeSocketsHttpHandlerTests
{
    [Fact]
    public void Create_enables_up_to_five_redirects()
    {
        using var handler = SsrfSafeSocketsHttpHandler.Create();

        Assert.True(handler.AllowAutoRedirect);
        Assert.Equal(5, handler.MaxAutomaticRedirections);
    }

    [Fact]
    public async Task Redirect_settings_follow_301_to_rss_feed()
    {
        using var settings = SsrfSafeSocketsHttpHandler.Create();
        // Exercise real redirects without the production callback that blocks loopback.
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = settings.AllowAutoRedirect,
            MaxAutomaticRedirections = settings.MaxAutomaticRedirections,
            ConnectTimeout = settings.ConnectTimeout,
            UseProxy = false,
        };
        using var client = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        const string rss = "<rss version=\"2.0\"><channel><title>Queen news</title></channel></rss>";

        var server = ServeAsync();
        try
        {
            using var response = await client.GetAsync(new Uri(origin, "/old-feed"), timeout.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new Uri(origin, "/feed.xml"), response.RequestMessage!.RequestUri);
            Assert.Equal(rss, await response.Content.ReadAsStringAsync(timeout.Token));
            await server;
        }
        finally
        {
            await timeout.CancelAsync();
            listener.Stop();
        }

        async Task ServeAsync()
        {
            foreach (var path in new[] { "/old-feed", "/feed.xml" })
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                Assert.Equal($"GET {path} HTTP/1.1", await reader.ReadLineAsync(timeout.Token));
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token)))
                {
                }

                var reply = path == "/old-feed"
                    ? "HTTP/1.1 301 Moved Permanently\r\nLocation: /feed.xml\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                    : $"HTTP/1.1 200 OK\r\nContent-Type: application/rss+xml\r\nContent-Length: {Encoding.UTF8.GetByteCount(rss)}\r\nConnection: close\r\n\r\n{rss}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), timeout.Token);
            }
        }
    }
}

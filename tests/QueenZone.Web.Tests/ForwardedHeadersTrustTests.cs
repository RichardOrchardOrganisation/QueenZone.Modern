using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace QueenZone.Web.Tests;

public sealed class ForwardedHeadersTrustTests
{
    private const string CloudflareEdge = "173.245.48.10";
    private const string Visitor = "203.0.113.10";

    [Fact]
    public async Task Untrusted_public_peer_cannot_spoof_client_ip_scheme_or_host()
    {
        var seen = await SendAsync(
            peer: "198.51.100.7",
            ("X-Forwarded-For", "1.2.3.4"),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", "www.queenzone.org"));

        Assert.Equal("198.51.100.7", seen.RemoteIp);
        Assert.Equal("http", seen.Scheme);
        Assert.Equal("origin.internal", seen.Host);
    }

    [Fact]
    public async Task Platform_peer_gets_visitor_ip_and_scheme_but_not_forwarded_host()
    {
        var seen = await SendAsync(
            peer: "169.254.129.1",
            ("X-Forwarded-For", Visitor),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", "evil.example"));

        Assert.Equal(Visitor, seen.RemoteIp);
        Assert.Equal("https", seen.Scheme);
        Assert.Equal("origin.internal", seen.Host);
    }

    [Theory]
    [InlineData(CloudflareEdge)]
    [InlineData("2606:4700::1")]
    public async Task Cloudflare_edge_peer_is_trusted(string edgePeer)
    {
        var seen = await SendAsync(peer: edgePeer, ("X-Forwarded-For", Visitor));

        Assert.Equal(Visitor, seen.RemoteIp);
    }

    [Fact]
    public async Task Two_hop_chain_resolves_visitor_not_cloudflare_edge()
    {
        // Cloudflare -> App Service front end -> container: visitor, edge.
        var seen = await SendAsync(
            peer: "10.0.0.4",
            ("X-Forwarded-For", $"{Visitor}, {CloudflareEdge}"));

        Assert.Equal(Visitor, seen.RemoteIp);
    }

    [Fact]
    public async Task Visitor_prepended_chain_cannot_forge_a_deeper_address()
    {
        // The visitor sent "1.2.3.4" themselves; Cloudflare appended the real address.
        var seen = await SendAsync(
            peer: "10.0.0.4",
            ("X-Forwarded-For", $"1.2.3.4, {Visitor}, {CloudflareEdge}"));

        Assert.Equal(Visitor, seen.RemoteIp);
    }

    [Fact]
    public void Embedded_cloudflare_list_is_single_cidrs()
    {
        var ranges = QueenZoneForwardedHeaders.LoadCloudflareRanges();

        Assert.NotEmpty(ranges);
        Assert.All(ranges, range =>
        {
            Assert.DoesNotContain(',', range);
            Assert.True(System.Net.IPNetwork.TryParse(range, out _), range);
        });
    }

    private static async Task<(string? RemoteIp, string Scheme, string Host)> SendAsync(
        string peer,
        params (string Name, string Value)[] headers)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                        return next(context);
                    });
                    app.UseForwardedHeaders(QueenZoneForwardedHeaders.CreateOptions());
                    app.Run(context => context.Response.WriteAsync(
                        $"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}|{context.Request.Host}"));
                }))
            .StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "origin.internal";
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await host.GetTestClient().SendAsync(request);
        var parts = (await response.Content.ReadAsStringAsync()).Split('|');
        return (parts[0], parts[1], parts[2]);
    }
}

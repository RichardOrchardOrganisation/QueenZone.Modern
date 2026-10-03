using System.Net;
using System.Net.Sockets;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class OutboundUrlSafetyTests
{
    [Theory]
    [InlineData("https://www.queenonline.com/news/tour")]
    [InlineData("http://example.com/queen")]
    public void TryValidatePublicHttpUrl_accepts_safe_public_urls(string url)
    {
        var ok = OutboundUrlSafety.TryValidatePublicHttpUrl(url, out var error, out var normalized);

        Assert.True(ok, error);
        Assert.False(string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:pass@example.com/secret")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1/meta")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://192.168.1.10/news")]
    [InlineData("http://10.0.0.5/news")]
    [InlineData("http://[::1]/")]
    public void TryValidatePublicHttpUrl_rejects_unsafe_urls(string url)
    {
        var ok = OutboundUrlSafety.TryValidatePublicHttpUrl(url, out var error, out var normalized);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.255.255.255")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.0")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.0")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::a9fe:a9fe")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("64:ff9b:1::a9fe:a9fe")]
    [InlineData("2002:a9fe:a9fe::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("febf:ffff::1")]
    [InlineData("fec0::1")]
    [InlineData("feff:ffff::1")]
    [InlineData("ff02::1")]
    public void IsBlockedAddress_BlocksSpecialPurposeAndTransitionAddresses(string address)
    {
        Assert.True(OutboundUrlSafety.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("223.255.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsBlockedAddress_KeepsPublicControlsAndRangeBoundariesAllowed(string address)
    {
        Assert.False(OutboundUrlSafety.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsBlockedAddress_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => OutboundUrlSafety.IsBlockedAddress(null!));
    }

    [Theory]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/")]
    [InlineData("http://[64:ff9b:1::a00:1]/")]
    [InlineData("http://[2002:a9fe:a9fe::1]/")]
    [InlineData("http://2852039166/")]
    [InlineData("http://0xA9FEA9FE/")]
    [InlineData("http://0251.0376.0251.0376/")]
    [InlineData("http://169.254.43518/")]
    [InlineData("http://127.1/")]
    [InlineData("http://0/")]
    [InlineData("http://224.0.0.1/")]
    public void TryValidatePublicHttpUrl_RejectsSpecialPurposeAndEncodedLiteralHosts(string url)
    {
        Assert.False(OutboundUrlSafety.TryValidatePublicHttpUrl(url, out var error, out var normalized));
        Assert.NotEmpty(error);
        Assert.Null(normalized);
    }

    [Fact]
    public async Task ResolveAllowedEndPointAsync_RejectsLiteralNat64BeforeConnecting()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeSocketsHttpHandler.ResolveAllowedEndPointAsync(
            new DnsEndPoint("64:ff9b::a9fe:a9fe", 80), CancellationToken.None));
    }

    [Fact]
    public void IsAllowedTextContentType_accepts_html_and_rejects_binary()
    {
        Assert.True(OutboundUrlSafety.IsAllowedTextContentType("text/html; charset=utf-8"));
        Assert.False(OutboundUrlSafety.IsAllowedTextContentType("application/octet-stream"));
        Assert.False(OutboundUrlSafety.IsAllowedTextContentType("image/png"));
    }
}

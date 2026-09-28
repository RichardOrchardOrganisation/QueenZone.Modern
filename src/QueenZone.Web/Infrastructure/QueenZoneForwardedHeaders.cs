using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;

namespace QueenZone.Web;

/// <summary>
/// Forwarded-header trust for the production ingress (#1654). Client IP and scheme are applied
/// only when the immediate peer is the platform front end or Cloudflare, so a caller that reaches
/// Kestrel directly cannot spoof <c>RemoteIpAddress</c> (the rate-limit partition key).
/// <c>X-Forwarded-Host</c> is deliberately not honored: public URLs come from
/// <c>Site:PublicBaseUrl</c> and the raw Host header is validated by host filtering.
/// </summary>
internal static class QueenZoneForwardedHeaders
{
    internal const string CloudflareRangesResource = "QueenZone.Web.cloudflare-ip-ranges.json";

    // The App Service front end connects from loopback, private, or link-local addresses.
    // Its range is not published as a stable list, so trust the non-routable space rather than
    // guessing at Azure ranges. Public peers stay untrusted unless they are Cloudflare.
    private static readonly string[] PlatformProxyNetworks =
    [
        "127.0.0.0/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "169.254.0.0/16",
        "fc00::/7",
        "fe80::/10",
    ];

    public static ForwardedHeadersOptions CreateOptions() =>
        CreateOptions(LoadCloudflareRanges());

    internal static ForwardedHeadersOptions CreateOptions(IEnumerable<string> cloudflareRanges)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Unlimited: the chain is "visitor, cloudflare-edge[, platform]" and each hop that
            // is a known network is stripped until the first untrusted address, the visitor.
            // The default limit of 1 would leave the Cloudflare edge as the client IP and
            // collapse rate-limit partitions onto a few POPs.
            ForwardLimit = null,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var range in PlatformProxyNetworks.Concat(cloudflareRanges))
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(range));
        }

        return options;
    }

    internal static IReadOnlyList<string> LoadCloudflareRanges()
    {
        using var stream = typeof(QueenZoneForwardedHeaders).Assembly.GetManifestResourceStream(CloudflareRangesResource)
            ?? throw new InvalidOperationException($"Embedded resource {CloudflareRangesResource} is missing.");
        using var document = JsonDocument.Parse(stream);
        var ranges = new List<string>();
        foreach (var family in new[] { "ipv4", "ipv6" })
        {
            ranges.AddRange(document.RootElement.GetProperty(family)
                .EnumerateArray()
                .Select(element => element.GetString()!));
        }

        return ranges;
    }
}

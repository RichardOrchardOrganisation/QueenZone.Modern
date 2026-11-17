using System.Globalization;
using System.Net;
using System.Net.Sockets;
using CsCheck;
using QueenZone.NewsAgent;
using QueenZone.TestUtilities;

namespace QueenZone.NewsAgent.Tests;

public sealed class OutboundUrlSafetyPropertyTests
{
    // IANA special-purpose registries plus multicast/reserved space; independent of production's network list.
    private static readonly string[] BlockedCidrs =
    [
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8",
        "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24",
        "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24",
        "224.0.0.0/4", "240.0.0.0/4", "::/96", "64:ff9b::/96",
        "64:ff9b:1::/48", "2002::/16", "fc00::/7", "fe80::/10", "fec0::/10", "ff00::/8",
    ];

    private static readonly (byte[] Bytes, int Prefix)[] Networks = BlockedCidrs.Select(cidr =>
    {
        var parts = cidr.Split('/');
        return (IPAddress.Parse(parts[0]).GetAddressBytes(), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }).ToArray();

    private static readonly Gen<string> BlockedAddresses =
        from network in Gen.Int[0, Networks.Length - 1]
        from random in Gen.Byte.Array[16]
        select AddressInNetwork(network, random);

    private static readonly Gen<string> BlockedHosts = Gen.OneOf(
        Gen.OneOf("localhost", "metadata", "169.254.169.254", "127.1", "0"),
        Gen.Int[0, 100_000].Select(n => $"host{n}.internal"),
        Gen.Int[0, 100_000].Select(n => $"host{n}.localhost"),
        Gen.Int[0, 100_000].Select(n => $"host{n}.local"));

    private static readonly Gen<string> Urls = Gen.OneOf(
        from address in BlockedAddresses
        from encoding in Gen.Int[0, 5]
        select $"https://{EncodeAddress(address, encoding)}/article",
        from host in BlockedHosts
        from suffix in Gen.OneOf("", ".")
        select $"http://{host}{suffix}/",
        from host in Gen.OneOf("example.com", "8.8.8.8", "1.1.1.1", "[2606:4700:4700::1111]")
        from scheme in Gen.OneOf("http", "https", "HTTPS")
        from number in Gen.Int[0, int.MaxValue]
        select $"  {scheme}://{host}:8080/article/{number}?q=queen  ",
        Gen.OneOf("", "not a URL", "ftp://example.com/", "file:///etc/hosts", "https://user:pass@example.com/",
            "https://example.com/" + new string('a', 2_001)));

    [Fact]
    public void BlockedAddresses_AreRejectedInEveryEncoding()
    {
        PropertyRun.Run(nameof(BlockedAddresses_AreRejectedInEveryEncoding), BlockedAddresses, address =>
        {
            Assert.True(OracleBlocked(address));
            Assert.True(OutboundUrlSafety.IsBlockedAddress(IPAddress.Parse(address)));
            for (var encoding = 0; encoding < 6; encoding++)
            {
                var host = EncodeAddress(address, encoding);
                Assert.True(OutboundUrlSafety.IsBlockedHostName(host), $"Host: {host}");
                foreach (var url in DecorateHost(host))
                {
                    Assert.False(OutboundUrlSafety.TryValidatePublicHttpUrl(url, out _, out _), $"URL: {url}");
                }
            }
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void BlockedNames_AreRejectedWithCasePortsAndTrailingDots()
    {
        PropertyRun.Run(nameof(BlockedNames_AreRejectedWithCasePortsAndTrailingDots), BlockedHosts, host =>
        {
            foreach (var variant in new[] { host, host.ToUpperInvariant(), host + "." })
            {
                Assert.True(OracleBlocked(variant));
                Assert.True(OutboundUrlSafety.IsBlockedHostName(variant), $"Host: {variant}");
                foreach (var url in DecorateHost(variant))
                {
                    Assert.False(OutboundUrlSafety.TryValidatePublicHttpUrl(url, out _, out _), $"URL: {url}");
                }
            }
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void AcceptedUrls_SatisfyIndependentSafetyOracle()
    {
        PropertyRun.Run(nameof(AcceptedUrls_SatisfyIndependentSafetyOracle), Urls, url =>
        {
            if (!OutboundUrlSafety.TryValidatePublicHttpUrl(url, out _, out var normalized))
            {
                return;
            }

            var uri = new Uri(url.Trim(), UriKind.Absolute);
            Assert.Contains(uri.Scheme, new[] { "http", "https" });
            Assert.InRange(url.Trim().Length, 1, 2_000);
            Assert.Empty(uri.UserInfo);
            Assert.False(OracleBlocked(uri.DnsSafeHost), $"URL: {url}");
            Assert.NotNull(normalized);
            Assert.True(OutboundUrlSafety.TryValidatePublicHttpUrl(normalized, out _, out _));
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void Normalization_PreservesValidationDecision()
    {
        PropertyRun.Run(nameof(Normalization_PreservesValidationDecision), Urls, url =>
        {
            var accepted = OutboundUrlSafety.TryValidatePublicHttpUrl(url, out _, out var normalized);
            if (accepted)
            {
                Assert.True(OutboundUrlSafety.TryValidatePublicHttpUrl(normalized, out _, out var again));
                Assert.Equal(normalized, again);
            }
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                Assert.Equal(accepted, OutboundUrlSafety.TryValidatePublicHttpUrl(uri.AbsoluteUri, out _, out _));
            }
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void PublicUrlsAreAccepted_AndInvalidUrlsAreRejected()
    {
        var cases = from number in Gen.Int[0, int.MaxValue]
                    from host in Gen.OneOf("example.com", "8.8.8.8", "1.1.1.1", "[2606:4700:4700::1111]")
                    from scheme in Gen.OneOf("http", "https")
                    select (number, host, scheme);
        PropertyRun.Run(nameof(PublicUrlsAreAccepted_AndInvalidUrlsAreRejected), cases, input =>
        {
            var url = $"{input.scheme}://{input.host}/{input.number}";
            Assert.False(OracleBlocked(input.host));
            Assert.True(OutboundUrlSafety.TryValidatePublicHttpUrl(url, out _, out _));
            foreach (var invalid in new[] { $"ftp://{input.host}/{input.number}", $"invalid-{input.number}",
                url + new string('x', 2_001), $"https://user:pass@{input.host}/" })
            {
                Assert.False(OutboundUrlSafety.TryValidatePublicHttpUrl(invalid, out _, out _));
            }
        }, PropertyRun.CheapIterations);
    }

    private static string AddressInNetwork(int index, byte[] random)
    {
        var (network, prefix) = Networks[index];
        var bytes = random[..network.Length];
        for (var bit = 0; bit < prefix; bit++)
        {
            var mask = 1 << (7 - bit % 8);
            bytes[bit / 8] = (byte)((bytes[bit / 8] & ~mask) | (network[bit / 8] & mask));
        }
        return new IPAddress(bytes).ToString();
    }

    private static string EncodeAddress(string address, int encoding)
    {
        var ip = IPAddress.Parse(address);
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{address.ToUpperInvariant()}]";
        }

        var bytes = ip.GetAddressBytes();
        var value = bytes.Aggregate(0UL, (current, b) => (current << 8) | b);
        return encoding switch
        {
            0 => address,
            1 => string.Join('.', bytes.Select(b => "0" + Convert.ToString(b, 8))),
            2 => $"0x{value:x}",
            3 => value.ToString(CultureInfo.InvariantCulture),
            4 => $"{bytes[0]}.{value & 0xFFFFFF}",
            _ => $"[::ffff:{address}]",
        };
    }

    private static IEnumerable<string> DecorateHost(string host)
    {
        yield return $"http://{host}/";
        yield return $"HTTPS://{host.ToUpperInvariant()}:8443/article";
        yield return $"http://user:password@{host}:8080/";
        if (!host.StartsWith('['))
        {
            yield return $"https://{host}./";
        }
    }

    private static bool OracleBlocked(string host)
    {
        host = host.TrimEnd('.').Trim('[', ']').ToLowerInvariant();
        if (host is "localhost" or "metadata" || new[] { ".localhost", ".local", ".internal" }.Any(host.EndsWith))
        {
            return true;
        }

        byte[] bytes;
        if (host.Contains(':'))
        {
            if (!IPAddress.TryParse(host, out var address))
            {
                return false;
            }
            bytes = address.GetAddressBytes();
            if (bytes.Length == 16 && bytes[..10].All(b => b == 0) && bytes[10] == 255 && bytes[11] == 255)
            {
                bytes = bytes[12..];
            }
        }
        else if (!TryDecodeIpv4(host, out bytes))
        {
            return false;
        }

        return Networks.Any(network => network.Bytes.Length == bytes.Length &&
            Enumerable.Range(0, network.Prefix).All(bit =>
                (bytes[bit / 8] & (1 << (7 - bit % 8))) == (network.Bytes[bit / 8] & (1 << (7 - bit % 8)))));
    }

    private static bool TryDecodeIpv4(string host, out byte[] bytes)
    {
        bytes = [];
        var parts = host.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        ulong value = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var radix = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : part.Length > 1 && part[0] == '0' ? 8 : 10;
            var digits = radix == 16 ? part[2..] : part;
            if (digits.Length == 0)
            {
                return false;
            }
            ulong component = 0;
            foreach (var digit in digits)
            {
                var n = "0123456789abcdef".IndexOf(char.ToLowerInvariant(digit));
                if (n < 0 || n >= radix || component > uint.MaxValue / (ulong)radix)
                {
                    return false;
                }
                component = component * (ulong)radix + (ulong)n;
            }
            var bits = i == parts.Length - 1 ? 8 * (5 - parts.Length) : 8;
            if (component >= (1UL << bits))
            {
                return false;
            }
            value = (value << bits) | component;
        }
        bytes = [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        return true;
    }
}

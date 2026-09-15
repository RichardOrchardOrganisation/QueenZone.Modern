using System.Security.Cryptography;
using System.Text;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class LogRedactionTests
{
    [Fact]
    public void EmailFingerprint_IsLowercaseSha256Prefix()
    {
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("admin@example.com")))[..12];

        Assert.Equal(expected, LogRedaction.EmailFingerprint(" Admin@Example.com "));
        Assert.Equal(LogRedaction.EmailFingerprintLength, expected.Length);
        Assert.Matches("^[0-9a-f]{12}$", expected);
    }

    [Fact]
    public void EmailFingerprint_IsStableAcrossCaseAndWhitespace()
    {
        var first = LogRedaction.EmailFingerprint("Admin@Example.com");
        var second = LogRedaction.EmailFingerprint("  admin@example.com\t");

        Assert.Equal(first, second);
        Assert.DoesNotContain('@', first);
        Assert.DoesNotContain("admin", first, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmailFingerprint_ReturnsEmptyForBlank(string? email)
    {
        Assert.Equal(string.Empty, LogRedaction.EmailFingerprint(email));
    }

    [Fact]
    public void EmailFingerprint_DiffersForDifferentAddresses()
    {
        Assert.NotEqual(
            LogRedaction.EmailFingerprint("admin@example.com"),
            LogRedaction.EmailFingerprint("other@example.com"));
    }
}

namespace QueenZone.Web.Tests;

public sealed class MobileAuthOptionsValidatorTests
{
    [Fact]
    public void AllowsDefaultOptions_InTesting()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions());
        Assert.False(result.Failed);
    }

    [Fact]
    public void DefaultsRefreshTokenReuseGraceSecondsToFiveMinutes()
    {
        Assert.Equal(300, new MobileAuthOptions().RefreshTokenReuseGraceSeconds);
    }

    [Fact]
    public void AllowsMissingSigningKey_InProduction()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions());
        Assert.False(result.Failed);
    }

    [Fact]
    public void AcceptsConfiguredSigningKey_InProduction()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions
            {
                SigningKey = "production-mobile-auth-signing-key!!",
            });
        Assert.False(result.Failed);
    }

    [Fact]
    public void RejectsShortSigningKey()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions { SigningKey = "too-short" });
        Assert.True(result.Failed);
        Assert.Contains("32", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsJavascriptRedirectUri()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions
            {
                RedirectUris = ["javascript:alert(1)"],
            });
        Assert.True(result.Failed);
        Assert.Contains("RedirectUris", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBlankClientId()
    {
        var result = new MobileAuthOptionsValidator()
            .Validate(null, new MobileAuthOptions { ClientId = " " });
        Assert.True(result.Failed);
        Assert.Contains("ClientId", result.FailureMessage, StringComparison.Ordinal);
    }
}

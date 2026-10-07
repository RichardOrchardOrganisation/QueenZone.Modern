using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for the shared DEV sign-in helper. No browser and no
/// deployed site — the password form path stays covered by DeployedAuth.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class DeployedMemberSignInTests
{
    [Test]
    public void AssertHasMemberSession_PassesWhenCookiePresent()
    {
        var cookies = new[]
        {
            new BrowserContextCookiesResult
            {
                Name = DeployedMemberSignIn.MemberSessionCookieName,
                Value = "ticket",
            },
        };

        Assert.DoesNotThrow(() => DeployedMemberSignIn.AssertHasMemberSession(cookies));
    }

    [Test]
    public void AssertHasMemberSession_AcceptsChunkedCookieName()
    {
        var cookies = new[]
        {
            new BrowserContextCookiesResult
            {
                Name = DeployedMemberSignIn.MemberSessionCookieName + "C1",
                Value = "chunk",
            },
        };

        Assert.DoesNotThrow(() => DeployedMemberSignIn.AssertHasMemberSession(cookies));
    }

    [Test]
    public void AssertHasMemberSession_FailsWithClearMessageWhenMissing()
    {
        var ex = Assert.Throws<AssertionException>(() =>
            DeployedMemberSignIn.AssertHasMemberSession([]));

        Assert.That(ex!.Message, Does.Contain(DeployedMemberSignIn.SignedOutMessage));
        Assert.That(ex.Message, Does.Not.Contain("SignInAsync"));
    }

    [Test]
    public void AssertHasMemberSession_FailsWhenCookieValueEmpty()
    {
        var cookies = new[]
        {
            new BrowserContextCookiesResult
            {
                Name = DeployedMemberSignIn.MemberSessionCookieName,
                Value = string.Empty,
            },
        };

        var ex = Assert.Throws<AssertionException>(() =>
            DeployedMemberSignIn.AssertHasMemberSession(cookies));
        Assert.That(ex!.Message, Does.Contain("refusing to sign in again"));
    }

    [Test]
    public void IsMemberSessionCookie_RejectsUnrelatedCookies()
    {
        var cookie = new BrowserContextCookiesResult
        {
            Name = ".AspNetCore.Antiforgery",
            Value = "token",
        };

        Assert.That(DeployedMemberSignIn.IsMemberSessionCookie(cookie), Is.False);
    }
}

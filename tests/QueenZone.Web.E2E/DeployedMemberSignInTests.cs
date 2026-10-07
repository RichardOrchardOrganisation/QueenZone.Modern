using System.Runtime.CompilerServices;

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
    public void SignedOutMessage_RefusesToSignInAgain()
    {
        Assert.That(DeployedMemberSignIn.SignedOutMessage, Does.Contain("signed in once"));
        Assert.That(DeployedMemberSignIn.SignedOutMessage, Does.Contain("refusing to sign in again"));
        Assert.That(DeployedMemberSignIn.SignedOutMessage, Does.Not.Contain("SignInAsync"));
    }

    [Test]
    public void AssertSignedInChrome_OpensHomeAndHardExpectsSignOut()
    {
        var path = Path.GetFullPath(Path.Combine(RepoRoot(), "tests", "QueenZone.Web.E2E", "DeployedMemberSignIn.cs"));
        var source = File.ReadAllText(path);
        const string marker = "internal static async Task AssertSignedInChromeAsync";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "Expected AssertSignedInChromeAsync on the shared helper.");
        var after = source.IndexOf("internal static", start + marker.Length, StringComparison.Ordinal);
        var method = after < 0 ? source[start..] : source[start..after];

        Assert.That(method, Does.Contain("GotoAsync(\"/\")"));
        Assert.That(method, Does.Contain("Name = \"Sign out\""));
        Assert.That(method, Does.Contain("ToBeVisibleAsync()"));
        Assert.That(method, Does.Contain("Assert.Fail(SignedOutMessage)"));
        Assert.That(method, Does.Not.Contain("CookiesAsync"));
        Assert.That(method, Does.Not.Contain("SignInAsync"));
        Assert.That(method, Does.Not.Contain("FillPasswordFormAndSubmitAsync"));
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var directory = Path.GetDirectoryName(thisFile);
        Assert.That(directory, Is.Not.Null.And.Not.Empty);
        return Path.GetFullPath(Path.Combine(directory!, "..", ".."));
    }
}

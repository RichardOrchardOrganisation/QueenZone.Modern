namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for encoding-artifact detection and the curated-page catalog (no browser).
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class PageShapeAssertionTests
{
    [TestCase("QueenZone &amp; the archive")]
    [TestCase("1 &lt; 2")]
    [TestCase("hello &quot;world&quot;")]
    [TestCase("it&#39;s")]
    [TestCase("A&nbsp;B")]
    public void FindEncodingArtifact_DetectsActionableEntities(string bodyText)
    {
        var match = PageShapeAssertions.FindEncodingArtifact(bodyText);
        Assert.That(match.Success, Is.True, bodyText);
    }

    [TestCase("QueenZone and the archive")]
    [TestCase("Use & for queries")]
    [TestCase("")]
    public void FindEncodingArtifact_IgnoresCleanVisibleText(string bodyText)
    {
        var match = PageShapeAssertions.FindEncodingArtifact(bodyText);
        Assert.That(match.Success, Is.False, bodyText);
    }

    [Test]
    public void IsActionableConsoleError_IgnoresResource404s()
    {
        Assert.That(
            PageShapeAssertions.IsActionableConsoleError("Failed to load resource: the server responded with a status of 404"),
            Is.False);
        Assert.That(
            PageShapeAssertions.IsActionableConsoleError("TypeError: Cannot read properties of null"),
            Is.True);
    }

    [Test]
    public void CuratedCatalog_IsASmallHighValueSet()
    {
        var all = CuratedLayoutPages.All.ToList();
        Assert.That(all, Has.Count.InRange(6, 10));
        Assert.That(all.Select(p => p.Path).Distinct(StringComparer.Ordinal), Has.Count.EqualTo(all.Count));
        Assert.That(all.Select(p => p.Path), Does.Contain("/").And.Contain("/messages"));
    }

    [Test]
    public void HorizontalOverflowScript_ComparesScrollWidthToClientWidth()
    {
        Assert.That(
            PageShapeAssertions.HorizontalOverflowScript,
            Does.Contain("scrollWidth").And.Contain("clientWidth"));
    }
}

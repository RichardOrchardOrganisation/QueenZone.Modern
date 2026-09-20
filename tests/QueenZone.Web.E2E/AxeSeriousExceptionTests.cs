namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for the serious-axe exception policy (no browser).
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class AxeSeriousExceptionTests
{
    [Test]
    public void IsAllowed_ColorContrast_IsTheDocumentedChromeException()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed("/", "color-contrast"), Is.True);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news/1003/queenzone-modernisation-begins", "color-contrast"), Is.True);
        Assert.That(AxeSeriousExceptions.IsAllowed("/forum/topic/1002/ranking-every-studio-album", "color-contrast"), Is.True);
    }

    [Test]
    public void IsAllowed_LinkInTextBlock_OnlyOnNewsDetailPrefix()
    {
        Assert.That(
            AxeSeriousExceptions.IsAllowed("/news/1003/queenzone-modernisation-begins", "link-in-text-block"),
            Is.True);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", "link-in-text-block"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/", "link-in-text-block"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/forum/topic/1002/ranking-every-studio-album", "link-in-text-block"), Is.False);
    }

    [Test]
    public void IsAllowed_FalseForUnknownRule()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed("/", "button-name"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/messages", "link-name"), Is.False);
    }

    [Test]
    public void IsAllowed_FalseForBlankRuleId()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", null), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", ""), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", "   "), Is.False);
    }

    [Test]
    public void PathMatches_SlashPrefixIsExactOnly()
    {
        Assert.That(AxeSeriousExceptions.PathMatches("/", "/"), Is.True);
        Assert.That(AxeSeriousExceptions.PathMatches("/news", "/"), Is.False);
        Assert.That(AxeSeriousExceptions.PathMatches("/news/1003/x", "/news/"), Is.True);
        Assert.That(AxeSeriousExceptions.PathMatches("/news", "/news/"), Is.False);
        Assert.That(AxeSeriousExceptions.PathMatches("/forum", "*"), Is.True);
    }

    [Test]
    public void AllowedList_StaysTheTriagedPairOnly()
    {
        Assert.That(
            AxeSeriousExceptions.Allowed,
            Is.EqualTo(new[]
            {
                ("*", "color-contrast"),
                ("/news/", "link-in-text-block"),
            }));
    }
}

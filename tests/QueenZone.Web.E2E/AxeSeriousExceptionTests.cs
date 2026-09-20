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
    public void IsAllowed_FalseForUnknownRuleOnAnyPath()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed("/", "color-contrast"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/forum/topic/1002/ranking-every-studio-album", "link-name"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/messages", "button-name"), Is.False);
    }

    [Test]
    public void IsAllowed_FalseForBlankRuleId()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", null), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", ""), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed("/news", "   "), Is.False);
    }

    [Test]
    public void IsAllowed_FalseWhenPagePathMissing()
    {
        Assert.That(AxeSeriousExceptions.IsAllowed(null, "color-contrast"), Is.False);
        Assert.That(AxeSeriousExceptions.IsAllowed(string.Empty, "color-contrast"), Is.False);
    }

    [Test]
    public void AllowedList_StartsEmptyUntilALegacyFindingIsTriaged()
    {
        var allowed = typeof(AxeSeriousExceptions)
            .GetField("Allowed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(allowed, Is.Not.Null);

        var rows = ((IEnumerable<(string PathPrefix, string RuleId)>?)allowed!.GetValue(null))?.ToArray();
        Assert.That(rows, Is.Not.Null);
        Assert.That(
            rows!,
            Is.Empty,
            "Serious axe exceptions must stay empty until a curated-page finding is triaged as known legacy/UGC. " +
            "Do not pre-seed wildcards.");
    }
}

namespace QueenZone.Web.E2E;

/// <summary>
/// Proves the live-site write guard throws (issue #551), not merely that the env flag is parsed.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class RealDataWriteGuardTests
{
    [Test]
    public void AdminFixture_ThrowsWhenE2EReadonlyIsTrue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(
                allowsWrites: true,
                fixtureName: "AdminRootProofTests",
                getEnv: name => name == "E2E_READONLY" ? "true" : null));

        Assert.That(ex!.Message, Does.Contain("AdminRootProofTests"));
        Assert.That(ex.Message, Does.Contain("E2E_READONLY=true"));
    }

    [Test]
    public void AdminProofFixtures_AreNeverRealDataOrReadOnly()
    {
        var fixtures = typeof(AdminFeatureProofTest).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(AdminFeatureProofTest).IsAssignableFrom(type))
            .ToArray();
        Assert.That(fixtures, Is.Not.Empty);
        foreach (var fixture in fixtures)
        {
            var categories = fixture.GetCustomAttributes(typeof(CategoryAttribute), inherit: true)
                .Concat(fixture.GetMethods().SelectMany(method => method.GetCustomAttributes(typeof(CategoryAttribute), inherit: true)))
                .Cast<CategoryAttribute>().Select(attribute => attribute.Name).ToArray();
            Assert.That(categories, Does.Not.Contain(E2ECategories.RealData), fixture.Name);
            Assert.That(categories, Does.Not.Contain(E2ECategories.ReadOnly), fixture.Name);
        }
    }

    [Test]
    public void WriteCapableFixture_ThrowsWhenE2EReadonlyIsTrue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(
                allowsWrites: true,
                fixtureName: "CommunitySubmissionWorkflowTests",
                getEnv: name => name == "E2E_READONLY" ? "true" : null));

        Assert.That(ex!.Message, Does.Contain("CommunitySubmissionWorkflowTests"));
        Assert.That(ex.Message, Does.Contain("E2E_READONLY=true"));
        Assert.That(ex.Message, Does.Contain("live-site"));
    }

    [Test]
    public void WriteCapableFixture_ThrowsWhenE2EReadonlyIsOne()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(
                allowsWrites: true,
                fixtureName: "WriteFixture",
                getEnv: name => name == "E2E_READONLY" ? "1" : null));
    }

    [Test]
    public void WriteCapableFixture_DoesNotThrowWhenReadonlyUnset()
    {
        Assert.DoesNotThrow(() =>
            RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(
                allowsWrites: true,
                fixtureName: "WriteFixture",
                getEnv: _ => null));
    }

    [Test]
    public void ReadOnlyFixture_DoesNotThrowWhenE2EReadonlyIsTrue()
    {
        Assert.DoesNotThrow(() =>
            RealDataWriteGuard.EnsureNotWriteCapableInReadOnlyMode(
                allowsWrites: false,
                fixtureName: "SitemapPublicRouteSweepTests",
                getEnv: name => name == "E2E_READONLY" ? "true" : null));
    }
}

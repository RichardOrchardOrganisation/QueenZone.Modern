using System.Reflection;

namespace QueenZone.Web.E2E;

/// <summary>
/// Ensures every concrete Playwright fixture is tagged so CI filters cannot silently
/// drop new tests outside the PR gate, nightly mirror suite, or deployed dev auth check.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class E2ECategoryGuardTests
{
    private static readonly string[] SuiteCategories =
    [
        E2ECategories.Deterministic,
        E2ECategories.RealData,
        E2ECategories.DeployedAuth
    ];

    [Test]
    public void EveryConcreteFixtureHasASuiteCategory()
    {
        var assembly = typeof(E2EPageTest).Assembly;
        var fixtures = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        Assert.That(fixtures, Is.Not.Empty, "Expected at least one NUnit fixture in QueenZone.Web.E2E.");

        var missing = fixtures
            .Where(t => !HasAnySuiteCategory(t))
            .Select(t => t.Name)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "These fixtures need [Category(\"Deterministic\")], [Category(\"RealData\")], or [Category(\"DeployedAuth\")] " +
            "(and optionally [Category(\"ReadOnly\")]): " + string.Join(", ", missing));
    }

    [Test]
    public void DeterministicFixturesArePresent()
    {
        var assembly = typeof(E2EPageTest).Assembly;
        var deterministic = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .Where(t => HasCategory(t, E2ECategories.Deterministic))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Keep this list in sync when adding Deterministic fixtures so an accidental
        // category rename or drop is visible in the PR gate.
        var expected = new[]
        {
            nameof(AccessibilitySmokeTests),
            nameof(AdminSmokeTests),
            nameof(AxeSeriousExceptionTests),
            nameof(CuratedPageLayoutSmokeTests),
            nameof(E2ECategoryGuardTests),
            nameof(EditorWorkflowTests),
            nameof(ForumPostingWorkflowTests),
            nameof(ForumSafetyWorkflowTests),
            nameof(LiveSiteTransportRetryTests),
            nameof(PageShapeAssertionTests),
            nameof(PhotographyLightboxTests),
            nameof(PrivateMessagingMobileTests),
            nameof(RealDataDbTests),
            nameof(RealDataMarkerTests),
            nameof(RealDataWriteGuardTests),
            nameof(SeededSamplerTests),
            nameof(SelectorConventionGuardTests),
            nameof(SitemapRouteParserTests),
            nameof(SmokeTests),
            nameof(SocialShareTests),
        };

        Assert.That(deterministic, Is.EqualTo(expected));
    }

    [Test]
    public void RealDataFixturesArePresent()
    {
        // Keep this list in sync as RealData fixtures land. LiveSite filter is
        // TestCategory=RealData&TestCategory=ReadOnly (sitemap + media CDN + public JSON API).
        var assembly = typeof(E2EPageTest).Assembly;
        var realData = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .Where(t => HasCategory(t, E2ECategories.RealData))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            nameof(AdminModerationWorkflowTests),
            nameof(CommunitySubmissionWorkflowTests),
            nameof(ForumBlockWorkflowTests),
            nameof(LiveSiteContentApiTests),
            nameof(LiveSiteMediaCdnTests),
            nameof(PrivateMessagingWorkflowTests),
            nameof(SitemapPublicRouteSweepTests),
        };

        Assert.That(realData, Is.EqualTo(expected));
    }

    [Test]
    public void LiveSiteFilterSelectsOnlyReadOnlyRealDataFixtures()
    {
        // Mirrors Run-E2E.ps1 -Mode LiveSite: TestCategory=RealData&TestCategory=ReadOnly
        var assembly = typeof(E2EPageTest).Assembly;
        var liveSite = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .Where(t => HasCategory(t, E2ECategories.RealData) && HasCategory(t, E2ECategories.ReadOnly))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            nameof(LiveSiteContentApiTests),
            nameof(LiveSiteMediaCdnTests),
            nameof(SitemapPublicRouteSweepTests),
        };

        Assert.That(
            liveSite,
            Is.EqualTo(expected),
            "Live-site job must only select write-free RealData fixtures. " +
            "Mark new read-only RealData fixtures with [Category(\"ReadOnly\")].");
    }

    [Test]
    public void DeployedAuthFilterSelectsOnlyTheDevMemberFixture()
    {
        var fixtures = typeof(E2EPageTest).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .Where(t => HasCategory(t, E2ECategories.DeployedAuth))
            .Select(t => t.Name)
            .ToList();

        Assert.That(fixtures, Is.EqualTo(new[] { nameof(DeployedMemberAuthTests) }));
    }

    [TestCase("https://dev.queenzone.org")]
    [TestCase("https://dev.queenzone.org/")]
    public void DeployedAuthTargetAcceptsOnlyTheDevOrigin(string url) =>
        Assert.That(DeployedAuthTarget.RequireDevUrl(url).Host, Is.EqualTo("dev.queenzone.org"));

    [TestCase("https://www.queenzone.org")]
    [TestCase("https://dev.queenzone.org.evil.example")]
    [TestCase("http://dev.queenzone.org")]
    [TestCase("https://dev.queenzone.org:444")]
    [TestCase("https://dev.queenzone.org/account/login")]
    public void DeployedAuthTargetRejectsOtherOriginsAndPaths(string url) =>
        Assert.Throws<InvalidOperationException>(() => DeployedAuthTarget.RequireDevUrl(url));

    private static bool IsNUnitFixture(Type type)
    {
        if (type.GetCustomAttributes(typeof(TestFixtureAttribute), inherit: true).Length > 0)
        {
            return true;
        }

        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Any(m => m.GetCustomAttributes(typeof(TestAttribute), inherit: true).Length > 0);
    }

    private static bool HasAnySuiteCategory(Type type) =>
        SuiteCategories.Any(c => HasCategory(type, c));

    private static bool HasCategory(Type type, string category) =>
        type.GetCustomAttributes(typeof(CategoryAttribute), inherit: true)
            .OfType<CategoryAttribute>()
            .Any(a => string.Equals(a.Name, category, StringComparison.Ordinal));
}

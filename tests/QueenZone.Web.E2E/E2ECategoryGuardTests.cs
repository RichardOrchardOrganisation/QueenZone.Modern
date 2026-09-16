using System.Reflection;

namespace QueenZone.Web.E2E;

/// <summary>
/// Ensures every concrete Playwright fixture is tagged so CI filters cannot silently
/// drop new tests into neither the PR gate nor the nightly RealData suite.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class E2ECategoryGuardTests
{
    private static readonly string[] SuiteCategories =
    [
        E2ECategories.Deterministic,
        E2ECategories.RealData
    ];

    [Test]
    public void EveryConcreteFixtureHasDeterministicOrRealDataCategory()
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
            "These fixtures need [Category(\"Deterministic\")] or [Category(\"RealData\")] " +
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
            nameof(E2ECategoryGuardTests),
            nameof(EditorWorkflowTests),
            nameof(ForumPostingWorkflowTests),
            nameof(LiveSiteTransportRetryTests),
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

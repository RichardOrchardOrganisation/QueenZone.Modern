using System.Reflection;
using System.Runtime.CompilerServices;

namespace QueenZone.Web.E2E;

/// <summary>
/// Ensures every concrete Playwright fixture is tagged so CI filters cannot silently
/// drop new tests outside the PR gate, nightly mirror suite, deployed dev auth check,
/// or deployed DEV journey tip gate.
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
        E2ECategories.DeployedAuth,
        E2ECategories.DevJourney
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
            "These fixtures need [Category(\"Deterministic\")], [Category(\"RealData\")], [Category(\"DeployedAuth\")], or [Category(\"DevJourney\")] " +
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
            nameof(AdminCrosswordTests),
            nameof(AdminEditReloadDiagnosticsTests),
            nameof(AdminSearchGotoDiagnosticsTests),
            nameof(AdminSmokeTests),
            nameof(AxeSeriousExceptionTests),
            nameof(CrosswordPlayTests),
            nameof(CrosswordReplayTests),
            nameof(CrosswordResultsTests),
            nameof(CuratedPageLayoutSmokeTests),
            nameof(DeployedMemberSignInTests),
            nameof(E2ECategoryGuardTests),
            nameof(EditorWorkflowTests),
            nameof(ForumPostingWorkflowTests),
            nameof(ForumSafetyWorkflowTests),
            nameof(ForumYoutubeVideoTests),
            nameof(LiveSiteTransportRetryTests),
            nameof(PageShapeAssertionTests),
            nameof(ParticipationClarityTests),
            nameof(PhotographyLightboxTests),
            nameof(PrivateMessagingMobileTests),
            nameof(QuizSprintProgressTests),
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

    [Test]
    public void DevJourneyFilterSelectsOnlyTheDevJourneyFixture()
    {
        var fixtures = typeof(E2EPageTest).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(IsNUnitFixture)
            .Where(t => HasCategory(t, E2ECategories.DevJourney))
            .Select(t => t.Name)
            .ToList();

        Assert.That(fixtures, Is.EqualTo(new[] { nameof(DevJourneyTests) }));
        Assert.That(HasCategory(typeof(DevJourneyTests), E2ECategories.RealData), Is.False);
        Assert.That(HasCategory(typeof(DevJourneyTests), E2ECategories.ReadOnly), Is.False);
        Assert.That(HasCategory(typeof(DevJourneyTests), E2ECategories.Deterministic), Is.False);
        Assert.That(HasCategory(typeof(DevJourneyTests), E2ECategories.DeployedAuth), Is.False);
    }

    [Test]
    public void DevJourneySignsInOncePerFixtureAndDoesNotResignInFromSetUp()
    {
        var path = Path.GetFullPath(Path.Combine(RepoRoot(), "tests", "QueenZone.Web.E2E", "DevJourneyTests.cs"));
        var source = File.ReadAllText(path);

        Assert.That(source, Does.Contain("[OneTimeSetUp]"));
        Assert.That(source, Does.Contain("CaptureSignedInStorageStateAsync"));
        Assert.That(source, Does.Contain("StorageState = _signedInStorageState"));
        Assert.That(source, Does.Contain("AssertContextHasMemberSessionAsync"));
        Assert.That(source, Does.Not.Contain("await DeployedMemberSignIn.SignInAsync(Page);"));
        Assert.That(source, Does.Contain("Write coverage is skipped for Ship A"));
    }

    [Test]
    public void DevJourneyDiscovery_PicksFirstAlphabeticWordOfAtLeastThreeLetters()
    {
        Assert.That(DevJourneyDiscovery.FirstSearchWord("A Night at the Opera"), Is.EqualTo("Night"));
        Assert.That(DevJourneyDiscovery.FirstSearchWord("News: Live Aid 1985"), Is.EqualTo("News"));
    }

    [Test]
    public void DevJourneyDiscovery_RejectsEmptyOrTinyTokens()
    {
        Assert.Throws<AssertionException>(() => DevJourneyDiscovery.FirstSearchWord(""));
        Assert.Throws<AssertionException>(() => DevJourneyDiscovery.FirstSearchWord("a I"));
    }

    [Test]
    public void DevJourneyWorkflowIsASiblingTipGateNotAPrCheck()
    {
        var workflowPath = Path.GetFullPath(Path.Combine(RepoRoot(), ".github", "workflows", "dev-journey-e2e.yml"));
        Assert.That(File.Exists(workflowPath), Is.True, $"Expected DevJourney workflow at {workflowPath}.");

        var workflow = File.ReadAllText(workflowPath);
        Assert.That(workflow, Does.Contain("TestCategory=DevJourney"));
        Assert.That(workflow, Does.Contain("environment: dev-deploy"));
        Assert.That(workflow, Does.Contain("https://dev.queenzone.org"));
        Assert.That(workflow, Does.Contain("0 7 * * *"));
        Assert.That(workflow, Does.Not.Contain("TestCategory=Deterministic"));
        Assert.That(workflow, Does.Not.Contain("TestCategory=DeployedAuth"));
        Assert.That(workflow, Does.Not.Contain("TestCategory=RealData"));
    }

    [Test]
    public void RunE2EScriptExposesDevJourneyModeWithoutStartingALocalApp()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(RepoRoot(), "scripts", "Run-E2E.ps1"));
        Assert.That(File.Exists(scriptPath), Is.True, $"Expected Run-E2E.ps1 at {scriptPath}.");

        var script = File.ReadAllText(scriptPath);
        Assert.That(script, Does.Contain("\"DevJourney\""));
        Assert.That(script, Does.Contain("TestCategory=DevJourney"));
        Assert.That(script, Does.Contain("RequireDevUrl"));
        Assert.That(script, Does.Contain("NUnit.NumberOfTestWorkers=1"));
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

    [Test]
    public void CiPullRequestE2eJobStaysDeterministicOnly()
    {
        // #1597: RealData (nightly/live-site), DeployedAuth (dev member cookie),
        // and DevJourney (deployed DEV journey) must not become required PR checks.
        // The merge-gate job pins Mode=Deterministic.
        var ciPath = Path.GetFullPath(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        Assert.That(File.Exists(ciPath), Is.True, $"Expected CI workflow at {ciPath}.");

        var ci = File.ReadAllText(ciPath);
        Assert.That(ci, Does.Contain("-Mode Deterministic"));
        Assert.That(ci, Does.Not.Contain("-Mode RealData"));
        Assert.That(ci, Does.Not.Contain("-Mode LiveSite"));
        Assert.That(ci, Does.Not.Contain("TestCategory=DeployedAuth"));
        Assert.That(ci, Does.Not.Contain("DevJourney"));
        Assert.That(ci, Does.Not.Contain("TestCategory=DevJourney"));
        Assert.That(ci, Does.Not.Contain("-Mode DevJourney"));
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

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var directory = Path.GetDirectoryName(thisFile);
        Assert.That(directory, Is.Not.Null.And.Not.Empty);
        return Path.GetFullPath(Path.Combine(directory!, "..", ".."));
    }
}

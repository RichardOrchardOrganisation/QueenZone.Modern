namespace QueenZone.Web.E2E;

/// <summary>
/// Host-free checks for the #1968 admin-search Goto failure dump (no browser).
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class AdminSearchGotoDiagnosticsTests
{
    [Test]
    public void FormatDump_IncludesUrlStatusAndSearchIndexAdminPresence()
    {
        var dump = AdminSearchGotoDiagnostics.FormatDump(
            url: "http://127.0.0.1:5099/admin/search",
            status: 200,
            searchIndexAdminPresent: true,
            exceptionMessage: "Timeout 30000ms exceeded");

        Assert.That(dump, Does.Contain("URL: http://127.0.0.1:5099/admin/search"));
        Assert.That(dump, Does.Contain("Status: 200"));
        Assert.That(dump, Does.Contain("#search-index-admin: present"));
        Assert.That(dump, Does.Contain("Timeout 30000ms exceeded"));
    }

    [Test]
    public void FormatDump_UsesPlaceholdersWhenStatusAndRootAreUnavailable()
    {
        var dump = AdminSearchGotoDiagnostics.FormatDump(
            url: "about:blank",
            status: null,
            searchIndexAdminPresent: null,
            exceptionMessage: "Timeout 30000ms exceeded");

        Assert.That(dump, Does.Contain("URL: about:blank"));
        Assert.That(dump, Does.Contain("Status: (none)"));
        Assert.That(dump, Does.Contain("#search-index-admin: (unavailable)"));
    }

    [Test]
    public void FormatDump_ReportsMissingSearchIndexAdminRoot()
    {
        var dump = AdminSearchGotoDiagnostics.FormatDump(
            url: "http://127.0.0.1:5099/admin",
            status: 200,
            searchIndexAdminPresent: false,
            exceptionMessage: "ready locator timeout");

        Assert.That(dump, Does.Contain("#search-index-admin: missing"));
    }

    [Test]
    public void WriteDump_WritesBesideExistingE2EArtifactRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qz-e2e-search-goto-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utcNow = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            var path = AdminSearchGotoDiagnostics.WriteDump(
                "diagnostic body",
                directory: dir,
                testName: "Admin_search_reindex_completes",
                utcNow: utcNow);

            Assert.That(
                path,
                Is.EqualTo(Path.Combine(
                    dir,
                    "Admin_search_reindex_completes-search-goto-diagnostics-20261001120000.txt")));
            Assert.That(File.ReadAllText(path), Is.EqualTo("diagnostic body"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

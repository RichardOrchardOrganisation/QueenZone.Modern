namespace QueenZone.Web.E2E;

/// <summary>
/// Host-free checks for the #1934 admin-edit reload failure dump (no browser).
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class AdminEditReloadDiagnosticsTests
{
    [Test]
    public void FormatDump_IncludesUrlStatusTitleAndPreviewSrc()
    {
        var dump = AdminEditReloadDiagnostics.FormatDump(
            url: "http://127.0.0.1:5099/admin/photos/22114",
            status: 200,
            titleValue: "updated title",
            previewImageSrc: "https://cdn.queenzone.org/photos/22114.jpg",
            expectedTitle: "expected title",
            exceptionMessage: "Timeout 30000ms exceeded");

        Assert.That(dump, Does.Contain("URL: http://127.0.0.1:5099/admin/photos/22114"));
        Assert.That(dump, Does.Contain("Status: 200"));
        Assert.That(dump, Does.Contain("Expected Title: expected title"));
        Assert.That(dump, Does.Contain("Title input value: updated title"));
        Assert.That(dump, Does.Contain("Preview img[src]: https://cdn.queenzone.org/photos/22114.jpg"));
        Assert.That(dump, Does.Contain("Timeout 30000ms exceeded"));
    }

    [Test]
    public void FormatDump_UsesPlaceholdersWhenStatusAndFieldsMissing()
    {
        var dump = AdminEditReloadDiagnostics.FormatDump(
            url: "http://127.0.0.1:5099/admin/biography/1/edit",
            status: null,
            titleValue: null,
            previewImageSrc: null,
            expectedTitle: "bio updated",
            exceptionMessage: "reload failed");

        Assert.That(dump, Does.Contain("Status: (none)"));
        Assert.That(dump, Does.Contain("Title input value: (unavailable)"));
        Assert.That(dump, Does.Contain("Preview img[src]: (none)"));
    }

    [Test]
    public void WriteDump_WritesBesideExistingE2EArtifactRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qz-e2e-reload-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utcNow = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            var path = AdminEditReloadDiagnostics.WriteDump(
                "diagnostic body",
                directory: dir,
                testName: "Admin_photo_create_edit_and_hard_delete_round_trip",
                utcNow: utcNow);

            Assert.That(
                path,
                Is.EqualTo(Path.Combine(
                    dir,
                    "Admin_photo_create_edit_and_hard_delete_round_trip-reload-diagnostics-20260930120000.txt")));
            Assert.That(File.ReadAllText(path), Is.EqualTo("diagnostic body"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

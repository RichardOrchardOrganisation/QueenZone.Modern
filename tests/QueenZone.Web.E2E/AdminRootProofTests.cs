namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminRootProofTests : AdminFeatureProofTest
{
    [Test]
    public Task AdminIndex_ProvesAccessAndSelectors() => AssertAdminFeatureAsync("web.admin.index", "/admin");

    [Test]
    public Task SearchIndex_ProvesAccessAndSelectors() => AssertAdminFeatureAsync("web.admin.search", "/admin/search");

    [Test]
    public async Task SearchReindex_ConfirmOnly_DoesNotStartJob()
    {
        await AssertAdminFeatureAsync("web.admin.search", "/admin/search");
        var status = Page.Locator("#job-status");
        var phase = await status.GetAttributeAsync("data-phase");
        Assert.That(phase, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("Running"));
        await Expect(Page.Locator("#reindex-form")).ToBeVisibleAsync();
        await Expect(Page.Locator("#reindex-form")).ToHaveAttributeAsync("method", "post");
        await Expect(Page.Locator("#reindex-form")).ToHaveAttributeAsync("action", "/admin/search?handler=Reindex");
        await Expect(Page.Locator("#reindex-button")).ToBeVisibleAsync();
        await Expect(Page.Locator("#reindex-button")).ToBeEnabledAsync();

        // This form has no confirm() dialog: clicking would submit immediately. Never click it.
        // Reload obtains the server's current phase, so the assertion also checks no job started.
        await Page.ReloadAsync();
        await Expect(status).ToHaveAttributeAsync("data-phase", phase!);
        await Expect(Page.Locator("#search-index-admin")).ToHaveAttributeAsync("data-running", "false");
    }
}

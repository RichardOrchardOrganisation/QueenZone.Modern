using System.Net;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.FreddieTributes;

namespace QueenZone.Web.Tests;

public sealed class AdminFreddieTributeRoutesTests : IClassFixture<WebHostVariantCache>, IAsyncLifetime
{
    private readonly VariantWebApplicationFactory factory;

    public AdminFreddieTributeRoutesTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.IsolatedAdminFreddieTributes);
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminFreddieTributesIndex_RendersDuplicateModerationControls()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await client.GetAsync("/admin/freddie-tributes?duplicatesOnly=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Freddie tributes", body);
        Assert.Contains("Repeated tribute", body);
        Assert.Contains("2 matches", body);
        Assert.Contains("/admin/freddie-tributes/9003/hide", body);
        Assert.Contains("/admin/freddie-tributes/9003/delete", body);
    }

    [Fact]
    public async Task AdminFreddieTributes_HideRestoreAndDelete_RoundTrip()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var publicBefore = await client.GetStringAsync("/freddie-mercury-tribute");
        Assert.Contains("Prune me from the public page", publicBefore);

        var hideResponse = await PostActionAsync(client, "/admin/freddie-tributes/9001/hide");
        Assert.Equal(HttpStatusCode.Redirect, hideResponse.StatusCode);

        var publicAfterHide = await client.GetStringAsync("/freddie-mercury-tribute");
        Assert.DoesNotContain("Prune me from the public page", publicAfterHide);

        var hiddenBody = await client.GetStringAsync("/admin/freddie-tributes?visibility=hidden");
        Assert.Contains("Prune me from the public page", hiddenBody);
        Assert.Contains("Restore", hiddenBody);

        var restoreResponse = await PostActionAsync(client, "/admin/freddie-tributes/9001/show");
        Assert.Equal(HttpStatusCode.Redirect, restoreResponse.StatusCode);

        var publicAfterRestore = await client.GetStringAsync("/freddie-mercury-tribute");
        Assert.Contains("Prune me from the public page", publicAfterRestore);

        var deleteResponse = await PostActionAsync(client, "/admin/freddie-tributes/9003/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var adminAfterDelete = await client.GetStringAsync("/admin/freddie-tributes?q=Repeated");
        Assert.DoesNotContain("/admin/freddie-tributes/9003/delete", adminAfterDelete);
        Assert.Contains("9002", adminAfterDelete);
    }

    [Fact]
    public async Task Hide_with_stale_visibility_token_does_not_change_the_row()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var indexPage = await client.GetStringAsync("/admin/freddie-tributes");
        var fields = new Dictionary<string, string>
        {
            [AdminFreddieTributesPageModel.AntiforgeryTokenFieldName] =
                AdminHttpTestHelpers.ExtractAntiforgeryToken(indexPage),
            ["expectedIsVisible"] = "false",
        };

        var response = await client.PostAsync("/admin/freddie-tributes/9001/hide", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var admin = await client.GetStringAsync("/admin/freddie-tributes");
        Assert.Contains(OptimisticConcurrencyException.UserMessage, admin);
        Assert.Contains("Prune me from the public page", await client.GetStringAsync("/freddie-mercury-tribute"));
    }

    [Fact]
    public void Delete_with_stale_visibility_token_leaves_the_row()
    {
        var store = new SharedFreddieTributeStore(
        [
            new FreddieTribute(1, "Name", "Thought", "UK", "24 November 2001", "09:01"),
        ]);
        Assert.Throws<OptimisticConcurrencyException>(() => store.Delete(1, expectedIsVisible: false));
        Assert.NotNull(store.GetById(1));
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string actionPath)
    {
        var indexPage = await client.GetStringAsync("/admin/freddie-tributes");
        var fields = new Dictionary<string, string>
        {
            [AdminFreddieTributesPageModel.AntiforgeryTokenFieldName] =
                AdminHttpTestHelpers.ExtractAntiforgeryToken(indexPage),
        };
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(fields));
    }
}

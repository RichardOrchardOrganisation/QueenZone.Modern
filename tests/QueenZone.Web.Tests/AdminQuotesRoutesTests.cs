using System.Net;
using QueenZone.Web.Pages.Admin.Quotes;

namespace QueenZone.Web.Tests;

public sealed class AdminQuotesRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public AdminQuotesRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AnonymousUserCannotAccessAdminQuotes()
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/quotes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanListSeedQuotes()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quotes", body);
        Assert.Contains("/admin/quotes/new", body);
        Assert.Contains("Freddie Mercury", body);
        Assert.Contains("/admin/quotes/1/edit", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenNewQuoteForm()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/quotes/new");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add quote", body);
        Assert.Contains("Who said it", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenEditFormForSeedQuote()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/quotes/1/edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit quote", body);
        Assert.Contains("Freddie Mercury", body);
    }

    [Fact]
    public async Task AuthorizedAdminGetsNotFoundForMissingQuote()
    {
        var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/quotes/99999/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditPost_SavesValidQuoteAndShowsConfirmation()
    {
        using var client = factory.CreateAdminClient();
        var page = await client.GetStringAsync("/admin/quotes/1/edit");
        var response = await PostEditAsync(client, 1, AdminHttpTestHelpers.ExtractAntiforgeryToken(page),
            "  Updated quote  ", "  Freddie Mercury  ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/quotes/1/edit", response.Headers.Location!.OriginalString);
        var updated = await client.GetStringAsync("/admin/quotes/1/edit");
        Assert.Contains("Updated quote", updated);
        Assert.Contains("Saved quote.", updated);
    }

    [Fact]
    public async Task EditPost_ShowsValidationErrorsAndPreservesDraft()
    {
        using var client = factory.CreateAdminClient();
        var page = await client.GetStringAsync("/admin/quotes/1/edit");
        var response = await PostEditAsync(client, 1, AdminHttpTestHelpers.ExtractAntiforgeryToken(page),
            " ", "Freddie Mercury");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quote text is required.", body);
        Assert.Contains("Freddie Mercury", body);
    }

    [Fact]
    public async Task EditPost_ReturnsNotFoundForMissingId()
    {
        using var client = factory.CreateAdminClient();
        var page = await client.GetStringAsync("/admin/quotes/1/edit");
        var response = await PostEditAsync(client, 99999, AdminHttpTestHelpers.ExtractAntiforgeryToken(page),
            "Quote", "Freddie Mercury");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditPost_RejectsMissingAntiforgeryToken()
    {
        using var client = factory.CreateAdminClient();
        var response = await PostEditAsync(client, 1, null, "Changed quote", "Freddie Mercury");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var page = await client.GetStringAsync("/admin/quotes/1/edit");
        Assert.DoesNotContain("Changed quote", page);
    }

    private static Task<HttpResponseMessage> PostEditAsync(
        HttpClient client, int id, string? token, string text, string whoSaid)
    {
        var fields = new Dictionary<string, string>
        {
            ["text"] = text,
            ["whoSaid"] = whoSaid,
            ["isPublished"] = "true",
        };
        if (token is not null)
        {
            fields[AdminQuotePageModel.AntiforgeryTokenFieldName] = token;
        }

        return client.PostAsync($"/admin/quotes/{id}", new FormUrlEncodedContent(fields));
    }
}

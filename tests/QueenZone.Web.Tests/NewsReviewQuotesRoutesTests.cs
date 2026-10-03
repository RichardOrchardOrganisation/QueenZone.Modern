using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class NewsReviewQuotesRoutesTests
{
    [Fact]
    public async Task Review_displays_preserved_quote_text_source_link_and_context()
    {
        await using var factory = new QueenZoneWebApplicationFactory();
        var repository = factory.Services.GetRequiredService<INewsDiscoveryRepository>();
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(repository);
        var at = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        var runId = await repository.CreateAiRunAsync(new NewsAiRunCreateRequest(candidateId,
            NewsAiRunKind.DraftGeneration, "openrouter", "test-model", "draft-v1", at));
        await repository.CompleteAiRunAsync(runId, new NewsAiRunCompletion(NewsAiRunStatus.Succeeded,
            100, 50, 0.0001m,
            """{"preserved_quotes":[{"speaker":"Brian May","exact_text":"We are ready.","source_url":"https://www.queenonline.com/quote","source_context":"Official announcement."},{"speaker":"Roger Taylor","exact_text":"See you soon."}]}""",
            null, at));
        using var client = factory.CreateAdminClient();
        var response = await client.GetAsync($"/admin/news-discovery/{candidateId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
        Assert.Contains("Preserved quotes", document.Body!.TextContent);
        Assert.Contains("Brian May", document.Body.TextContent);
        Assert.Contains("We are ready.", document.Body.TextContent);
        Assert.Contains("Official announcement.", document.Body.TextContent);
        Assert.Contains("See you soon.", document.Body.TextContent);
        Assert.NotNull(document.QuerySelector("a[href='https://www.queenonline.com/quote']"));
    }
}

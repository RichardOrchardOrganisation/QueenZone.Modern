using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class TriviaPageTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private const string UnpublishedText = "Unpublished draft fact must never render";
    private const string FirstPublishedText = "First published Queen trivia fact";

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory isolatedTrivia;
    private readonly VariantWebApplicationFactory sequentialTrivia;

    public TriviaPageTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        isolatedTrivia = variants.Get(WebHostVariants.IsolatedTrivia);
        sequentialTrivia = variants.Get(WebHostVariants.SequentialTrivia);
    }

    public async Task InitializeAsync()
    {
        await isolatedTrivia.ResetAsync();
        await sequentialTrivia.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Trivia_page_renders_a_published_fact_and_next_fact_form()
    {
        using var client = factory.CreateAnonymousClient();

        var body = await client.GetStringAsync("/trivia");

        Assert.Contains("<title>Queen Trivia | QueenZone</title>", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/trivia"), body);
        Assert.Contains("href=\"/trivia\"", body);
        Assert.Contains("Next fact", body);
        Assert.Contains("handler=Next", body);
        Assert.DoesNotContain("Brian May's Red Special was built with his father", body);
        Assert.True(
            body.Contains("Freddie Mercury was born Farrokh Bulsara", StringComparison.Ordinal)
            || body.Contains("A Night at the Opera takes its title", StringComparison.Ordinal),
            "Expected a published sample trivia fact.");
    }

    [Fact]
    public async Task Trivia_page_does_not_render_unpublished_facts()
    {
        var trivia = isolatedTrivia.Services.GetRequiredService<ITriviaRepository>();
        await trivia.CreateAsync(new AdminTriviaDraft(UnpublishedText, false, "Band", TriviaDifficulty.Hard, "Draft"));
        using var client = isolatedTrivia.CreateAnonymousClient();

        var body = await client.GetStringAsync("/trivia");

        Assert.Contains("No trivia facts have been published yet.", body);
        Assert.DoesNotContain(UnpublishedText, body);
        Assert.DoesNotContain("Next fact", body);
    }

    [Fact]
    public async Task Next_fact_reuses_the_published_pool_without_random_repository_reads()
    {
        var repository = sequentialTrivia.SequentialTrivia!;
        using var client = sequentialTrivia.CreateAnonymousClient();

        var first = await client.GetStringAsync("/trivia");
        Assert.Contains(FirstPublishedText, first);
        Assert.DoesNotContain(UnpublishedText, first);

        using var next = await client.PostAsync(
            "/trivia?handler=Next",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(first),
            }));

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var second = await next.Content.ReadAsStringAsync();
        Assert.Contains(FirstPublishedText, second);
        Assert.DoesNotContain(UnpublishedText, second);
        Assert.Equal(1, repository.AllCallCount);
        Assert.Equal(0, repository.RandomCallCount);
    }
}

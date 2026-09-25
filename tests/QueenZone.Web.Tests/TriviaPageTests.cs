using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class TriviaPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string UnpublishedText = "Unpublished draft fact must never render";
    private const string FirstPublishedText = "First published Queen trivia fact";
    private const string SecondPublishedText = "Second published Queen trivia fact";

    private readonly QueenZoneWebApplicationFactory factory;

    public TriviaPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

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
        using var isolated = IsolatedTrivia(
            new TriviaFactItem(31, UnpublishedText, DateTime.UtcNow, false, "Band", TriviaDifficulty.Hard, "Draft"));
        using var client = isolated.CreateAnonymousClient();

        var body = await client.GetStringAsync("/trivia");

        Assert.Contains("No trivia facts have been published yet.", body);
        Assert.DoesNotContain(UnpublishedText, body);
        Assert.DoesNotContain("Next fact", body);
    }

    [Fact]
    public async Task Next_fact_reuses_the_published_pool_without_random_repository_reads()
    {
        var repository = new SequentialTriviaRepository(
            new TriviaFactItem(41, FirstPublishedText, DateTime.UtcNow, true, "Band", TriviaDifficulty.Easy, null),
            new TriviaFactItem(43, UnpublishedText, DateTime.UtcNow, false, "Band", TriviaDifficulty.Hard, "Draft"));
        using var isolated = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<ITriviaRepository>();
            services.AddSingleton<ITriviaRepository>(repository);
        });
        using var client = isolated.CreateAnonymousClient();

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

    private static QueenZoneWebApplicationFactory IsolatedTrivia(params TriviaFactItem[] facts) =>
        QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<ITriviaRepository>();
            services.AddSingleton<ITriviaRepository>(new InMemoryTriviaRepository(facts));
        });

    private sealed class SequentialTriviaRepository(params TriviaFactItem[] facts) : ITriviaRepository
    {
        public int AllCallCount { get; private set; }

        public int RandomCallCount { get; private set; }

        public Task<IReadOnlyList<TriviaFactItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            AllCallCount++;
            return Task.FromResult<IReadOnlyList<TriviaFactItem>>(facts);
        }

        public Task<TriviaFactItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(facts.SingleOrDefault(fact => fact.Id == id));

        public Task<TriviaFactItem?> GetRandomPublishedAsync(CancellationToken cancellationToken = default)
        {
            RandomCallCount++;
            return Task.FromResult<TriviaFactItem?>(null);
        }

        public Task<int> CreateAsync(AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(int id, AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetPublishedAsync(int id, bool isPublished, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

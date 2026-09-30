using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MemberNewsSuggestionApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public MemberNewsSuggestionApiTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Submit_requires_bearer_token()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        using var headerOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        headerOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());

        foreach (var client in new[] { anonymous, cookieOnly, headerOnly })
        {
            using var response = await client.PostAsJsonAsync(
                MemberApiEndpoints.NewsSuggestionsPath,
                new { url = "https://example.com/needs-a-token" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Submit_returns_created_id_location_and_normalized_url()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(factory, memberId, "News Fan");
        var rawUrl = $"https://example.com/queen-tour-{Guid.NewGuid():N}/?utm_source=share";

        using var response = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url = rawUrl, title = "Tour announced", notes = "From the app" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NewsSuggestionCreatedDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal(NewsSuggestionStatus.Pending, created.Status);
        Assert.Equal(NewsCandidateDedupe.NormalizeCanonicalUrl(rawUrl), created.Url);
        Assert.Equal("Tour announced", created.Title);
        Assert.Equal(
            $"{MemberApiEndpoints.NewsSuggestionsPath}/{created.Id:D}",
            response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INewsSuggestionRepository>();
        var stored = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(memberId, stored!.SubmitterMemberId);
        Assert.Equal(NewsSuggestionStatus.Pending, stored.Status);
        Assert.Equal(created.Url, stored.Url);
        Assert.Equal("From the app", stored.Notes);
    }

    [Fact]
    public async Task Submit_ignores_member_id_in_the_body()
    {
        var jwtMemberId = Guid.NewGuid();
        var bodyMemberId = Guid.NewGuid();
        using var client = CreateBearerClient(factory, jwtMemberId, "Jwt Fan");
        var url = $"https://example.com/body-identity-{Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new
            {
                url,
                title = "Body cannot set member",
                memberId = bodyMemberId,
                submitterMemberId = bodyMemberId,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NewsSuggestionCreatedDto>(JsonOptions);
        Assert.NotNull(created);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INewsSuggestionRepository>();
        var stored = await repository.GetByIdAsync(created!.Id);
        Assert.NotNull(stored);
        Assert.Equal(jwtMemberId, stored!.SubmitterMemberId);
        Assert.NotEqual(bodyMemberId, stored.SubmitterMemberId);
    }

    [Theory]
    [InlineData("", "URL is required.")]
    [InlineData("http://example.com/not-secure", "URL must be a well-formed https:// link.")]
    [InlineData("https://localhost/private", "URL must be a public https:// link.")]
    public async Task Submit_returns_bad_request_for_invalid_urls(string url, string expectedDetail)
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url, title = "Invalid" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedDetail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Submit_returns_bad_request_for_overlong_url()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());
        var url = "https://example.com/" + new string('a', 2000);

        using var response = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("URL must be 2000 characters or fewer.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Submit_returns_conflict_for_active_duplicate()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());
        var url = $"https://example.com/shared-story-{Guid.NewGuid():N}";

        using var first = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url, title = "First" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url = url + "/", title = "Second" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal(NewsSuggestionService.DuplicateActiveMessage, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public void MapNewsSuggestionOutcome_maps_each_closed_variant()
    {
        var suggestion = new NewsSuggestion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/mapped",
            "hash",
            "Mapped",
            null,
            NewsSuggestionStatus.Pending,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var created = Assert.IsType<Created<NewsSuggestionCreatedDto>>(
            MemberApiEndpoints.MapNewsSuggestionOutcome(new SubmitOutcome.Accepted(suggestion)));
        Assert.Equal($"{MemberApiEndpoints.NewsSuggestionsPath}/{suggestion.Id:D}", created.Location);
        Assert.Equal(suggestion.Id, created.Value!.Id);

        AssertProblem(new SubmitOutcome.InvalidField("bad url"), StatusCodes.Status400BadRequest, "bad url");
        AssertProblem(new SubmitOutcome.SignInRequired(), StatusCodes.Status401Unauthorized, "Sign in is required to suggest news.");
        AssertProblem(new SubmitOutcome.DuplicateActive("dup"), StatusCodes.Status409Conflict, "dup");
        AssertProblem(new SubmitOutcome.DailyLimit("limit"), StatusCodes.Status429TooManyRequests, "limit");
    }

    [Fact]
    public async Task Submit_returns_too_many_requests_on_sixth_submit()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(factory, memberId);

        for (var i = 0; i < 5; i++)
        {
            using var ok = await client.PostAsJsonAsync(
                MemberApiEndpoints.NewsSuggestionsPath,
                new { url = $"https://example.com/quota-{memberId:N}-{i}" });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var blocked = await client.PostAsJsonAsync(
            MemberApiEndpoints.NewsSuggestionsPath,
            new { url = $"https://example.com/quota-{memberId:N}-extra" });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Contains("5 news stories per day", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private static void AssertProblem(SubmitOutcome outcome, int statusCode, string detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(MemberApiEndpoints.MapNewsSuggestionOutcome(outcome));
        Assert.Equal(statusCode, problem.StatusCode);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory source,
        Guid memberId,
        string displayName = "News Fan")
    {
        MemberBearerAccounts.Ensure(source.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = source.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = source.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

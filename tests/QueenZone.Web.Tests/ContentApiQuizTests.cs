using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiQuizTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task List_only_returns_published_quizzes()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        await quizzes.CreateAsync(SampleDraft("Draft"), Guid.NewGuid());
        var publishedId = await quizzes.CreateAsync(SampleDraft("Published"), Guid.NewGuid());
        await quizzes.PublishAsync(publishedId);

        using var client = isolated.CreateAnonymousClient();
        var response = await client.GetFromJsonAsync<ApiPagedResponse<QuizListItemDto>>(
            $"{ContentApiEndpoints.RootPath}/quizzes",
            JsonOptions);

        Assert.NotNull(response);
        Assert.Single(response!.Items);
        Assert.Equal(publishedId, response.Items[0].Id);
    }

    [Fact]
    public async Task Detail_hides_the_correct_answer_and_404s_for_drafts()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var draftId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
        var publishedId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
        await quizzes.PublishAsync(publishedId);

        using var client = isolated.CreateAnonymousClient();

        var missing = await client.GetAsync($"{ContentApiEndpoints.RootPath}/quizzes/{draftId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var detail = await client.GetFromJsonAsync<QuizDetailDto>(
            $"{ContentApiEndpoints.RootPath}/quizzes/{publishedId}",
            JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Questions.Count);
        var rawJson = await client.GetStringAsync($"{ContentApiEndpoints.RootPath}/quizzes/{publishedId}");
        Assert.DoesNotContain("isCorrect", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_requires_a_member_and_scores_server_side()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quizId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
        await quizzes.PublishAsync(quizId);
        var play = await quizzes.GetPublishedForPlayAsync(quizId);
        var correctOptionId = play!.Questions[0].Options.First(o => o.Text == "Freddie Mercury").Id;

        using var anonymous = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var unauthorized = await anonymous.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/{quizId}/attempts",
            new QuizSubmitRequestDto([new QuizAnswerSubmissionDto(play.Questions[0].Id, correctOptionId)]));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var memberId = Guid.NewGuid();
        using var member = CreateBearerClient(isolated, memberId, "Quiz Fan");
        var submitted = await member.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/{quizId}/attempts",
            new QuizSubmitRequestDto([new QuizAnswerSubmissionDto(play.Questions[0].Id, correctOptionId)]));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var result = await submitted.Content.ReadFromJsonAsync<QuizResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result!.Recorded);
        Assert.Equal(2, result.Score); // Q1 correct (2 pts), Q2 unanswered
        Assert.Equal(3, result.MaxScore);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(correctOptionId, result.Answers.Single(a => a.QuestionId == play.Questions[0].Id).CorrectOptionId);
    }

    [Fact]
    public async Task Leaderboard_ranks_by_score_and_reports_the_viewers_rank()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quizId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
        await quizzes.PublishAsync(quizId);
        var leader = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        await quizzes.RecordAttemptAsync(quizId, leader, score: 10, correctCount: 2, questionCount: 2);
        await quizzes.RecordAttemptAsync(quizId, viewer, score: 3, correctCount: 1, questionCount: 2);

        using var client = CreateBearerClient(isolated, viewer, "Viewer");
        var response = await client.GetFromJsonAsync<QuizLeaderboardDto>(
            $"{ContentApiEndpoints.RootPath}/quizzes/leaderboard?scope=all",
            JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(2, response!.Top.Count);
        Assert.Equal(10, response.Top[0].Score);
        Assert.NotNull(response.Viewer);
        Assert.Equal(2, response.Viewer!.Rank);
    }

    private static AdminQuizDraft SampleDraft(string title = "Sample quiz") =>
        new(
            title,
            "Description",
            [
                new QuizQuestionDraft(
                    "Who was the lead singer?",
                    2,
                    [new QuizOptionDraft("Freddie Mercury", true), new QuizOptionDraft("Brian May", false)]),
                new QuizQuestionDraft(
                    "What year was Bohemian Rhapsody released?",
                    1,
                    [new QuizOptionDraft("1975", true), new QuizOptionDraft("1980", false)]),
            ]);

    private static QueenZoneWebApplicationFactory IsolatedQuizzes()
    {
        var store = new SharedQuizStore();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedQuizStore>();
            services.RemoveAll<IQuizRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IQuizRepository>(_ => new InMemoryQuizRepository(store));
        });
    }

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory factory,
        Guid memberId,
        string displayName)
    {
        MemberBearerAccounts.Ensure(factory.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

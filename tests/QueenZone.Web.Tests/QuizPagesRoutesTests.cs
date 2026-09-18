using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizPagesRoutesTests
{
    [Fact]
    public async Task List_page_only_shows_published_quizzes()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        await quizzes.CreateAsync(SampleDraft("Hidden draft"), Guid.NewGuid());
        var publishedId = await quizzes.CreateAsync(SampleDraft("Visible quiz"), Guid.NewGuid());
        await quizzes.PublishAsync(publishedId);

        using var client = isolated.CreateAnonymousClient();
        var html = await client.GetStringAsync("/quizzes");

        Assert.Contains("Visible quiz", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden draft", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Play_page_404s_for_a_draft_quiz()
    {
        using var isolated = IsolatedQuizzes();
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var draftId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());

        using var client = isolated.CreateAnonymousClient();
        var response = await client.GetAsync($"/quizzes/{draftId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Play_page_never_leaks_the_correct_answer_before_submit()
    {
        using var isolated = IsolatedQuizzes();
        var quizId = await PublishSampleAsync(isolated);

        using var client = isolated.CreateAnonymousClient();
        var html = await client.GetStringAsync($"/quizzes/{quizId}");

        Assert.Contains("Freddie Mercury", html, StringComparison.Ordinal);
        Assert.DoesNotContain("isCorrect", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anonymous_visitor_can_complete_a_quiz_but_the_attempt_is_not_recorded()
    {
        using var isolated = IsolatedQuizzes();
        var quizId = await PublishSampleAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var formPage = await client.GetStringAsync($"/quizzes/{quizId}");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        var (q1Id, q2Id, correctOptionId) = await GetQuestionAndCorrectOptionAsync(isolated, quizId);

        var response = await client.PostAsync(
            $"/quizzes/{quizId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                [$"answer_{q1Id:N}"] = correctOptionId.ToString("N"),
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("2 / 3 points", body, StringComparison.Ordinal);
        Assert.Contains("Sign in", body, StringComparison.Ordinal);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        Assert.Equal(0, (await quizzes.GetByIdAsync(quizId))!.AttemptCount);
    }

    [Fact]
    public async Task Signed_in_member_completing_a_quiz_is_recorded_and_appears_on_the_leaderboard()
    {
        using var isolated = IsolatedQuizzes();
        var quizId = await PublishSampleAsync(isolated);
        var (q1Id, _, correctOptionId) = await GetQuestionAndCorrectOptionAsync(isolated, quizId);

        using var member = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Quiz Champion");

        var formPage = await member.GetStringAsync($"/quizzes/{quizId}");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        var response = await member.PostAsync(
            $"/quizzes/{quizId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                [$"answer_{q1Id:N}"] = correctOptionId.ToString("N"),
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sign in", body, StringComparison.Ordinal);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        Assert.Equal(1, (await quizzes.GetByIdAsync(quizId))!.AttemptCount);

        var leaderboard = await member.GetStringAsync("/quizzes/leaderboard?scope=all");
        Assert.Contains("Quiz Champion", leaderboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sprint_uses_only_published_questions_and_shows_a_visible_countdown()
    {
        using var isolated = IsolatedQuizzes();
        using (var scope = isolated.Services.CreateScope())
        {
            var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
            await quizzes.CreateAsync(SampleDraft("Hidden draft"), Guid.NewGuid());
            var publishedId = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
            await quizzes.PublishAsync(publishedId);
        }

        using var client = isolated.CreateAnonymousClient();
        var landing = await client.GetStringAsync("/quizzes/sprint");
        Assert.Contains("Start 60-second sprint", landing, StringComparison.Ordinal);
        var round = await StartSprintAsync(client, landing);

        Assert.Contains("data-sprint-seconds", round, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", round, StringComparison.Ordinal);
        Assert.Contains("Who was the lead singer?", round, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(round, "data-sprint-question").Count);
        Assert.DoesNotContain("isCorrect", round, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sprint_scores_correct_answers_without_deducting_for_wrong_answers()
    {
        using var isolated = IsolatedQuizzes();
        var quizId = await PublishSampleAsync(isolated);
        var (q1Id, q2Id, correctOptionId) = await GetQuestionAndCorrectOptionAsync(isolated, quizId);
        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var play = await quizzes.GetPublishedForPlayAsync(quizId);
        var wrongOptionId = play!.Questions[1].Options.First(option => option.Text == "1980").Id;

        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var landing = await client.GetStringAsync("/quizzes/sprint");
        var round = await StartSprintAsync(client, landing);
        var response = await client.PostAsync("/quizzes/sprint?handler=Finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(round),
            ["ticket"] = ExtractTicket(round),
            [$"answer_{q1Id:N}"] = correctOptionId.ToString("N"),
            [$"answer_{q2Id:N}"] = wrongOptionId.ToString("N"),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("1 correct from 2 answered", body, StringComparison.Ordinal);
        Assert.Contains("Correct answer: 1975", body, StringComparison.Ordinal);
        Assert.Equal(0, (await quizzes.GetByIdAsync(quizId))!.AttemptCount);
    }

    [Fact]
    public async Task Sprint_rejects_results_after_the_deadline()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero));
        using var isolated = IsolatedQuizzes(clock);
        await PublishSampleAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var landing = await client.GetStringAsync("/quizzes/sprint");
        var round = await StartSprintAsync(client, landing);
        clock.Advance(TimeSpan.FromSeconds(66));

        var response = await client.PostAsync("/quizzes/sprint?handler=Finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(round),
            ["ticket"] = ExtractTicket(round),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Round expired", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sprint_rejects_a_tampered_answer_ticket()
    {
        using var isolated = IsolatedQuizzes();
        await PublishSampleAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var landing = await client.GetStringAsync("/quizzes/sprint");
        var round = await StartSprintAsync(client, landing);

        var response = await client.PostAsync("/quizzes/sprint?handler=Finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(round),
            ["ticket"] = ExtractTicket(round) + "tampered",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> StartSprintAsync(HttpClient client, string landing)
    {
        var response = await client.PostAsync("/quizzes/sprint?handler=Start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(landing),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string ExtractTicket(string html) =>
        Regex.Match(html, "name=\"ticket\" value=\"([^\"]+)\"").Groups[1].Value;

    private static QueenZoneWebApplicationFactory IsolatedQuizzes(TimeProvider? clock = null)
    {
        var store = new SharedQuizStore();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedQuizStore>();
            services.RemoveAll<IQuizRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IQuizRepository>(_ => new InMemoryQuizRepository(store));
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        });
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
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

    private static async Task<Guid> PublishSampleAsync(QueenZoneWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var id = await quizzes.CreateAsync(SampleDraft(), Guid.NewGuid());
        await quizzes.PublishAsync(id);
        return id;
    }

    private static async Task<(Guid Question1Id, Guid Question2Id, Guid CorrectOptionId)> GetQuestionAndCorrectOptionAsync(
        QueenZoneWebApplicationFactory factory,
        Guid quizId)
    {
        using var scope = factory.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var play = await quizzes.GetPublishedForPlayAsync(quizId);
        var correctOptionId = play!.Questions[0].Options.First(o => o.Text == "Freddie Mercury").Id;
        return (play.Questions[0].Id, play.Questions[1].Id, correctOptionId);
    }
}

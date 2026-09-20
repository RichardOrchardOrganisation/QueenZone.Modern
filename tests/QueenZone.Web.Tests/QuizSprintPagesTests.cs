using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintPagesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Guest_results_prompt_to_sign_in_and_are_not_ranked()
    {
        using var isolated = IsolatedQuizzes();
        await PublishPoolAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var landing = await client.GetStringAsync("/quizzes/sprint");
        Assert.Contains("Sign in to be ranked", landing, StringComparison.Ordinal);
        var round = await StartAsync(client, landing);
        var body = await FinishAsync(client, round, answerCorrectly: true);

        Assert.Contains("only added to the leaderboard if you are signed in", body, StringComparison.Ordinal);
        Assert.Contains("No scores on the board yet today.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_in_run_is_ranked_and_shown_on_results_leaderboard_and_homepage()
    {
        using var isolated = IsolatedQuizzes();
        await PublishPoolAsync(isolated);
        using var member = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Quiz Champion");

        var landing = await member.GetStringAsync("/quizzes/sprint");
        Assert.DoesNotContain("Sign in to be ranked", landing, StringComparison.Ordinal);
        var round = await StartAsync(member, landing);
        var body = await FinishAsync(member, round, answerCorrectly: true);

        Assert.Contains("on today's leaderboard (rank #1)", body, StringComparison.Ordinal);
        Assert.Contains("qz-sprint-board__row--you", body, StringComparison.Ordinal);

        var leaderboard = await member.GetStringAsync("/quizzes/leaderboard");
        Assert.Contains("1 member ranked today.", leaderboard, StringComparison.Ordinal);
        var home = await member.GetStringAsync("/");
        Assert.Contains("The sixty-second Queen quiz", home, StringComparison.Ordinal);
        Assert.Contains("1 member has played today.", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_results_offer_to_save_the_score_and_signing_in_adds_it()
    {
        using var isolated = IsolatedQuizzes();
        await PublishPoolAsync(isolated);
        using var guest = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var landing = await guest.GetStringAsync("/quizzes/sprint");
        var results = await FinishAsync(guest, await StartAsync(guest, landing), answerCorrectly: true);

        var link = Regex.Match(results, "href=\"(/account/login\\?returnUrl=[^\"]+)\"[^>]*>Sign in to save this score");
        Assert.True(link.Success, "results should link to sign in with the claim token");
        var encoded = Regex.Match(System.Net.WebUtility.HtmlDecode(link.Groups[1].Value), "returnUrl=(.+)$").Groups[1].Value;
        var returnUrl = Uri.UnescapeDataString(encoded);
        Assert.StartsWith("/quizzes/sprint?claim=", returnUrl, StringComparison.Ordinal);

        using var member = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Late Signer");
        var saved = await member.GetStringAsync(returnUrl);
        Assert.Contains("Your score of 3 was added to the leaderboard (rank #1 today).", saved, StringComparison.Ordinal);

        var replay = await member.GetStringAsync(returnUrl);
        Assert.Contains("already on the leaderboard", replay, StringComparison.Ordinal);

        var signedOut = await guest.GetStringAsync(returnUrl);
        Assert.Contains("Sign in to save your score", signedOut, StringComparison.Ordinal);
        var tampered = await member.GetStringAsync("/quizzes/sprint?claim=nonsense");
        Assert.Contains("save that score", tampered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_endpoint_reveals_correctness_for_a_live_round_only()
    {
        using var isolated = IsolatedQuizzes();
        await PublishPoolAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var round = (await (await client.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;
        var question = round.Questions[0];
        var right = question.Options.First(option => option.Text.StartsWith("Right", StringComparison.Ordinal));
        var wrong = question.Options.First(option => option.Text.StartsWith("Wrong", StringComparison.Ordinal));
        var url = $"{ContentApiEndpoints.RootPath}/quizzes/sprint/answer";

        var correct = await (await client.PostAsJsonAsync(url, new SprintAnswerRequestDto(round.Ticket, question.Id, right.Id)))
            .Content.ReadFromJsonAsync<SprintAnswerResultDto>(JsonOptions);
        var incorrect = await (await client.PostAsJsonAsync(url, new SprintAnswerRequestDto(round.Ticket, question.Id, wrong.Id)))
            .Content.ReadFromJsonAsync<SprintAnswerResultDto>(JsonOptions);

        Assert.True(correct!.IsCorrect);
        Assert.False(incorrect!.IsCorrect);
        Assert.Equal(right.Id, incorrect.CorrectOptionId);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(url, new SprintAnswerRequestDto("bad", question.Id, right.Id))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(url, new SprintAnswerRequestDto(round.Ticket, question.Id, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Api_daily_board_lists_a_recorded_member()
    {
        using var isolated = IsolatedQuizzes();
        var memberId = Guid.NewGuid();
        using (var scope = isolated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IQuizRepository>()
                .RecordSprintRunAsync(memberId, new QuizSprintScore(7, 5, 6, 5));
        }

        using var client = isolated.CreateAnonymousClient();
        var board = await client.GetFromJsonAsync<SprintDailyBoardDto>(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/daily",
            JsonOptions);

        Assert.Equal(1, board!.PlayersToday);
        Assert.Equal(7, board.Top[0].Score);
        Assert.Equal(5, board.Top[0].BestStreak);
    }

    [Fact]
    public async Task Leaderboard_offers_today_and_all_time_and_the_api_scopes_match()
    {
        using var isolated = IsolatedQuizzes();
        var memberId = Guid.NewGuid();
        using (var scope = isolated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IQuizRepository>()
                .RecordSprintRunAsync(memberId, new QuizSprintScore(9, 7, 8, 6));
        }

        using var client = isolated.CreateAnonymousClient();
        var today = await client.GetStringAsync("/quizzes/leaderboard");
        var allTime = await client.GetStringAsync("/quizzes/leaderboard?scope=all");
        Assert.Contains("Today&#x27;s leaderboard", today, StringComparison.Ordinal);
        Assert.Contains("Best runs", allTime, StringComparison.Ordinal);
        var total = await client.GetStringAsync("/quizzes/leaderboard?scope=total");
        Assert.Contains("Total points", total, StringComparison.Ordinal);
        Assert.Contains("1 run &middot; best streak 6", total.Replace("·", "&middot;"), StringComparison.Ordinal);
        Assert.Contains("href=\"/quizzes/leaderboard?scope=all\"", today, StringComparison.Ordinal);
        Assert.Contains("1 member ranked.", allTime, StringComparison.Ordinal);

        var api = $"{ContentApiEndpoints.RootPath}/quizzes/sprint/leaderboard";
        var daily = await client.GetFromJsonAsync<SprintBoardDto>(api, JsonOptions);
        var all = await client.GetFromJsonAsync<SprintBoardDto>($"{api}?scope=all", JsonOptions);
        Assert.Equal(("daily", 1, 9), (daily!.Scope, daily.Players, daily.Top[0].Score));
        Assert.Equal(("all", 1, 9), (all!.Scope, all.Players, all.Top[0].Score));
        var totals = await client.GetFromJsonAsync<SprintBoardDto>($"{api}?scope=total", JsonOptions);
        Assert.Equal(("total", 1, 9, 1), (totals!.Scope, totals.Players, totals.Top[0].Score, totals.Top[0].Runs));
    }

    private static async Task<string> StartAsync(HttpClient client, string landing)
    {
        var response = await client.PostAsync("/quizzes/sprint?handler=Start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(landing),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> FinishAsync(HttpClient client, string round, bool answerCorrectly)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(round),
            ["ticket"] = Regex.Match(round, "name=\"ticket\" value=\"([^\"]+)\"").Groups[1].Value,
        };

        // Each question renders its options in a shuffled order; the correct ones are labelled "Right".
        foreach (Match question in Regex.Matches(round, "<fieldset[^>]*data-question-id=\"[^\"]+\".*?</fieldset>", RegexOptions.Singleline))
        {
            var options = Regex.Matches(
                question.Value,
                "name=\"(answer_[0-9a-f]+)\" value=\"([0-9a-f]+)\".*?qz-sprint__answer-text\">([^<]+)<",
                RegexOptions.Singleline);
            var pick = options.First(option => option.Groups[3].Value.StartsWith(answerCorrectly ? "Right" : "Wrong", StringComparison.Ordinal));
            form[pick.Groups[1].Value] = pick.Groups[2].Value;
        }

        var response = await client.PostAsync("/quizzes/sprint?handler=Finish", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task PublishPoolAsync(QueenZoneWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var id = await quizzes.CreateAsync(
            new AdminQuizDraft(
                "Sprint pool",
                null,
                Enumerable.Range(1, 3)
                    .Select(n => new QuizQuestionDraft(
                        $"Question {n}?",
                        1,
                        [new QuizOptionDraft($"Right {n}", true), new QuizOptionDraft($"Wrong {n}", false)]))
                    .ToList()),
            Guid.NewGuid());
        await quizzes.PublishAsync(id);
    }

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
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintRunTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Ef_repository_persists_a_sprint_run()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var memberId = Guid.NewGuid();

        await repository.RecordSprintRunAsync(memberId, new QuizSprintScore(10, 8, 9, 4));

        var run = await dbContext.QuizSprintRuns.SingleAsync();
        Assert.Equal(memberId, run.MemberAccountId);
        Assert.Equal((10, 8, 9, 4), (run.Score, run.CorrectCount, run.AnsweredCount, run.BestStreak));
    }

    [Fact]
    public async Task Ef_all_time_board_ranks_each_members_best_run_and_includes_the_viewer_outside_the_top()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var leader = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var viewer = Guid.NewGuid();

        await repository.RecordSprintRunAsync(leader, new QuizSprintScore(4, 4, 4, 4));
        await repository.RecordSprintRunAsync(leader, new QuizSprintScore(20, 12, 13, 9));
        await repository.RecordSprintRunAsync(middle, new QuizSprintScore(10, 8, 9, 5));
        await repository.RecordSprintRunAsync(viewer, new QuizSprintScore(2, 2, 3, 2));

        var board = await repository.GetSprintBoardAsync(QuizSprintBoardScope.AllTime, viewer, top: 1);

        Assert.Equal(3, board.Players);
        var only = Assert.Single(board.Top);
        Assert.Equal((leader, 1, 20, 9), (only.MemberAccountId, only.Rank, only.Score, only.BestStreak));
        Assert.Equal((viewer, 3, 2), (board.Viewer!.MemberAccountId, board.Viewer.Rank, board.Viewer.Score));

        var withoutViewer = await repository.GetSprintBoardAsync(QuizSprintBoardScope.AllTime, null, top: 10);
        Assert.Equal([leader, middle, viewer], withoutViewer.Top.Select(entry => entry.MemberAccountId));
        Assert.Null(withoutViewer.Viewer);
    }

    [Fact]
    public async Task Ef_total_board_sums_points_across_runs_and_counts_them()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var grinder = Guid.NewGuid();
        var sprinter = Guid.NewGuid();

        // The grinder never has the best single run, but wins on total points.
        await repository.RecordSprintRunAsync(grinder, new QuizSprintScore(8, 6, 7, 3));
        await repository.RecordSprintRunAsync(grinder, new QuizSprintScore(9, 7, 8, 5));
        await repository.RecordSprintRunAsync(grinder, new QuizSprintScore(7, 5, 6, 2));
        await repository.RecordSprintRunAsync(sprinter, new QuizSprintScore(20, 12, 13, 9));

        var total = await repository.GetSprintBoardAsync(QuizSprintBoardScope.Total, sprinter, top: 1);
        var best = await repository.GetSprintBoardAsync(QuizSprintBoardScope.AllTime, null);

        Assert.Equal(2, total.Players);
        var leader = Assert.Single(total.Top);
        Assert.Equal((grinder, 24, 3, 5), (leader.MemberAccountId, leader.Score, leader.Runs, leader.BestStreak));
        Assert.Equal((2, 20, 1), (total.Viewer!.Rank, total.Viewer.Score, total.Viewer.Runs));
        Assert.Equal(sprinter, best.Top[0].MemberAccountId);
        Assert.Equal(3, best.Top[1].Runs);
    }

    [Fact]
    public async Task In_memory_total_board_sums_points_across_runs()
    {
        var repository = new InMemoryQuizRepository(new SharedQuizStore());
        var grinder = Guid.NewGuid();
        var sprinter = Guid.NewGuid();
        await repository.RecordSprintRunAsync(grinder, new QuizSprintScore(8, 6, 7, 3));
        await repository.RecordSprintRunAsync(grinder, new QuizSprintScore(9, 7, 8, 5));
        await repository.RecordSprintRunAsync(sprinter, new QuizSprintScore(12, 8, 9, 6));

        var board = await repository.GetSprintBoardAsync(QuizSprintBoardScope.Total, grinder);

        Assert.Equal([grinder, sprinter], board.Top.Select(entry => entry.MemberAccountId));
        Assert.Equal((17, 2), (board.Top[0].Score, board.Top[0].Runs));
        Assert.Equal(1, board.Viewer!.Rank);
    }

    [Fact]
    public async Task Ef_claim_is_idempotent_on_the_run_id()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var runId = Guid.NewGuid();
        var completedAt = new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

        Assert.True(await repository.ClaimSprintRunAsync(runId, Guid.NewGuid(), new QuizSprintScore(5, 4, 5, 3), completedAt));
        Assert.False(await repository.ClaimSprintRunAsync(runId, Guid.NewGuid(), new QuizSprintScore(5, 4, 5, 3), completedAt));

        var run = await dbContext.QuizSprintRuns.SingleAsync();
        Assert.Equal((runId, 5, completedAt), (run.Id, run.Score, run.CompletedAt));
    }

    [Fact]
    public async Task Ef_all_time_board_is_empty_without_runs()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        var board = await new EfQuizRepository(dbContext, TimeProvider.System)
            .GetSprintBoardAsync(QuizSprintBoardScope.AllTime, Guid.NewGuid());

        Assert.Empty(board.Top);
        Assert.Null(board.Viewer);
        Assert.Equal(0, board.Players);
    }

    [Fact]
    public async Task In_memory_all_time_board_keeps_yesterdays_runs()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryQuizRepository(new SharedQuizStore(), clock);
        var old = Guid.NewGuid();
        await repository.RecordSprintRunAsync(old, new QuizSprintScore(30, 20, 20, 20));
        clock.Advance(TimeSpan.FromDays(1));

        Assert.Empty((await repository.GetSprintBoardAsync(QuizSprintBoardScope.Daily, null)).Top);
        var allTime = await repository.GetSprintBoardAsync(QuizSprintBoardScope.AllTime, null);
        Assert.Equal(old, Assert.Single(allTime.Top).MemberAccountId);
    }

    [Fact]
    public async Task Daily_board_uses_each_members_best_run_today_and_ignores_yesterday()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 19, 23, 59, 0, TimeSpan.Zero));
        var repository = new InMemoryQuizRepository(new SharedQuizStore(), clock);
        var leader = Guid.NewGuid();
        var viewer = Guid.NewGuid();

        await repository.RecordSprintRunAsync(Guid.NewGuid(), new QuizSprintScore(30, 20, 20, 20));
        clock.Advance(TimeSpan.FromHours(9));
        await repository.RecordSprintRunAsync(leader, new QuizSprintScore(10, 8, 9, 4));
        await repository.RecordSprintRunAsync(leader, new QuizSprintScore(7, 6, 8, 3));
        await repository.RecordSprintRunAsync(viewer, new QuizSprintScore(2, 2, 3, 2));

        var board = await repository.GetSprintBoardAsync(QuizSprintBoardScope.Daily, viewer, top: 1);

        Assert.Equal(2, board.Players);
        var only = Assert.Single(board.Top);
        Assert.Equal(leader, only.MemberAccountId);
        Assert.Equal(10, only.Score);
        Assert.Equal(2, board.Viewer!.Rank);
    }

    [Fact]
    public async Task In_memory_daily_board_ranks_by_best_score()
    {
        var repository = new InMemoryQuizRepository(new SharedQuizStore());
        var low = Guid.NewGuid();
        var high = Guid.NewGuid();
        await repository.RecordSprintRunAsync(low, new QuizSprintScore(3, 3, 3, 3));
        await repository.RecordSprintRunAsync(high, new QuizSprintScore(9, 6, 6, 6));

        var board = await repository.GetSprintBoardAsync(QuizSprintBoardScope.Daily, null);

        Assert.Equal([high, low], board.Top.Select(entry => entry.MemberAccountId));
        Assert.Null(board.Viewer);
    }

    [Fact]
    public async Task Api_start_hides_the_answer_key_and_finish_scores_for_a_member()
    {
        using var isolated = IsolatedQuizzes();
        await PublishThreeQuestionsAsync(isolated);
        using var client = CreateBearerClient(isolated, Guid.NewGuid(), "Sprinter");

        var startResponse = await client.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null);
        var rawStart = await startResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("isCorrect", rawStart, StringComparison.OrdinalIgnoreCase);
        var round = JsonSerializer.Deserialize<SprintRoundDto>(rawStart, JsonOptions)!;
        Assert.Equal(60, round.DurationSeconds);

        var finish = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, CorrectAnswers(round)));
        var result = await finish.Content.ReadFromJsonAsync<SprintResultDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        Assert.Equal(3, result!.Correct);
        Assert.Equal(3, result.Points);
        Assert.Equal(3, result.BestStreak);
        Assert.True(result.Recorded);
        Assert.Equal(1, result.Rank);

        var board = await client.GetFromJsonAsync<SprintDailyBoardDto>($"{ContentApiEndpoints.RootPath}/quizzes/sprint/daily", JsonOptions);
        Assert.Equal(1, board!.PlayersToday);
        Assert.Equal(3, board.Top[0].Score);
        Assert.NotNull(board.Viewer);
    }

    [Fact]
    public async Task Api_finish_for_anonymous_callers_scores_but_does_not_record()
    {
        using var isolated = IsolatedQuizzes();
        await PublishThreeQuestionsAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var round = (await (await client.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;
        var finish = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, CorrectAnswers(round)));
        var result = (await finish.Content.ReadFromJsonAsync<SprintResultDto>(JsonOptions))!;

        Assert.Equal(3, result.Points);
        Assert.False(result.Recorded);
        Assert.Null(result.Rank);
        var board = await client.GetFromJsonAsync<SprintDailyBoardDto>($"{ContentApiEndpoints.RootPath}/quizzes/sprint/daily", JsonOptions);
        Assert.Equal(0, board!.PlayersToday);
    }

    [Fact]
    public async Task Guest_run_can_be_claimed_once_after_signing_in()
    {
        using var isolated = IsolatedQuizzes();
        await PublishThreeQuestionsAsync(isolated);
        using var guest = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var round = (await (await guest.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;
        var finished = (await (await guest.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, CorrectAnswers(round))))
            .Content.ReadFromJsonAsync<SprintResultDto>(JsonOptions))!;
        Assert.False(finished.Recorded);
        Assert.False(string.IsNullOrEmpty(finished.ClaimToken));

        var claimUrl = $"{ContentApiEndpoints.RootPath}/quizzes/sprint/claim";
        using var member = CreateBearerClient(isolated, Guid.NewGuid(), "Late Signer");
        var claimed = (await (await member.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto(finished.ClaimToken)))
            .Content.ReadFromJsonAsync<SprintClaimResultDto>(JsonOptions))!;
        Assert.Equal(("claimed", 3, 1), (claimed.Status, claimed.Points, claimed.Rank));

        var again = (await (await member.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto(finished.ClaimToken)))
            .Content.ReadFromJsonAsync<SprintClaimResultDto>(JsonOptions))!;
        Assert.Equal("already_claimed", again.Status);

        // A second member cannot re-claim the same run, and the board holds it exactly once.
        using var other = CreateBearerClient(isolated, Guid.NewGuid(), "Other");
        var stolen = (await (await other.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto(finished.ClaimToken)))
            .Content.ReadFromJsonAsync<SprintClaimResultDto>(JsonOptions))!;
        Assert.Equal("already_claimed", stolen.Status);
        var board = await member.GetFromJsonAsync<SprintDailyBoardDto>($"{ContentApiEndpoints.RootPath}/quizzes/sprint/daily", JsonOptions);
        Assert.Equal(1, board!.PlayersToday);
        Assert.Equal(3, board.Top[0].Score);
    }

    [Fact]
    public async Task Claim_requires_sign_in_and_rejects_bad_or_expired_tokens()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var isolated = IsolatedQuizzes(clock);
        await PublishThreeQuestionsAsync(isolated);
        using var guest = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var claimUrl = $"{ContentApiEndpoints.RootPath}/quizzes/sprint/claim";

        var anonymous = await guest.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto("x"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var member = CreateBearerClient(isolated, Guid.NewGuid(), "Member");
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto("tampered"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto(null))).StatusCode);

        var round = (await (await guest.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;
        var finished = (await (await guest.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, CorrectAnswers(round))))
            .Content.ReadFromJsonAsync<SprintResultDto>(JsonOptions))!;
        clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal(HttpStatusCode.Gone, (await member.PostAsJsonAsync(claimUrl, new SprintClaimRequestDto(finished.ClaimToken))).StatusCode);
    }

    [Fact]
    public async Task Signed_in_finish_returns_no_claim_token()
    {
        using var isolated = IsolatedQuizzes();
        await PublishThreeQuestionsAsync(isolated);
        using var member = CreateBearerClient(isolated, Guid.NewGuid(), "Member");
        var round = (await (await member.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;

        var finished = (await (await member.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, CorrectAnswers(round))))
            .Content.ReadFromJsonAsync<SprintResultDto>(JsonOptions))!;

        Assert.True(finished.Recorded);
        Assert.Null(finished.ClaimToken);
    }

    [Fact]
    public async Task Api_finish_rejects_a_bad_ticket_and_start_404s_without_questions()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var empty = await client.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null);
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);

        var bad = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto("not-a-ticket", []));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var missing = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Api_finish_after_the_deadline_returns_gone()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));
        using var isolated = IsolatedQuizzes(clock);
        await PublishThreeQuestionsAsync(isolated);
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        var round = (await (await client.PostAsync($"{ContentApiEndpoints.RootPath}/quizzes/sprint/start", null))
            .Content.ReadFromJsonAsync<SprintRoundDto>(JsonOptions))!;

        clock.Advance(TimeSpan.FromSeconds(66));
        var finish = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/quizzes/sprint/finish",
            new SprintFinishRequestDto(round.Ticket, []));

        Assert.Equal(HttpStatusCode.Gone, finish.StatusCode);
    }

    private static List<QuizAnswerSubmissionDto> CorrectAnswers(SprintRoundDto round) =>
        round.Questions
            .Select(question => new QuizAnswerSubmissionDto(
                question.Id,
                question.Options.First(option => option.Text.StartsWith("Right", StringComparison.Ordinal)).Id))
            .ToList();

    private static async Task PublishThreeQuestionsAsync(QueenZoneWebApplicationFactory factory)
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

    private static HttpClient CreateBearerClient(QueenZoneWebApplicationFactory factory, Guid memberId, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}

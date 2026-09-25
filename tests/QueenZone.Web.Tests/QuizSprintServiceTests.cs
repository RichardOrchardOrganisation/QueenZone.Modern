using Microsoft.AspNetCore.DataProtection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintServiceTests
{
    [Fact]
    public async Task Guest_is_scored_without_recording_and_member_is_recorded()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        var quizzes = new InMemoryQuizRepository(new SharedQuizStore(), clock);
        var members = new InMemoryMemberAccountRepository();
        var service = new QuizSprintService(quizzes, members, DataProtectionProvider.Create("QuizSprintServiceTests"), clock);
        var quizId = await quizzes.CreateAsync(new AdminQuizDraft("Sprint", null,
            Enumerable.Range(1, 3).Select(index => new QuizQuestionDraft($"Question {index}", 1,
                [new QuizOptionDraft("Right", true), new QuizOptionDraft("Wrong", false)])).ToList()), Guid.NewGuid());
        await quizzes.PublishAsync(quizId);

        var round = await service.StartAsync(CancellationToken.None);
        Assert.NotNull(round);
        Assert.Equal(QuizSprintService.DurationSeconds * 1000,
            round.ExpiresAtUnixMilliseconds - round.ServerNowUnixMilliseconds);
        var selections = round.Questions.ToDictionary(question => question.Id,
            question => question.Options.Single(option => option.Text == "Right").Id);

        var guest = await service.FinishAsync(round.Ticket, selections, null, CancellationToken.None);
        Assert.Equal(SprintFinishStatus.Completed, guest.Status);
        Assert.Equal(3, guest.Result!.Points);
        Assert.False(guest.Result.Recorded);
        Assert.NotNull(guest.Result.ClaimToken);
        Assert.Empty((await quizzes.GetSprintBoardAsync(QuizSprintBoardScope.Daily, null)).Top);

        var memberId = Guid.NewGuid();
        var member = await service.FinishAsync(round.Ticket, selections, memberId, CancellationToken.None);
        Assert.Equal(SprintFinishStatus.Completed, member.Status);
        Assert.True(member.Result!.Recorded);
        Assert.Equal(1, member.Result.Rank);
        Assert.Null(member.Result.ClaimToken);
        Assert.Single((await quizzes.GetSprintBoardAsync(QuizSprintBoardScope.Daily, memberId)).Top);
    }

    [Fact]
    public async Task Expired_round_cannot_be_scored_or_recorded()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        var quizzes = new InMemoryQuizRepository(new SharedQuizStore(), clock);
        var service = new QuizSprintService(quizzes, new InMemoryMemberAccountRepository(),
            DataProtectionProvider.Create("QuizSprintServiceTests"), clock);
        var quizId = await quizzes.CreateAsync(new AdminQuizDraft("Sprint", null,
            [new QuizQuestionDraft("Question", 1,
                [new QuizOptionDraft("Right", true), new QuizOptionDraft("Wrong", false)])]), Guid.NewGuid());
        await quizzes.PublishAsync(quizId);
        var round = await service.StartAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(66));
        var outcome = await service.FinishAsync(round!.Ticket, new Dictionary<Guid, Guid>(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SprintFinishStatus.Expired, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.Empty((await quizzes.GetSprintBoardAsync(QuizSprintBoardScope.Daily, null)).Top);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}

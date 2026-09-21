using Microsoft.AspNetCore.Http;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizSprintSeenQuestionsTests
{
    private static List<QuizSprintQuestion> Pool(int count) =>
        Enumerable.Range(0, count).Select(_ => new QuizSprintQuestion(Guid.NewGuid(), "Q", [])).ToList();

    [Fact]
    public void Round_avoids_seen_questions_when_enough_fresh_ones_remain()
    {
        var pool = Pool(200);
        var seen = pool.Take(100).Select(question => QuizSprintSeenQuestions.Key(question.Id)).ToList();

        var round = QuizSprintService.PickRoundQuestions(pool.ToList(), seen);

        Assert.Equal(60, round.Count);
        Assert.DoesNotContain(round, question => seen.Contains(QuizSprintSeenQuestions.Key(question.Id)));
    }

    [Fact]
    public void Round_tops_up_with_the_oldest_seen_questions_when_fresh_ones_run_short()
    {
        var pool = Pool(70);
        var seen = pool.Select(question => QuizSprintSeenQuestions.Key(question.Id)).ToList();
        var freshOnes = Pool(40);
        var all = pool.Concat(freshOnes).ToList();

        var round = QuizSprintService.PickRoundQuestions(all, seen);

        Assert.Equal(60, round.Count);
        Assert.All(freshOnes, fresh => Assert.Contains(round, question => question.Id == fresh.Id));
        // 20 stale slots go to the 20 oldest-seen questions.
        Assert.All(pool.Take(20), old => Assert.Contains(round, question => question.Id == old.Id));
    }

    [Fact]
    public void Cookie_round_trips_and_keeps_only_the_newest_entries()
    {
        var context = new DefaultHttpContext();
        var first = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();
        var second = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();

        QuizSprintSeenQuestions.Remember(context, first);
        Replay(context);
        QuizSprintSeenQuestions.Remember(context, second);
        Replay(context);

        var keys = QuizSprintSeenQuestions.Read(context.Request);
        Assert.Equal(QuizSprintSeenQuestions.MaxRemembered, keys.Count);
        Assert.Equal(second.Select(QuizSprintSeenQuestions.Key), keys.TakeLast(300));
    }

    [Fact]
    public void Malformed_cookie_reads_as_empty()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{QuizSprintSeenQuestions.CookieName}=not-valid!";

        Assert.Empty(QuizSprintSeenQuestions.Read(context.Request));
    }

    /// <summary>Feeds the cookie just written to the response back in as the next request's cookie.</summary>
    private static void Replay(DefaultHttpContext context)
    {
        var setCookie = context.Response.Headers.SetCookie.ToString();
        var pair = setCookie.Split(';')[0];
        context.Request.Headers.Cookie = pair;
        context.Response.Headers.Remove("Set-Cookie");
    }
}

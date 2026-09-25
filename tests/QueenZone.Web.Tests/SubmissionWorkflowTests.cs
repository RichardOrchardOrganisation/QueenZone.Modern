using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Characterisation tests for the member-submission status sets and transition rules shared
/// by photo, fan-performance, quiz-question and trivia-fact submissions.
/// </summary>
public sealed class SubmissionWorkflowTests
{
    public static TheoryData<string> Kinds => new() { "photo", "fan-performance", "quiz-question", "trivia-fact" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Status_set_exposes_the_known_statuses_and_normalizes_case_and_whitespace(string kind)
    {
        var subject = Subject.For(kind);

        Assert.Equal(subject.AllStatuses, subject.All);
        foreach (var status in subject.AllStatuses)
        {
            Assert.True(subject.IsKnown(status));
            Assert.True(subject.IsKnown($"  {status.ToUpperInvariant()} "));
            Assert.Equal(status, subject.Normalize($"  {status.ToLowerInvariant()} "));
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Status_set_rejects_unknown_and_blank_statuses(string kind)
    {
        var subject = Subject.For(kind);

        Assert.False(subject.IsKnown(null));
        Assert.False(subject.IsKnown(""));
        Assert.False(subject.IsKnown("   "));
        Assert.False(subject.IsKnown("Bogus"));

        var ex = Assert.Throws<ArgumentException>(() => subject.Normalize("Bogus"));
        Assert.Equal("status", ex.ParamName);
        Assert.StartsWith($"Unknown {subject.Noun} status 'Bogus'.", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Pending_can_be_approved_or_rejected_but_terminal_states_cannot_move(string kind)
    {
        var subject = Subject.For(kind);

        Assert.True(subject.CanTransition("Pending", "Approved"));
        Assert.True(subject.CanTransition("pending", " REJECTED "));
        Assert.False(subject.CanTransition("Approved", "Pending"));
        Assert.False(subject.CanTransition("Rejected", "Approved"));
        Assert.False(subject.CanTransition("Pending", "Bogus"));
        Assert.False(subject.CanTransition("Bogus", "Approved"));
        Assert.False(subject.CanTransition("Pending", "Pending"));

        Assert.True(subject.IsTerminal("Approved"));
        Assert.True(subject.IsTerminal("rejected"));
        Assert.False(subject.IsTerminal("Pending"));
        Assert.False(subject.IsTerminal("Bogus"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TryValidateStatusChange_reports_the_expected_errors(string kind)
    {
        var subject = Subject.For(kind);

        Assert.True(subject.TryValidateStatusChange("Pending", "Approved", out var error));
        Assert.Null(error);

        Assert.False(subject.TryValidateStatusChange("Bogus", "Approved", out error));
        Assert.Equal("Unknown current status 'Bogus'.", error);

        Assert.False(subject.TryValidateStatusChange("Pending", "Bogus", out error));
        Assert.Equal("Unknown target status 'Bogus'.", error);

        Assert.False(subject.TryValidateStatusChange("pending", "PENDING", out error));
        Assert.Equal("This submission is already Pending.", error);

        Assert.False(subject.TryValidateStatusChange("Approved", "Rejected", out error));
        Assert.Equal($"Cannot transition {subject.Noun} status from Approved to Rejected.", error);
    }

    [Theory]
    [InlineData("photo")]
    [InlineData("fan-performance")]
    public void Review_workflows_allow_the_intermediate_review_states(string kind)
    {
        var subject = Subject.For(kind);

        Assert.True(subject.CanTransition("Pending", "UnderReview"));
        Assert.True(subject.CanTransition("Pending", "NeedsInfo"));
        Assert.True(subject.CanTransition("UnderReview", "NeedsInfo"));
        Assert.True(subject.CanTransition("NeedsInfo", "UnderReview"));
        Assert.True(subject.CanTransition("NeedsInfo", "Approved"));
        Assert.False(subject.CanTransition("UnderReview", "Pending"));
        Assert.False(subject.CanTransition("NeedsInfo", "Pending"));
    }

    [Fact]
    public void Simple_workflows_do_not_have_review_states()
    {
        Assert.False(QuizQuestionSubmissionStatus.IsKnown("UnderReview"));
        Assert.False(TriviaFactSubmissionStatus.IsKnown("NeedsInfo"));
        Assert.True(QuizQuestionSubmissionStatus.IsPendingReview(" pending "));
        Assert.False(QuizQuestionSubmissionStatus.IsPendingReview("Approved"));
        Assert.True(TriviaFactSubmissionStatus.IsPendingReview("Pending"));
        Assert.False(TriviaFactSubmissionStatus.IsPendingReview("Rejected"));
    }

    [Fact]
    public void Fan_performance_members_can_withdraw_until_a_terminal_state()
    {
        Assert.True(FanPerformanceSubmissionWorkflow.CanTransition("Pending", "Withdrawn"));
        Assert.True(FanPerformanceSubmissionWorkflow.CanTransition("NeedsInfo", "Withdrawn"));
        Assert.False(FanPerformanceSubmissionWorkflow.CanTransition("Withdrawn", "Pending"));
        Assert.True(FanPerformanceSubmissionWorkflow.IsTerminal("Withdrawn"));
        Assert.True(FanPerformanceSubmissionWorkflow.CanMemberWithdraw("Pending"));
        Assert.False(FanPerformanceSubmissionWorkflow.CanMemberWithdraw("Approved"));
        Assert.False(PhotoSubmissionStatus.IsKnown("Withdrawn"));
    }

    private sealed record Subject(
        string Noun,
        IReadOnlyList<string> All,
        IReadOnlyList<string> AllStatuses,
        Func<string?, bool> IsKnown,
        Func<string, string> Normalize,
        Func<string, string, bool> CanTransition,
        TryValidate TryValidateStatusChange,
        Func<string, bool> IsTerminal)
    {
        public static Subject For(string kind) => kind switch
        {
            "photo" => new(
                "photo submission",
                PhotoSubmissionStatus.All,
                ["Pending", "UnderReview", "NeedsInfo", "Approved", "Rejected"],
                PhotoSubmissionStatus.IsKnown,
                PhotoSubmissionStatus.Normalize,
                PhotoSubmissionWorkflow.CanTransition,
                PhotoSubmissionWorkflow.TryValidateStatusChange,
                PhotoSubmissionWorkflow.IsTerminal),
            "fan-performance" => new(
                "fan-performance submission",
                FanPerformanceSubmissionStatus.All,
                ["Pending", "UnderReview", "NeedsInfo", "Approved", "Rejected", "Withdrawn"],
                FanPerformanceSubmissionStatus.IsKnown,
                FanPerformanceSubmissionStatus.Normalize,
                FanPerformanceSubmissionWorkflow.CanTransition,
                FanPerformanceSubmissionWorkflow.TryValidateStatusChange,
                FanPerformanceSubmissionWorkflow.IsTerminal),
            "quiz-question" => new(
                "quiz question submission",
                QuizQuestionSubmissionStatus.All,
                ["Pending", "Approved", "Rejected"],
                QuizQuestionSubmissionStatus.IsKnown,
                QuizQuestionSubmissionStatus.Normalize,
                QuizQuestionSubmissionWorkflow.CanTransition,
                QuizQuestionSubmissionWorkflow.TryValidateStatusChange,
                QuizQuestionSubmissionWorkflow.IsTerminal),
            "trivia-fact" => new(
                "trivia fact submission",
                TriviaFactSubmissionStatus.All,
                ["Pending", "Approved", "Rejected"],
                TriviaFactSubmissionStatus.IsKnown,
                TriviaFactSubmissionStatus.Normalize,
                TriviaFactSubmissionWorkflow.CanTransition,
                TriviaFactSubmissionWorkflow.TryValidateStatusChange,
                TriviaFactSubmissionWorkflow.IsTerminal),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private delegate bool TryValidate(string current, string next, out string? error);
}

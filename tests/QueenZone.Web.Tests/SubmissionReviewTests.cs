using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class SubmissionReviewTests
{
    [Fact]
    public void EnsureTransition_accepts_a_legal_change_and_throws_the_workflow_message()
    {
        SubmissionReview.EnsureTransition(
            TriviaFactSubmissionStatus.Pending,
            TriviaFactSubmissionStatus.Approved,
            TriviaFactSubmissionWorkflow.TryValidateStatusChange);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubmissionReview.EnsureTransition(
                TriviaFactSubmissionStatus.Approved,
                TriviaFactSubmissionStatus.Rejected,
                TriviaFactSubmissionWorkflow.TryValidateStatusChange));
        Assert.Contains("Cannot transition", ex.Message);
    }

    [Fact]
    public void RequireRejectionReason_trims_truncates_and_rejects_blank_input()
    {
        Assert.Equal("Blurry", SubmissionReview.RequireRejectionReason("  Blurry  "));
        Assert.Equal(new string('a', 500), SubmissionReview.RequireRejectionReason(new string('a', 600)));

        var ex = Assert.Throws<InvalidOperationException>(() => SubmissionReview.RequireRejectionReason("  "));
        Assert.Equal("A rejection reason is required.", ex.Message);
    }

    [Fact]
    public void Stamp_and_audit_values_are_shared_across_submission_types()
    {
        var trivia = new TriviaFactSubmissionEntity();
        var reviewedAt = SubmissionReview.Stamp(
            trivia,
            TriviaFactSubmissionStatus.Approved,
            "  editor@example.test  ",
            "  notes  ");
        Assert.Equal(TriviaFactSubmissionStatus.Approved, trivia.Status);
        Assert.Equal(reviewedAt, trivia.ReviewedAt);
        Assert.Equal("editor@example.test", trivia.ReviewerEmail);
        Assert.Equal("notes", trivia.ReviewNotes);

        var quizEntity = new QuizQuestionSubmissionEntity();
        SubmissionReview.Stamp(quizEntity, QuizQuestionSubmissionStatus.Rejected, "  ", null);
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, quizEntity.Status);
        Assert.NotNull(quizEntity.ReviewedAt);
        Assert.Null(quizEntity.ReviewerEmail);
        Assert.Null(quizEntity.ReviewNotes);

        var photoEntity = new PhotoSubmissionEntity();
        SubmissionReview.Stamp(photoEntity, PhotoSubmissionStatus.NeedsInfo, "admin@test.local", "year");
        Assert.Equal(PhotoSubmissionStatus.NeedsInfo, photoEntity.Status);
        Assert.Equal("admin@test.local", photoEntity.ReviewerEmail);
        Assert.Equal("year", photoEntity.ReviewNotes);

        var fanEntity = new FanPerformanceSubmissionEntity();
        SubmissionReview.Stamp(fanEntity, FanPerformanceSubmissionStatus.Approved, null, "   ");
        Assert.Equal(FanPerformanceSubmissionStatus.Approved, fanEntity.Status);
        Assert.Null(fanEntity.ReviewerEmail);
        Assert.Null(fanEntity.ReviewNotes);

        var submittedAt = DateTimeOffset.UtcNow;
        var submitted = SubmissionReview.Copy(
            new TriviaFactSubmissionAuditLogEntity(),
            SubmissionReview.Submitted(submittedAt, "Member submitted a trivia fact for review."));
        Assert.Equal("Submitted", submitted.Action);
        Assert.Equal(string.Empty, submitted.ActorEmail);
        Assert.Equal(submittedAt, submitted.OccurredAt);
        Assert.Equal("Member submitted a trivia fact for review.", submitted.Details);

        var quiz = SubmissionReview.Copy(
            new QuizQuestionSubmissionAuditLogEntity(),
            SubmissionReview.ForStatus(QuizQuestionSubmissionStatus.Rejected, null, reviewedAt, "Rejected."));
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, quiz.Action);
        Assert.Equal(string.Empty, quiz.ActorEmail);
        Assert.Equal(reviewedAt, quiz.OccurredAt);
        Assert.Equal("Rejected.", quiz.Details);

        var photo = SubmissionReview.Copy(
            new PhotoSubmissionAuditLogEntity(),
            SubmissionReview.ForStatus(PhotoSubmissionStatus.Approved, "admin@test.local", reviewedAt, "Approved."));
        Assert.Equal("admin@test.local", photo.ActorEmail);
        Assert.Equal("Approved.", photo.Details);

        var fan = SubmissionReview.Copy(
            new FanPerformanceSubmissionAuditLogEntity(),
            SubmissionReview.Submitted(submittedAt, "Member submitted a fan performance for review."));
        Assert.Equal("Submitted", fan.Action);
        Assert.Equal(submittedAt, fan.OccurredAt);
    }

    [Fact]
    public void Fan_status_change_keeps_previous_review_fields_unless_the_caller_supplies_them()
    {
        var entity = new FanPerformanceSubmissionEntity
        {
            Status = FanPerformanceSubmissionStatus.NeedsInfo,
            ReviewerEmail = "admin@test.local",
            ReviewNotes = "Need a cleaner take",
        };

        InMemoryFanPerformanceSubmissionRepository.ApplyStatusChange(
            entity,
            FanPerformanceSubmissionStatus.UnderReview,
            actorEmail: "  ",
            reviewNotes: null,
            rejectionReason: "optional context");

        Assert.Equal(FanPerformanceSubmissionStatus.UnderReview, entity.Status);
        Assert.Equal("admin@test.local", entity.ReviewerEmail);
        Assert.Equal("Need a cleaner take", entity.ReviewNotes);
        Assert.Equal("optional context", entity.RejectionReason);
        Assert.NotNull(entity.ReviewedAt);

        var pending = new FanPerformanceSubmissionEntity { Status = FanPerformanceSubmissionStatus.Pending };
        var rejected = Assert.Throws<InvalidOperationException>(() =>
            InMemoryFanPerformanceSubmissionRepository.ApplyStatusChange(
                pending,
                FanPerformanceSubmissionStatus.Rejected,
                "admin@test.local",
                null,
                "  "));
        Assert.Equal("A rejection reason is required.", rejected.Message);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, pending.Status);
        Assert.Null(pending.ReviewedAt);

        var needsInfo = Assert.Throws<InvalidOperationException>(() =>
            InMemoryFanPerformanceSubmissionRepository.ApplyStatusChange(
                pending,
                FanPerformanceSubmissionStatus.NeedsInfo,
                "admin@test.local",
                "   ",
                null,
                requireNeedsInfoNotes: true));
        Assert.Contains("Review notes are required", needsInfo.Message);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, pending.Status);
    }
}

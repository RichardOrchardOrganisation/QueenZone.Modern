using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class NewsCandidateReviewPresentationTests
{
    [Theory]
    [InlineData(NewsCandidateStatus.Discovered, false, true, true, true)]
    [InlineData(NewsCandidateStatus.NeedsReview, false, true, true, true)]
    [InlineData(NewsCandidateStatus.Drafted, false, false, true, true)]
    [InlineData(NewsCandidateStatus.Rejected, false, true, false, true)]
    [InlineData(NewsCandidateStatus.IgnoredDuplicate, false, false, true, false)]
    [InlineData(NewsCandidateStatus.PromotedToArticle, false, false, true, true)]
    [InlineData(NewsCandidateStatus.Discovered, true, true, true, true)]
    [InlineData(NewsCandidateStatus.NeedsReview, true, true, true, true)]
    [InlineData(NewsCandidateStatus.Drafted, true, true, true, true)]
    [InlineData(NewsCandidateStatus.Rejected, true, true, false, true)]
    [InlineData(NewsCandidateStatus.IgnoredDuplicate, true, false, true, false)]
    [InlineData(NewsCandidateStatus.PromotedToArticle, true, false, true, true)]
    [InlineData((NewsCandidateStatus)99, false, false, true, true)]
    [InlineData((NewsCandidateStatus)99, true, false, true, true)]
    public void Actions_PreserveStatusAndDraftPolicy(NewsCandidateStatus status, bool hasDraft, bool generate, bool reject, bool ignore)
    {
        var actions = NewsCandidateReviewPresentation.Actions(status, hasDraft);
        Assert.Equal(generate, actions.CanGenerateDraft);
        Assert.Equal(reject, actions.CanReject);
        Assert.Equal(ignore, actions.CanIgnoreDuplicate);
        Assert.Equal(hasDraft, actions.CanPromote);
        Assert.Equal(hasDraft, actions.ConfirmGeneration);
        Assert.Equal(hasDraft ? "Regenerate draft with AI" : "Generate draft with AI", actions.GenerationLabel);
        Assert.Equal(hasDraft ? "Regenerating draft" : "Generating draft", actions.GenerationBusyLabel);
    }

    [Fact]
    public void PublishedDate_PreservesUnknownAndDateWording()
    {
        Assert.Equal("Unknown", NewsCandidateReviewPresentation.PublishedDate(null));
        Assert.Equal("02 Oct 2026", NewsCandidateReviewPresentation.PublishedDate(new DateTime(2026, 10, 2)));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0.0, "0.00")]
    [InlineData(0.926, "0.93")]
    [InlineData(1.0, "1.00")]
    public void Score_PreservesMissingAndTwoDecimalFormatting(double? score, string expected) =>
        Assert.Equal(expected, NewsCandidateReviewPresentation.Score(score is null ? null : (decimal)score.Value));

    [Theory]
    [InlineData(null, "compiled default")]
    [InlineData(0, "0")]
    [InlineData(42, "42")]
    public void GuidanceRevision_PreservesFallbackAndRevision(int? revision, string expected) =>
        Assert.Equal(expected, NewsCandidateReviewPresentation.GuidanceRevision(revision));

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "")]
    [InlineData("revision-hash", "revision-hash")]
    public void GuidanceHash_PreservesNullFallbackAndExistingValue(string? hash, string expected) =>
        Assert.Equal(expected, NewsCandidateReviewPresentation.GuidanceHash(hash));
}

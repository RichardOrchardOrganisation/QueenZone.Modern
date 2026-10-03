using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MySubmissionPresentationTests
{
    [Theory]
    [InlineData("photos", "_MySubmissionPhotos")]
    [InlineData("performances", "_MySubmissionPerformances")]
    [InlineData("trivia", "_MySubmissionTrivia")]
    [InlineData("quiz", "_MySubmissionQuiz")]
    [InlineData("news", "_MySubmissionNews")]
    [InlineData("articles", "_MySubmissionArticles")]
    [InlineData("unknown", "_MySubmissionArticles")]
    public void Tabs_PreserveLabelsLinksAndExactSelection(string tab, string partial)
    {
        Assert.Equal(partial, MySubmissionPresentation.PartialForTab(tab));
        var tabs = MySubmissionPresentation.Tabs(tab);
        Assert.Equal(new[] { "Photos", "News suggestions", "Articles", "Trivia", "Quiz", "Fan performances" }, tabs.Select(item => item.Label));
        Assert.Equal(new[] { "photos", "news", "articles", "trivia", "quiz", "performances" },
            tabs.Select(item => item.Href.Split('=')[1]));
        Assert.All(tabs, item => Assert.StartsWith("/account/my-submissions?tab=", item.Href));
        Assert.Equal(tab == "unknown" ? 0 : 1, tabs.Count(item => item.IsActive));
        Assert.All(tabs.Where(item => item.IsActive), item => Assert.EndsWith("=" + tab, item.Href));
    }

    [Theory]
    [InlineData("Rejected", "Reason", "Reason")]
    [InlineData("Rejected", "  Reason  ", "  Reason  ")]
    [InlineData("Rejected", "   ", null)]
    [InlineData("Rejected", null, null)]
    [InlineData("Pending", "Private rejection reason", null)]
    public void RejectionNotes_ShowOnlyNonBlankRejectedNotes(string status, string? reason, string? expected) =>
        Assert.Equal(expected, MySubmissionPresentation.RejectionNotes(status, reason));

    [Theory]
    [InlineData("Rejected", "Rejection", "Review", "Rejection")]
    [InlineData("Rejected", " ", "Review", "Review")]
    [InlineData("Pending", "Rejection", "Review", "Review")]
    [InlineData("Pending", null, " ", null)]
    [InlineData("Rejected", null, null, null)]
    public void PhotoNotes_PreserveRejectionPriorityAndReviewFallback(string status, string? reason, string? review, string? expected) =>
        Assert.Equal(expected, MySubmissionPresentation.PhotoNotes(status, reason, review));

    [Theory]
    [InlineData(ArticleSubmissionStatus.Draft, "Continue editing")]
    [InlineData(ArticleSubmissionStatus.RequiresRevision, "Revise and resubmit")]
    [InlineData(ArticleSubmissionStatus.Published, "View published article")]
    [InlineData(ArticleSubmissionStatus.Submitted, null)]
    [InlineData("Unknown", null)]
    public void ArticleActions_PreserveEditingRevisionAndPublishedLinks(string status, string? label)
    {
        var item = Article(status, null, null);
        var presentation = MySubmissionPresentation.Article(item);
        Assert.Equal(label, presentation.Action?.Label);
        if (label is not null)
        {
            Assert.Equal(status == ArticleSubmissionStatus.Published ? "/articles/presentation-article" : $"/submit/article/{item.Id:D}", presentation.Action!.Href);
        }
    }

    [Theory]
    [InlineData(ArticleSubmissionStatus.RequiresRevision, "Review", "Rejection", "Review", true)]
    [InlineData(ArticleSubmissionStatus.RequiresRevision, " ", "Rejection", "Rejection", false)]
    [InlineData(ArticleSubmissionStatus.Rejected, "Review", "Rejection", "Rejection", false)]
    [InlineData(ArticleSubmissionStatus.Submitted, "Review", " ", "Review", false)]
    [InlineData(ArticleSubmissionStatus.Submitted, "  Review  ", null, "  Review  ", false)]
    [InlineData(ArticleSubmissionStatus.Submitted, " ", null, null, false)]
    [InlineData(ArticleSubmissionStatus.RequiresRevision, null, null, null, false)]
    public void ArticleNotes_PreserveRevisionPresentationAndFallbackPriority(string status, string? review, string? reason, string? notes, bool revision)
    {
        var presentation = MySubmissionPresentation.Article(Article(status, review, reason));
        Assert.Equal(notes, presentation.Notes);
        Assert.Equal(revision, presentation.IsRevisionNote);
    }

    [Fact]
    public void ArticleSubmittedTime_PreservesDraftAndUniversalFormat()
    {
        var item = Article(ArticleSubmissionStatus.Draft, null, null);
        Assert.Equal("Draft", MySubmissionPresentation.Article(item).SubmittedLabel);
        var submitted = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(submitted.ToString("u"), MySubmissionPresentation.Article(item with { SubmittedAt = submitted }).SubmittedLabel);
    }

    private static ArticleSubmission Article(string status, string? review, string? reason) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Presentation article", "presentation-article", null, "Body", null, null,
            status, null, null, null, review, reason, "Member", "member@example.com");
}

using QueenZone.Data;
using QueenZone.Routing;

namespace QueenZone.Web;

public sealed record MySubmissionTab(string Label, string Href, bool IsActive);
public sealed record MySubmissionAction(string Label, string Href);
public sealed record MyArticleSubmissionPresentation(string SubmittedLabel, string? Notes, bool IsRevisionNote, MySubmissionAction? Action);

public static class MySubmissionPresentation
{
    private static readonly (string Id, string Label)[] TabLabels =
    [
        ("photos", "Photos"), ("news", "News suggestions"), ("articles", "Articles"),
        ("trivia", "Trivia"), ("quiz", "Quiz"), ("performances", "Fan performances"),
    ];

    public static IReadOnlyList<MySubmissionTab> Tabs(string activeTab) => TabLabels.Select(tab =>
        new MySubmissionTab(tab.Label, $"/account/my-submissions?tab={tab.Id}", tab.Id == activeTab)).ToArray();

    public static string PartialForTab(string tab) => tab switch
    {
        "photos" => "_MySubmissionPhotos",
        "performances" => "_MySubmissionPerformances",
        "trivia" => "_MySubmissionTrivia",
        "quiz" => "_MySubmissionQuiz",
        "news" => "_MySubmissionNews",
        _ => "_MySubmissionArticles",
    };

    public static string? RejectionNotes(string status, string? rejectionReason) =>
        status == PhotoSubmissionStatus.Rejected ? NonBlank(rejectionReason) : null;

    public static string? PhotoNotes(string status, string? rejectionReason, string? reviewNotes) =>
        RejectionNotes(status, rejectionReason) ?? NonBlank(reviewNotes);

    public static MyArticleSubmissionPresentation Article(ArticleSubmission item)
    {
        var isRevisionNote = item.Status == ArticleSubmissionStatus.RequiresRevision && !string.IsNullOrWhiteSpace(item.ReviewNotes);
        var notes = isRevisionNote ? item.ReviewNotes : NonBlank(item.RejectionReason) ?? NonBlank(item.ReviewNotes);
        return new MyArticleSubmissionPresentation(item.SubmittedAt?.ToString("u") ?? "Draft", notes, isRevisionNote, ArticleAction(item));
    }

    private static MySubmissionAction? ArticleAction(ArticleSubmission item) => item.Status switch
    {
        ArticleSubmissionStatus.Draft => new("Continue editing", $"/submit/article/{item.Id:D}"),
        ArticleSubmissionStatus.RequiresRevision => new("Revise and resubmit", $"/submit/article/{item.Id:D}"),
        ArticleSubmissionStatus.Published => new("View published article", ArticlesRoutes.GetCommunityArticleDetailPath(item.Slug)),
        _ => null,
    };

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

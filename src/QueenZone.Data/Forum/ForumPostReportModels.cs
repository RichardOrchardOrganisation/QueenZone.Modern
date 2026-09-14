using System.Text.Json;

namespace QueenZone.Data;

public static class ForumPostReportLimits
{
    public const int MaxDetailsLength = 1000;
    public const int ContextPostCount = 4;
}

public static class ForumPostReportCategories
{
    public const string Harassment = "Harassment or bullying";
    public const string HateSpeech = "Hate speech or discrimination";
    public const string Threats = "Threats or violence";
    public const string SexualContent = "Sexual or explicit content";
    public const string Spam = "Spam or scams";
    public const string Copyright = "Copyright or ownership concern";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
    [Harassment, HateSpeech, Threats, SexualContent, Spam, Copyright, Other];

    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

public static class ForumPostReportText
{
    public const string PostNotFound = "Forum post was not found.";
    public const string CannotReportOwn = "You cannot report your own post.";
    public const string CategoryRequired = "Choose a valid report reason.";
    public static string DetailsTooLong =>
        $"Supporting details must be {ForumPostReportLimits.MaxDetailsLength} characters or fewer.";
}

public sealed record ForumReportablePost(
    int PostId,
    int TopicId,
    string ThreadTitle,
    string Body,
    Guid? AuthorMemberId,
    string AuthorDisplayName,
    DateTimeOffset PostedAt);

public sealed record ForumPostReportContextItem(
    int PostId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset PostedAt);

public sealed record ForumPostReport(
    Guid Id,
    int PostId,
    int TopicId,
    Guid ReporterMemberId,
    Guid? ReportedMemberId,
    string Category,
    string? Details,
    DateTimeOffset CreatedAt,
    string Status,
    string PostBodySnapshot,
    string AuthorDisplayNameSnapshot,
    DateTimeOffset PostCreatedAtSnapshot,
    string ThreadTitleSnapshot,
    IReadOnlyList<ForumPostReportContextItem> Context,
    int PreviousAuthorReportCount = 0);

public sealed record ForumPostReportResult(
    bool Succeeded,
    Guid? ReportId,
    string? ErrorMessage,
    bool AlreadyReported = false);

public sealed record ForumPostReportListItem(
    Guid Id,
    int PostId,
    int TopicId,
    Guid ReporterMemberId,
    string ReporterDisplayName,
    Guid? ReportedMemberId,
    string ReportedDisplayName,
    string Category,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record ForumPostReportListPage(
    IReadOnlyList<ForumPostReportListItem> Items,
    int TotalCount,
    string? StatusFilter);

internal static class ForumPostReportContextSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? Serialize(IReadOnlyList<ForumPostReportContextItem> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<ForumPostReportContextItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ForumPostReportContextItem>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

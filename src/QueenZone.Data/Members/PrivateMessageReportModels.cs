using System.Text.Json;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public static class PrivateMessageReportText
{
    public const string NotAParticipant = "You are not a participant in this conversation.";

    public const string MessageNotFound = "Message was not found.";

    public const string CannotReportOwn = "You cannot report your own message.";

    public static string ReasonTooLong =>
        $"Report reason must be {PrivateMessageLimits.MaxReportReasonLength} characters or fewer.";
}

public sealed record PrivateMessageReportContextItem(
    Guid MessageId,
    Guid SenderMemberId,
    string SenderDisplayName,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record PrivateMessageReport(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    Guid ReporterMemberId,
    Guid ReportedMemberId,
    string? Reason,
    DateTimeOffset CreatedAt,
    string Status,
    string MessageBodySnapshot,
    string SenderDisplayNameSnapshot,
    DateTimeOffset MessageCreatedAtSnapshot,
    long MessageSortKeySnapshot,
    IReadOnlyList<PrivateMessageReportContextItem> PrecedingMessages);

/// <summary>Typed outcome of a private message report so callers never branch on message text (#2436).</summary>
public enum PrivateMessageReportOutcome
{
    Created,
    AlreadyReported,
    MessageNotFound,
    NotAParticipant,
    CannotReportOwn,
    Invalid,
}

public sealed record PrivateMessageReportResult(
    PrivateMessageReportOutcome Outcome,
    Guid? ReportId,
    string? ErrorMessage)
{
    public bool Succeeded => Outcome is PrivateMessageReportOutcome.Created or PrivateMessageReportOutcome.AlreadyReported;

    public bool AlreadyReported => Outcome == PrivateMessageReportOutcome.AlreadyReported;

    public static PrivateMessageReportResult Created(Guid reportId) =>
        new(PrivateMessageReportOutcome.Created, reportId, null);

    public static PrivateMessageReportResult Duplicate(Guid reportId) =>
        new(PrivateMessageReportOutcome.AlreadyReported, reportId, null);

    public static PrivateMessageReportResult Failure(PrivateMessageReportOutcome outcome, string errorMessage) =>
        new(outcome, null, errorMessage);
}

/// <summary>Row shown on the admin moderation queue (issue #470).</summary>
public sealed record PrivateMessageReportListItem(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    Guid ReporterMemberId,
    string ReporterDisplayName,
    Guid ReportedMemberId,
    string ReportedDisplayName,
    string? Reason,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record PrivateMessageReportListPage(
    IReadOnlyList<PrivateMessageReportListItem> Items,
    int TotalCount,
    string? StatusFilter);

internal static class PrivateMessageReportContextSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? Serialize(IReadOnlyList<PrivateMessageReportContextItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    public static IReadOnlyList<PrivateMessageReportContextItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PrivateMessageReportContextItem>>(json, JsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

internal static class PrivateMessageReportMapping
{
    public static PrivateMessageReport ToModel(PrivateMessageReportEntity entity) =>
        new(
            entity.Id,
            entity.MessageId,
            entity.ConversationId,
            entity.ReporterMemberId,
            entity.ReportedMemberId,
            entity.Reason,
            entity.CreatedAt,
            entity.Status,
            entity.MessageBodySnapshot,
            entity.SenderDisplayNameSnapshot,
            entity.MessageCreatedAtSnapshot,
            entity.MessageSortKeySnapshot,
            PrivateMessageReportContextSerializer.Deserialize(entity.PrecedingContextJson));

    public static PrivateMessageReportEntity CreateEntity(
        Guid reporterMemberId,
        PrivateMessageEntity message,
        string senderDisplayName,
        IReadOnlyList<PrivateMessageReportContextItem> preceding,
        string? reason,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            MessageId = message.Id,
            ConversationId = message.ConversationId,
            ReporterMemberId = reporterMemberId,
            ReportedMemberId = message.SenderMemberId,
            Reason = reason,
            CreatedAt = createdAt,
            Status = PrivateMessageReportStatus.Open,
            MessageBodySnapshot = message.Body,
            SenderDisplayNameSnapshot = senderDisplayName,
            MessageCreatedAtSnapshot = message.CreatedAt,
            MessageSortKeySnapshot = message.SortKey,
            PrecedingContextJson = PrivateMessageReportContextSerializer.Serialize(preceding),
        };
}

using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Row construction, review updates, and mapping shared by the EF and in-memory help request
/// repositories, so both stores normalise input the same way.
/// </summary>
internal static class HelpRequestRecords
{
    internal static HelpRequestEntity NewEntity(HelpRequest request) =>
        new()
        {
            Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            Topic = HelpRequestTopic.Normalize(request.Topic),
            Subject = RequireTrimmed(request.Subject, 200),
            Message = RequireTrimmed(request.Message, 4000),
            Name = RequireTrimmed(request.Name, 100),
            Email = RequireTrimmed(request.Email, 256),
            NormalizedEmail = NormalizeEmail(request.NormalizedEmail, request.Email),
            MemberId = request.MemberId,
            Status = HelpRequestStatus.Open,
            SubmittedAt = request.SubmittedAt == default ? DateTimeOffset.UtcNow : request.SubmittedAt,
        };

    internal static void ApplyStatus(HelpRequestEntity entity, string status, string? reviewerEmail, string? notes)
    {
        entity.Status = HelpRequestStatus.Normalize(status);
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(notes, 500);
    }

    internal static string NormalizeEmail(string? normalizedEmail, string email)
    {
        var source = string.IsNullOrWhiteSpace(normalizedEmail) ? email : normalizedEmail;
        return source.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// The status to filter a list by; blank or <c>all</c> means no filter.
    /// </summary>
    internal static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return HelpRequestStatus.Normalize(status);
    }

    internal static HelpRequest Map(HelpRequestEntity entity) =>
        new(
            entity.Id,
            entity.Topic,
            entity.Subject,
            entity.Message,
            entity.Name,
            entity.Email,
            entity.NormalizedEmail,
            entity.MemberId,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes);

    private static string RequireTrimmed(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

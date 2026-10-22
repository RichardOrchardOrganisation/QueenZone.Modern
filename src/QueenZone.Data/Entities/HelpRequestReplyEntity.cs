namespace QueenZone.Data.Entities;

public sealed class HelpRequestReplyEntity
{
    public Guid Id { get; set; }

    public Guid HelpRequestId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string SentByEmail { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public HelpRequestEntity? HelpRequest { get; set; }
}

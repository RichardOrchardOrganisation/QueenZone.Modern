namespace QueenZone.Application;

/// <summary>Application-wide outbound mail contract for website and mobile API workflows.</summary>
public interface IEmailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default);
}

public sealed record OutboundEmail(
    string ToAddress,
    string Subject,
    string TextBody,
    string? ReplyToAddress = null,
    string? HtmlBody = null);

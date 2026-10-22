using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>Emails an admin's answer to a help request and keeps a log of what was sent.</summary>
public sealed class HelpRequestReplyService(
    IHelpRequestRepository helpRequestRepository,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    IOptions<HelpRequestOptions> options,
    ILogger<HelpRequestReplyService> logger)
{
    public const int MinBodyLength = 2;

    public enum ReplyOutcome
    {
        Sent,
        NotFound,
        Invalid,
        SendFailed,
    }

    public sealed record ReplyResult(ReplyOutcome Outcome, string? Error = null, HelpRequest? Request = null);

    public static string DefaultSubject(string requestSubject)
    {
        var trimmed = requestSubject.Trim();
        return trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Re: {trimmed}";
    }

    public async Task<ReplyResult> ReplyAsync(
        Guid requestId,
        string? subject,
        string? body,
        string senderEmail,
        bool markResolved,
        CancellationToken cancellationToken = default)
    {
        var trimmedBody = body?.Trim() ?? string.Empty;
        var trimmedSubject = subject?.Trim() ?? string.Empty;
        var contentError = Validate(trimmedSubject, trimmedBody);
        if (contentError is not null)
        {
            return new ReplyResult(ReplyOutcome.Invalid, contentError);
        }

        var request = await helpRequestRepository.GetByIdAsync(requestId, cancellationToken);
        if (request is null)
        {
            return new ReplyResult(ReplyOutcome.NotFound);
        }

        var email = new OutboundEmail(
            request.Email,
            trimmedSubject,
            ComposeBody(request, trimmedBody),
            options.Value.NotificationAddress);

        try
        {
            await emailSender.SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Could not email reply to help request {RequestId}: {ErrorType}.", requestId, ex.GetType().Name);
            return new ReplyResult(
                ReplyOutcome.SendFailed,
                "The email could not be sent, so nothing was logged. Check the SMTP settings and try again.");
        }

        await helpRequestRepository.AddReplyAsync(
            new HelpRequestReply(
                Guid.NewGuid(),
                requestId,
                request.Email,
                trimmedSubject,
                trimmedBody,
                senderEmail,
                timeProvider.GetUtcNow()),
            cancellationToken);

        if (markResolved && request.Status != HelpRequestStatus.Resolved)
        {
            await helpRequestRepository.UpdateStatusAsync(
                requestId,
                HelpRequestStatus.Resolved,
                senderEmail,
                request.ReviewNotes,
                cancellationToken);
        }

        return new ReplyResult(ReplyOutcome.Sent, Request: request);
    }

    private static string? Validate(string subject, string body)
    {
        if (subject.Length == 0)
        {
            return "A subject is required.";
        }

        if (subject.Length > HelpRequestReply.MaxSubjectLength)
        {
            return $"Subject must be {HelpRequestReply.MaxSubjectLength} characters or fewer.";
        }

        if (body.Length < MinBodyLength)
        {
            return "Write a reply before sending.";
        }

        if (body.Length > HelpRequestReply.MaxBodyLength)
        {
            return $"Reply must be {HelpRequestReply.MaxBodyLength} characters or fewer.";
        }

        return null;
    }

    private static string ComposeBody(HelpRequest request, string body) =>
        $"{body}\n\n--\nQueenzone Support\n\n"
        + $"On {request.SubmittedAt:u} you wrote:\n"
        + string.Join('\n', request.Message.ReplaceLineEndings("\n").Split('\n').Select(line => $"> {line}"));
}

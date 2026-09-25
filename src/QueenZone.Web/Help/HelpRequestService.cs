using System.Net.Mail;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class HelpRequestService(
    IHelpRequestRepository helpRequestRepository,
    IMemberAccountRepository memberAccountRepository,
    HelpRequestFormStamp formStamp,
    HelpRequestRateLimiter rateLimiter,
    TimeProvider timeProvider,
    IOptions<HelpRequestOptions> options,
    IEmailSender? emailSender = null,
    ILogger<HelpRequestService>? logger = null)
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 256;
    public const int MinSubjectLength = 5;
    public const int MaxSubjectLength = 200;
    public const int MinMessageLength = 20;
    public const int MaxMessageLength = 4000;

    public sealed record SubmitResult(bool Succeeded, HelpRequest? Request, string? Error, bool SilentlyDropped);

    public string IssueFormStamp() => formStamp.Issue();

    public async Task<SubmitResult> SubmitAsync(
        Guid? memberId,
        string topic,
        string subject,
        string message,
        string? name,
        string? email,
        string? websiteHoneypot,
        string? issuedStamp,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(websiteHoneypot)
            || !formStamp.IsAcceptable(issuedStamp, options.Value.MinimumDwellSeconds))
        {
            return new SubmitResult(true, null, null, SilentlyDropped: true);
        }

        if (!rateLimiter.IsAllowed(memberId, clientIp))
        {
            return new SubmitResult(
                false,
                null,
                "Too many messages from this network. Please try again later.",
                false);
        }

        if (!HelpRequestTopic.IsKnown(topic))
        {
            return new SubmitResult(false, null, "Please choose a topic.", false);
        }

        var normalizedTopic = HelpRequestTopic.Normalize(topic);
        var trimmedSubject = subject?.Trim() ?? string.Empty;
        var trimmedMessage = message?.Trim() ?? string.Empty;

        if (trimmedSubject.Length < MinSubjectLength)
        {
            return new SubmitResult(false, null, $"Subject must be at least {MinSubjectLength} characters.", false);
        }

        if (trimmedSubject.Length > MaxSubjectLength)
        {
            return new SubmitResult(false, null, $"Subject must be {MaxSubjectLength} characters or fewer.", false);
        }

        if (trimmedMessage.Length < MinMessageLength)
        {
            return new SubmitResult(false, null, $"Message must be at least {MinMessageLength} characters.", false);
        }

        if (trimmedMessage.Length > MaxMessageLength)
        {
            return new SubmitResult(false, null, $"Message must be {MaxMessageLength} characters or fewer.", false);
        }

        string snapshotName;
        string snapshotEmail;
        Guid? storedMemberId = null;

        if (memberId is Guid signedInId)
        {
            var account = await memberAccountRepository.FindByIdAsync(signedInId, cancellationToken);
            if (account is null)
            {
                return new SubmitResult(false, null, "Sign in again and retry your message.", false);
            }

            snapshotName = account.DisplayName.Trim();
            snapshotEmail = account.Email.Trim();
            storedMemberId = account.Id;
        }
        else
        {
            snapshotName = name?.Trim() ?? string.Empty;
            snapshotEmail = email?.Trim() ?? string.Empty;

            if (snapshotName.Length < 2)
            {
                return new SubmitResult(false, null, "Name is required.", false);
            }

            if (snapshotName.Length > MaxNameLength)
            {
                return new SubmitResult(false, null, $"Name must be {MaxNameLength} characters or fewer.", false);
            }

            var emailError = ValidateEmail(snapshotEmail);
            if (emailError is not null)
            {
                return new SubmitResult(false, null, emailError, false);
            }
        }

        var normalizedEmail = snapshotEmail.Trim().ToUpperInvariant();
        var sinceUtc = timeProvider.GetUtcNow().AddDays(-1);

        if (storedMemberId is Guid memberAccountId)
        {
            var maxPerMember = Math.Max(1, options.Value.MaxPerMemberPerDay);
            var recentMemberCount = await helpRequestRepository.CountByMemberSinceAsync(
                memberAccountId,
                sinceUtc,
                cancellationToken);
            if (recentMemberCount >= maxPerMember)
            {
                return new SubmitResult(
                    false,
                    null,
                    $"You can send up to {maxPerMember} messages per day. Please try again tomorrow.",
                    false);
            }
        }
        else
        {
            var maxPerEmail = Math.Max(1, options.Value.MaxPerEmailPerDay);
            var recentEmailCount = await helpRequestRepository.CountByEmailSinceAsync(
                normalizedEmail,
                sinceUtc,
                cancellationToken);
            if (recentEmailCount >= maxPerEmail)
            {
                return new SubmitResult(
                    false,
                    null,
                    $"You can send up to {maxPerEmail} messages per day from this email address. Please try again tomorrow.",
                    false);
            }
        }

        var created = await helpRequestRepository.CreateAsync(
            new HelpRequest(
                Guid.NewGuid(),
                normalizedTopic,
                trimmedSubject,
                trimmedMessage,
                snapshotName,
                snapshotEmail,
                normalizedEmail,
                storedMemberId,
                HelpRequestStatus.Open,
                timeProvider.GetUtcNow(),
                null,
                null,
                null),
            cancellationToken);

        if (emailSender is not null)
        {
            try
            {
                await emailSender.SendAsync(
                    new OutboundEmail(
                        options.Value.NotificationAddress,
                        "New Queenzone contact request",
                        $"Topic: {HelpRequestTopic.DisplayName(normalizedTopic)}\nName: {snapshotName}\nEmail: {snapshotEmail}\nSubject: {trimmedSubject}\n\n{trimmedMessage}",
                        snapshotEmail),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogError("Could not email contact request {RequestId}: {ErrorType}.", created.Id, ex.GetType().Name);
            }
        }

        return new SubmitResult(true, created, null, false);
    }

    internal static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "Email address is required.";
        }

        var trimmed = email.Trim();
        if (trimmed.Length > MaxEmailLength)
        {
            return $"Email address must be {MaxEmailLength} characters or fewer.";
        }

        try
        {
            var parsed = new MailAddress(trimmed);
            if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                || !trimmed.Contains('@', StringComparison.Ordinal))
            {
                return "Enter a valid email address.";
            }
        }
        catch (FormatException)
        {
            return "Enter a valid email address.";
        }

        return null;
    }

    public static string? ResolveClientIp(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            return ip;
        }

        var environment = httpContext.RequestServices.GetService<IHostEnvironment>();
        return environment is not null && QueenZoneEnvironments.IsAutomatedTestHost(environment)
            ? "test"
            : null;
    }
}

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
    HelpRequestNotificationSender? notificationSender = null)
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
        HelpRequestSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var memberId = submission.MemberId;
        var topic = submission.Topic;
        var subject = submission.Subject;
        var message = submission.Message;
        var name = submission.Name;
        var email = submission.Email;
        var websiteHoneypot = submission.WebsiteHoneypot;
        var issuedStamp = submission.IssuedStamp;
        var clientIp = submission.ClientIp;

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

        var contentError = ValidateContent(trimmedSubject, trimmedMessage);
        if (contentError is not null)
        {
            return new SubmitResult(false, null, contentError, false);
        }

        var contact = await ResolveContactSnapshotAsync(memberId, name, email, cancellationToken);
        if (contact.Error is not null)
        {
            return new SubmitResult(false, null, contact.Error, false);
        }
        var snapshotName = contact.Name;
        var snapshotEmail = contact.Email;
        var storedMemberId = contact.MemberId;

        var normalizedEmail = snapshotEmail.Trim().ToUpperInvariant();
        var sinceUtc = timeProvider.GetUtcNow().AddDays(-1);

        var limitError = await CheckDailyLimitAsync(storedMemberId, normalizedEmail, sinceUtc, cancellationToken);
        if (limitError is not null)
        {
            return new SubmitResult(false, null, limitError, false);
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

        if (notificationSender is not null)
        {
            await notificationSender.SendAsync(
                created.Id,
                new OutboundEmail(
                    options.Value.NotificationAddress,
                    "New Queenzone contact request",
                    $"Topic: {HelpRequestTopic.DisplayName(normalizedTopic)}\nName: {snapshotName}\nEmail: {snapshotEmail}\nSubject: {trimmedSubject}\n\n{trimmedMessage}",
                    snapshotEmail),
                cancellationToken);
        }

        return new SubmitResult(true, created, null, false);
    }

    private sealed record ContactSnapshot(string Name, string Email, Guid? MemberId, string? Error);

    private async Task<ContactSnapshot> ResolveContactSnapshotAsync(
        Guid? memberId,
        string? name,
        string? email,
        CancellationToken cancellationToken)
    {
        string snapshotName;
        string snapshotEmail;
        Guid? storedMemberId = null;

        if (memberId is Guid signedInId)
        {
            var account = await memberAccountRepository.FindByIdAsync(signedInId, cancellationToken);
            if (account is null)
            {
                return new ContactSnapshot("", "", null, "Sign in again and retry your message.");
            }

            snapshotName = account.DisplayName.Trim();
            snapshotEmail = account.Email.Trim();
            storedMemberId = account.Id;
        }
        else
        {
            snapshotName = name?.Trim() ?? string.Empty;
            snapshotEmail = email?.Trim() ?? string.Empty;

            var contactError = ValidateGuestContact(snapshotName, snapshotEmail);
            if (contactError is not null)
            {
                return new ContactSnapshot("", "", null, contactError);
            }
        }

        return new ContactSnapshot(snapshotName, snapshotEmail, storedMemberId, null);
    }

    private static string? ValidateContent(string trimmedSubject, string trimmedMessage)
    {
        if (trimmedSubject.Length < MinSubjectLength)
        {
            return $"Subject must be at least {MinSubjectLength} characters.";
        }

        if (trimmedSubject.Length > MaxSubjectLength)
        {
            return $"Subject must be {MaxSubjectLength} characters or fewer.";
        }

        if (trimmedMessage.Length < MinMessageLength)
        {
            return $"Message must be at least {MinMessageLength} characters.";
        }

        if (trimmedMessage.Length > MaxMessageLength)
        {
            return $"Message must be {MaxMessageLength} characters or fewer.";
        }

        return null;
    }

    private static string? ValidateGuestContact(string name, string email)
    {
        if (name.Length < 2)
        {
            return "Name is required.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        var emailError = ValidateEmail(email);
        if (emailError is not null)
        {
            return emailError;
        }
        return null;
    }

    private async Task<string?> CheckDailyLimitAsync(
        Guid? memberId,
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        if (memberId is Guid memberAccountId)
        {
            var maxPerMember = Math.Max(1, options.Value.MaxPerMemberPerDay);
            var recentMemberCount = await helpRequestRepository.CountByMemberSinceAsync(
                memberAccountId,
                sinceUtc,
                cancellationToken);
            if (recentMemberCount >= maxPerMember)
            {
                return $"You can send up to {maxPerMember} messages per day. Please try again tomorrow.";
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
                return $"You can send up to {maxPerEmail} messages per day from this email address. Please try again tomorrow.";
            }
        }

        return null;
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

public sealed record HelpRequestSubmission(
    Guid? MemberId,
    string Topic,
    string Subject,
    string Message,
    string? Name,
    string? Email)
{
    public string? WebsiteHoneypot { get; init; }
    public string? IssuedStamp { get; init; }
    public string? ClientIp { get; init; }
}

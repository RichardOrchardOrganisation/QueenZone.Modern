using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

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

public sealed class SmtpEmailOptions
{
    public const string SectionName = "SmtpEmail";
    public string Username { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "support@queenzone.org";
}

public interface ISmtpTransport
{
    Task SendAsync(SmtpEmailOptions settings, MailMessage message, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options, ISmtpTransport transport) : IEmailSender
{
    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.AppPassword))
        {
            throw new InvalidOperationException("SMTP credentials are not configured.");
        }

        using var message = CreateMessage(settings, email);

        await transport.SendAsync(settings, message, cancellationToken);
    }

    internal static MailMessage CreateMessage(SmtpEmailOptions settings, OutboundEmail email)
    {
        var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, "Queenzone Support"),
            Subject = email.Subject,
            Body = email.TextBody,
        };
        message.To.Add(new MailAddress(email.ToAddress));
        if (!string.IsNullOrWhiteSpace(email.ReplyToAddress))
        {
            message.ReplyToList.Add(new MailAddress(email.ReplyToAddress));
        }
        if (!string.IsNullOrWhiteSpace(email.HtmlBody))
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.TextBody, null, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.HtmlBody, null, "text/html"));
        }

        return message;
    }
}

[ExcludeFromCodeCoverage(Justification = "Thin Gmail SMTP adapter; sender behavior is tested with an injectable transport.")]
public sealed class GmailSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(SmtpEmailOptions settings, MailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient("smtp.gmail.com", 587)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.Username, settings.AppPassword),
            Timeout = 15000,
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}

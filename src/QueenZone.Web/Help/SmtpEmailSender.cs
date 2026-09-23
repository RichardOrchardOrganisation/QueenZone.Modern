using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public interface IOutboundEmailSender
{
    Task SendAsync(string to, string subject, string body, string? replyTo = null, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailOptions
{
    public const string SectionName = "SmtpEmail";
    public string Username { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "support@queenzone.org";
    public string SupportAddress { get; set; } = "support@queenzone.org";
}

public interface ISmtpTransport
{
    Task SendAsync(SmtpEmailOptions settings, MailMessage message, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options, ISmtpTransport transport) : IOutboundEmailSender
{
    public async Task SendAsync(string to, string subject, string body, string? replyTo = null, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.AppPassword))
        {
            throw new InvalidOperationException("SMTP credentials are not configured.");
        }

        using var message = CreateMessage(settings, to, subject, body, replyTo);

        await transport.SendAsync(settings, message, cancellationToken);
    }

    internal static MailMessage CreateMessage(SmtpEmailOptions settings, string to, string subject, string body, string? replyTo)
    {
        var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, "Queenzone Support"),
            Subject = subject,
            Body = body,
        };
        message.To.Add(new MailAddress(to));
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message.ReplyToList.Add(new MailAddress(replyTo));
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

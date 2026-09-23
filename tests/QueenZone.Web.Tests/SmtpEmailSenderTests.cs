using Microsoft.Extensions.Options;
using QueenZone.Web;
using System.Net.Mail;

namespace QueenZone.Web.Tests;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public void CreateMessage_UsesSupportAliasAndVisitorReplyTo()
    {
        var options = new SmtpEmailOptions
        {
            Username = "owner@gmail.com",
            AppPassword = "example-password",
        };

        using var message = SmtpEmailSender.CreateMessage(
            options, "support@queenzone.org", "Contact request", "Message text", "fan@example.com");

        Assert.Equal("support@queenzone.org", message.From!.Address);
        Assert.Equal("support@queenzone.org", Assert.Single(message.To).Address);
        Assert.Equal("fan@example.com", Assert.Single(message.ReplyToList).Address);
        Assert.Equal("Message text", message.Body);
    }

    [Fact]
    public async Task SendAsync_RejectsMissingCredentialsBeforeNetworkAccess()
    {
        var transport = new RecordingTransport();
        var sender = new SmtpEmailSender(Options.Create(new SmtpEmailOptions()), transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("fan@example.com", "Subject", "Body"));
        Assert.Null(transport.From);
    }

    [Fact]
    public async Task SendAsync_PassesAliasAndLoginToTransport()
    {
        var transport = new RecordingTransport();
        var sender = new SmtpEmailSender(Options.Create(new SmtpEmailOptions
        {
            Username = "owner@gmail.com",
            AppPassword = "example-password",
        }), transport);

        await sender.SendAsync("fan@example.com", "Deletion request", "Your request was received.");

        Assert.Equal("owner@gmail.com", transport.Username);
        Assert.Equal("support@queenzone.org", transport.From);
        Assert.Equal("fan@example.com", transport.To);
    }

    private sealed class RecordingTransport : ISmtpTransport
    {
        public string? Username { get; private set; }
        public string? From { get; private set; }
        public string? To { get; private set; }

        public Task SendAsync(SmtpEmailOptions settings, MailMessage message, CancellationToken cancellationToken)
        {
            Username = settings.Username;
            From = message.From?.Address;
            To = Assert.Single(message.To).Address;
            return Task.CompletedTask;
        }
    }
}

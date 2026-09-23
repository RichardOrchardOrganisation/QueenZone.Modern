# Outbound email

Server-side website and mobile API flows send mail through the registered `IEmailSender` service in
`src/QueenZone.Web/Notifications/SmtpEmailSender.cs`. Callers construct an `OutboundEmail` with a
recipient, subject, plain-text body, and optional reply-to address or HTML body:

```csharp
await emailSender.SendAsync(
    new OutboundEmail(recipientEmail, "Queenzone update", "Your plain-text message."),
    cancellationToken);
```

The sender always uses the configured `SmtpEmail__FromAddress`; callers cannot impersonate a
visitor by changing From. `ReplyToAddress` is for replies to contact submissions. When an HTML
body is present, the sender includes both plain-text and HTML views. Mobile features call this
service from the backend API; Gmail credentials never belong in the mobile app.
Build HTML from trusted templates and escape any visitor-supplied text before adding it.

`SmtpEmailSender` authenticates to Gmail using `SmtpEmail__Username` and
`SmtpEmail__AppPassword` from Bitwarden-backed App Service settings. Its transport is isolated
behind `ISmtpTransport`, so application flows can use a fake sender in tests. Contact request
notification and immediate account deletion confirmation are the first callers. They catch
send failures after committing their primary operation: a mail outage must not undo a stored
contact request or account deletion. Contact requests remain available in the admin queue.
There is currently no mail retry queue, so a failed confirmation may need manual follow-up.

See [Bitwarden secret mappings](../bitwarden-secrets.md) for deployment configuration.

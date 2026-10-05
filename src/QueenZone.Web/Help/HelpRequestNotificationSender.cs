namespace QueenZone.Web;

public sealed class HelpRequestNotificationSender(
    IEmailSender? emailSender = null,
    ILogger<HelpRequestService>? logger = null)
{
    public async Task SendAsync(Guid requestId, OutboundEmail notification, CancellationToken cancellationToken)
    {
        if (emailSender is null)
        {
            return;
        }

        try
        {
            await emailSender.SendAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError("Could not email contact request {RequestId}: {ErrorType}.", requestId, ex.GetType().Name);
        }
    }
}

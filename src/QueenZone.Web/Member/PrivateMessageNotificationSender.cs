using Microsoft.Extensions.Logging;

namespace QueenZone.Web;

public sealed partial class PrivateMessageNotificationSender(
    INotificationDispatcher notificationDispatcher,
    ILogger<PrivateMessageService> logger)
{
    public async Task SendAsync(
        Guid conversationId,
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationDispatcher.NotifyPrivateMessageAsync(
                conversationId,
                recipientMemberId,
                senderMemberId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PushDispatchFailedAfterPrivateMessage(
                logger,
                ex,
                recipientMemberId,
                conversationId,
                ex.Message);
        }
    }

}

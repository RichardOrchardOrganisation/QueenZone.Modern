using Microsoft.Extensions.Logging;

namespace QueenZone.Web;

public sealed partial class PrivateMessageNotificationSender
{
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1200,
            EventName = "PushDispatchFailedAfterPrivateMessage",
            Level = LogLevel.Warning,
            Message = "Push dispatch failed after private message to member {MemberId} conversation {ConversationId}: {Error}")]
        public static partial void PushDispatchFailedAfterPrivateMessage(
            ILogger logger,
            Exception exception,
            Guid memberId,
            Guid conversationId,
            string error);
    }
}

using Microsoft.Extensions.Logging;

namespace QueenZone.Web;

public sealed partial class ForumPostWriteEffects
{
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1100,
            EventName = "PushDispatchFailedAfterForumReply",
            Level = LogLevel.Warning,
            Message = "Push dispatch failed after forum reply {TopicId}/{PostId} by member {MemberId}: {Error}")]
        public static partial void PushDispatchFailedAfterForumReply(
            ILogger logger,
            Exception exception,
            int topicId,
            int postId,
            Guid memberId,
            string error);

    }
}

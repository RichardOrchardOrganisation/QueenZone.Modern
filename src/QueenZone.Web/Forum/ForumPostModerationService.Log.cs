using Microsoft.Extensions.Logging;

namespace QueenZone.Web;

public sealed partial class ForumPostModerationService
{
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1101,
            EventName = "AutoSuspendedMember",
            Level = LogLevel.Warning,
            Message = "Auto-suspended member {MemberId}: {Signature} {ElapsedSeconds:0}s after registration.")]
        public static partial void AutoSuspendedMember(
            ILogger logger,
            Guid memberId,
            string signature,
            double elapsedSeconds);
    }
}

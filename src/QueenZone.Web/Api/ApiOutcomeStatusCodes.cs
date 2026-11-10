using Microsoft.AspNetCore.WebUtilities;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// The single place the JSON API maps typed service outcomes to HTTP status codes (#2436).
/// Callers branch on the outcome enum, never on the user-facing message, so rewording a
/// message cannot change the response status.
/// </summary>
public static class ApiOutcomeStatusCodes
{
    public static int For(ForumPostReportOutcome outcome) => outcome switch
    {
        ForumPostReportOutcome.PostNotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    public static int For(PrivateMessageReportOutcome outcome) => outcome switch
    {
        PrivateMessageReportOutcome.MessageNotFound or PrivateMessageReportOutcome.NotAParticipant =>
            StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    public static int For(PrivateMessageSendOutcome outcome) => outcome switch
    {
        PrivateMessageSendOutcome.NotAParticipant or PrivateMessageSendOutcome.ConversationNotFound =>
            StatusCodes.Status404NotFound,
        PrivateMessageSendOutcome.RateLimited => StatusCodes.Status429TooManyRequests,
        PrivateMessageSendOutcome.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };

    public static int For(FanPerformanceReportOutcome outcome) => outcome switch
    {
        FanPerformanceReportOutcome.NotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    public static int For(HelpRequestSubmitOutcome outcome) => outcome switch
    {
        HelpRequestSubmitOutcome.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>Problem response whose title is the standard reason phrase for <paramref name="statusCode"/>.</summary>
    public static IResult Problem(int statusCode, string? detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: ReasonPhrases.GetReasonPhrase(statusCode),
            detail: detail);
}

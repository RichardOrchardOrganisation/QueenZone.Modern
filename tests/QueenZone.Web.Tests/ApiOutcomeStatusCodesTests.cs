using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Pins the outcome-to-HTTP mapping for the JSON API (#2436). The mapping takes only the
/// typed outcome, so rewording a user-facing message cannot change the response status.
/// </summary>
public sealed class ApiOutcomeStatusCodesTests
{
    [Theory]
    [InlineData(ForumPostReportOutcome.PostNotFound, StatusCodes.Status404NotFound)]
    [InlineData(ForumPostReportOutcome.CannotReportOwn, StatusCodes.Status400BadRequest)]
    [InlineData(ForumPostReportOutcome.Invalid, StatusCodes.Status400BadRequest)]
    public void ForumPostReport(ForumPostReportOutcome outcome, int expected) =>
        Assert.Equal(expected, ApiOutcomeStatusCodes.For(outcome));

    [Theory]
    [InlineData(PrivateMessageReportOutcome.MessageNotFound, StatusCodes.Status404NotFound)]
    [InlineData(PrivateMessageReportOutcome.NotAParticipant, StatusCodes.Status404NotFound)]
    [InlineData(PrivateMessageReportOutcome.CannotReportOwn, StatusCodes.Status400BadRequest)]
    [InlineData(PrivateMessageReportOutcome.Invalid, StatusCodes.Status400BadRequest)]
    public void PrivateMessageReport(PrivateMessageReportOutcome outcome, int expected) =>
        Assert.Equal(expected, ApiOutcomeStatusCodes.For(outcome));

    [Theory]
    [InlineData(PrivateMessageSendOutcome.NotAParticipant, StatusCodes.Status404NotFound)]
    [InlineData(PrivateMessageSendOutcome.ConversationNotFound, StatusCodes.Status404NotFound)]
    [InlineData(PrivateMessageSendOutcome.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PrivateMessageSendOutcome.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(PrivateMessageSendOutcome.RecipientNotFound, StatusCodes.Status400BadRequest)]
    [InlineData(PrivateMessageSendOutcome.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(PrivateMessageSendOutcome.Failed, StatusCodes.Status400BadRequest)]
    public void PrivateMessageSend(PrivateMessageSendOutcome outcome, int expected) =>
        Assert.Equal(expected, ApiOutcomeStatusCodes.For(outcome));

    [Theory]
    [InlineData(FanPerformanceReportOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(FanPerformanceReportOutcome.Invalid, StatusCodes.Status400BadRequest)]
    public void FanPerformanceReport(FanPerformanceReportOutcome outcome, int expected) =>
        Assert.Equal(expected, ApiOutcomeStatusCodes.For(outcome));

    [Theory]
    [InlineData(HelpRequestSubmitOutcome.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(HelpRequestSubmitOutcome.DailyLimitReached, StatusCodes.Status400BadRequest)]
    [InlineData(HelpRequestSubmitOutcome.Invalid, StatusCodes.Status400BadRequest)]
    public void HelpRequestSubmit(HelpRequestSubmitOutcome outcome, int expected) =>
        Assert.Equal(expected, ApiOutcomeStatusCodes.For(outcome));

    [Fact]
    public void Problem_UsesStandardReasonPhraseAndKeepsDetail()
    {
        var result = Assert.IsType<ProblemHttpResult>(
            ApiOutcomeStatusCodes.Problem(StatusCodes.Status429TooManyRequests, "Reworded message."));

        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("Too Many Requests", result.ProblemDetails.Title);
        Assert.Equal("Reworded message.", result.ProblemDetails.Detail);
    }

    [Fact]
    public void FailureStatus_DoesNotDependOnMessageText()
    {
        var reworded = ForumPostReportResult.Failure(ForumPostReportOutcome.PostNotFound, "Something entirely different.");

        Assert.False(reworded.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, ApiOutcomeStatusCodes.For(reworded.Outcome));
    }
}

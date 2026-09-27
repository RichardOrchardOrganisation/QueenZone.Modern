using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Authenticated member writes under <c>/api/v1/member/*</c> (issues #746 / #926).
/// Photo submit requires <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>
/// and delegates to <see cref="PhotoSubmissionService.SubmitAsync"/> — the same
/// <c>ugc-photos</c> path, admin queue, and <see cref="MemberUploadQuotaService"/>
/// bucket as <c>/submit/photo</c>. Do not add a second quota counter.
/// News suggestions reuse <see cref="NewsSuggestionService.SubmitAsync"/> with the
/// same daily cap and active-URL dedupe as <c>/submit/news</c>.
/// </summary>
public static class MemberApiEndpoints
{
    public const string RootPath = "/api/v1/member";

    public const string PhotoSubmissionsPath = $"{RootPath}/photo-submissions";

    public const string NewsSuggestionsPath = $"{RootPath}/news-suggestions";

    public const string FanPerformanceSubmissionsPath = $"{RootPath}/fan-performance-submissions";

    public const int FanPerformanceRequestSizeLimitBytes = 28_000_000;

    public static void MapMemberApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(RootPath)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Member")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapPost("/photo-submissions", CreatePhotoSubmissionAsync)
            .WithName("CreateMemberPhotoSubmission")
            .WithSummary("Submit a photo for review. Same PhotoSubmissionService and daily quota as /submit/photo.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<PhotoSubmissionRequestDto>("multipart/form-data")
            .Produces<PhotoSubmissionCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .DisableAntiforgery();

        group.MapPost("/news-suggestions", CreateNewsSuggestionAsync)
            .WithName("CreateMemberNewsSuggestion")
            .WithSummary("Suggest a news URL for review. Same NewsSuggestionService as /submit/news.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<NewsSuggestionRequestDto>("application/json")
            .Produces<NewsSuggestionCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .DisableAntiforgery();

        group.MapPost("/fan-performance-submissions", CreateFanPerformanceSubmissionAsync)
            .WithName("CreateMemberFanPerformanceSubmission")
            .WithSummary("Submit a fan performance for review. Same FanPerformanceSubmissionService and daily quota as /submit/fan-performance.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<FanPerformanceSubmissionRequestDto>("multipart/form-data")
            .Produces<FanPerformanceSubmissionCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithMetadata(new RequestSizeLimitAttribute(FanPerformanceRequestSizeLimitBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = FanPerformanceRequestSizeLimitBytes })
            .DisableAntiforgery();
    }

    internal static async Task<IResult> CreatePhotoSubmissionAsync(
        ClaimsPrincipal user,
        HttpRequest request,
        PhotoSubmissionService photoSubmissionService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        if (!request.HasFormContentType)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Photo submissions must be sent as multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var title = FirstNonEmpty(form["title"].ToString(), form["Title"].ToString());
        var description = FirstNonEmpty(form["description"].ToString(), form["Description"].ToString());
        var suggestedCategory = FirstNonEmpty(
            form["suggestedCategory"].ToString(),
            form["SuggestedCategory"].ToString());
        var approximateYear = TryParseInt(
            FirstNonEmpty(form["approximateYear"].ToString(), form["ApproximateYear"].ToString()));
        var approximateDate = TryParseDateOnly(
            FirstNonEmpty(form["approximateDate"].ToString(), form["ApproximateDate"].ToString()));
        var photo = FindPhotoFile(form.Files);
        if (photo is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "A photo file is required.");
        }

        await using var stream = photo.OpenReadStream();
        var result = await photoSubmissionService.SubmitAsync(
            memberId.Value,
            title ?? string.Empty,
            description,
            suggestedCategory,
            approximateYear,
            approximateDate,
            stream,
            photo.FileName,
            cancellationToken);

        if (!result.Succeeded || result.Submission is null)
        {
            return MapSubmitFailure(result.Error);
        }

        var submission = result.Submission;
        var dto = new PhotoSubmissionCreatedDto(
            submission.Id,
            submission.Status,
            submission.Title,
            submission.SubmittedAt);
        return Results.Created($"{PhotoSubmissionsPath}/{submission.Id:D}", dto);
    }

    internal static IResult MapSubmitFailure(string? error)
    {
        if (IsQuotaLimitError(error))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too Many Requests",
                detail: error);
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: error ?? "Could not submit photo.");
    }

    internal static bool IsQuotaLimitError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("Daily upload", StringComparison.Ordinal)
            || message.Contains("Uploads are temporarily disabled", StringComparison.Ordinal)
            || message.Contains("exceeds the daily size limit", StringComparison.Ordinal);
    }

    private static IFormFile? FindPhotoFile(IFormFileCollection files)
    {
        foreach (var name in new[] { "photo", "Photo", "photoFile", "PhotoFile" })
        {
            var file = files.GetFile(name);
            if (file is { Length: > 0 })
            {
                return file;
            }
        }

        return files.FirstOrDefault(file => file is { Length: > 0 });
    }

    private static IFormFile? FindAudioFile(IFormFileCollection files)
    {
        foreach (var name in new[] { "audio", "Audio", "audioFile", "AudioFile" })
        {
            var file = files.GetFile(name);
            if (file is { Length: > 0 })
            {
                return file;
            }
        }

        return files.FirstOrDefault(file => file is { Length: > 0 });
    }

    private static bool ParseRightsAccepted(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<IResult> CreateFanPerformanceSubmissionAsync(
        ClaimsPrincipal user,
        HttpRequest request,
        FanPerformanceSubmissionService fanPerformanceSubmissionService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        if (!request.HasFormContentType)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Fan performance submissions must be sent as multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var title = FirstNonEmpty(form["title"].ToString(), form["Title"].ToString());
        var coveredSong = FirstNonEmpty(form["coveredSong"].ToString(), form["CoveredSong"].ToString());
        var performedBy = FirstNonEmpty(form["performedBy"].ToString(), form["PerformedBy"].ToString());
        var description = FirstNonEmpty(form["description"].ToString(), form["Description"].ToString());
        var rightsAccepted = ParseRightsAccepted(
            FirstNonEmpty(
                form["rightsDeclarationAccepted"].ToString(),
                form["RightsDeclarationAccepted"].ToString()));
        var audio = FindAudioFile(form.Files);
        if (audio is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "An audio file is required.");
        }

        await using var stream = audio.OpenReadStream();
        var result = await fanPerformanceSubmissionService.SubmitAsync(
            memberId.Value,
            title ?? string.Empty,
            coveredSong ?? string.Empty,
            performedBy ?? string.Empty,
            description,
            rightsAccepted,
            stream,
            audio.FileName,
            cancellationToken);

        if (!result.Succeeded || result.Submission is null)
        {
            return MapSubmitFailure(result.Error ?? "Could not submit fan performance.");
        }

        var submission = result.Submission;
        return Results.Created(
            $"{FanPerformanceSubmissionsPath}/{submission.Id:D}",
            new FanPerformanceSubmissionCreatedDto(
                submission.Id,
                submission.Status,
                submission.Title,
                submission.SubmittedAt));
    }

    internal static async Task<IResult> CreateNewsSuggestionAsync(
        ClaimsPrincipal user,
        NewsSuggestionRequestDto? request,
        NewsSuggestionService newsSuggestionService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var outcome = await newsSuggestionService.SubmitAsync(
            memberId.Value,
            request?.Url ?? string.Empty,
            request?.Title,
            request?.Notes,
            cancellationToken);

        return MapNewsSuggestionOutcome(outcome);
    }

    internal static IResult MapNewsSuggestionOutcome(SubmitOutcome outcome)
    {
        // Records synthesize a copy constructor, so CS8509 cannot see the nested sum as closed.
#pragma warning disable CS8509
        return outcome switch
        {
            SubmitOutcome.Accepted accepted => Results.Created(
                $"{NewsSuggestionsPath}/{accepted.Suggestion.Id:D}",
                new NewsSuggestionCreatedDto(
                    accepted.Suggestion.Id,
                    accepted.Suggestion.Status,
                    accepted.Suggestion.Url,
                    accepted.Suggestion.Title,
                    accepted.Suggestion.SubmittedAt)),
            SubmitOutcome.InvalidField invalid => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: invalid.Message),
            SubmitOutcome.SignInRequired signIn => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: signIn.Message),
            SubmitOutcome.DuplicateActive duplicate => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: duplicate.Message),
            SubmitOutcome.DailyLimit limit => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too Many Requests",
                detail: limit.Message),
        };
#pragma warning restore CS8509
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static DateOnly? TryParseDateOnly(string? value) =>
        DateOnly.TryParse(value, out var parsed) ? parsed : null;
}

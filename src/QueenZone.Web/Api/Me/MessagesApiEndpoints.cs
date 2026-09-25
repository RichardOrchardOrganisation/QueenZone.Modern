using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Signed-in member inbox, replies, and compose for the mobile app
/// (issues #737 / #738 / #739). Reuses <see cref="PrivateMessageService"/> so
/// unread counts, SortKey assignment, recipient search, and privacy/block
/// rules match website <c>/messages</c>. Opening a conversation marks it
/// read the same way as <c>GET /messages/{id}</c>. Requires
/// <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>.
/// </summary>
public static class MessagesApiEndpoints
{
    public const string Path = "/api/v1/me/messages";

    public const string UnreadCountPath = $"{Path}/unread-count";

    public const string RecipientsPath = $"{Path}/recipients";

    public const string ArchivedPath = $"{Path}/archived";

    public static string ConversationPath(Guid conversationId) => $"{Path}/{conversationId:D}";

    public static string ReportPath(Guid conversationId, Guid messageId) =>
        $"{Path}/{conversationId:D}/messages/{messageId:D}/report";

    public static string ArchivePath(Guid conversationId) => $"{ConversationPath(conversationId)}/archive";

    public static string UnarchivePath(Guid conversationId) => $"{ConversationPath(conversationId)}/unarchive";

    public static string BlockPath(Guid conversationId) => $"{ConversationPath(conversationId)}/block";

    public static string UnblockPath(Guid conversationId) => $"{ConversationPath(conversationId)}/unblock";

    public static void MapMessagesApiEndpoints(this WebApplication app)
    {
        var group = app.MapApiV1Group("/api/v1/me", "Messages")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapAuthorizedPagedList<InboxConversationDto>(
            "/messages",
            GetInboxAsync,
            "GetMemberInbox",
            "Paged inbox conversations with unread counts matching /messages.");

        group.MapGet("/messages/unread-count", GetUnreadCountAsync)
            .WithName("GetMemberUnreadConversationCount")
            .WithSummary("Unread conversation count matching the website messages header badge.")
            .Produces<UnreadConversationsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/messages/recipients", SearchRecipientsAsync)
            .WithName("SearchMemberMessageRecipients")
            .WithSummary("Display-name recipient search matching GET /messages/compose?q=.")
            .Produces<MessageRecipientsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapAuthorizedPagedList<InboxConversationDto>(
            "/messages/archived",
            GetArchivedInboxAsync,
            "GetMemberArchivedInbox",
            "Paged archived conversations matching GET /messages/archived.");

        group.MapPost("/messages", ComposeAsync)
            .WithName("ComposeMemberMessage")
            .WithSummary("Start or continue a conversation matching POST /messages/compose.")
            .Accepts<ComposeMessageRequest>("application/json")
            .Produces<ConversationDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/messages/{conversationId:guid}", GetConversationAsync)
            .WithName("GetMemberConversation")
            .WithSummary("Open a conversation and mark it read, matching GET /messages/{id}.")
            .Produces<ConversationDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/messages/{conversationId:guid}", CreateReplyAsync)
            .WithName("ReplyToMemberConversation")
            .WithSummary("Send a reply with the same SortKey rules as POST /messages/{id}.")
            .Accepts<ConversationReplyRequest>("application/json")
            .Produces<ConversationDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/messages/{conversationId:guid}/archive", ArchiveConversationAsync)
            .WithName("ArchiveMemberConversation")
            .WithSummary("Archive a conversation matching POST /messages/{id} (Archive handler).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/messages/{conversationId:guid}/unarchive", UnarchiveConversationAsync)
            .WithName("UnarchiveMemberConversation")
            .WithSummary("Move an archived conversation back to the inbox, matching POST /messages/archived (Unarchive handler).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/messages/{conversationId:guid}/block", BlockConversationParticipantAsync)
            .WithName("BlockMemberConversationParticipant")
            .WithSummary("Block the other participant in a conversation, matching POST /messages/{id} (Block handler).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/messages/{conversationId:guid}/unblock", UnblockConversationParticipantAsync)
            .WithName("UnblockMemberConversationParticipant")
            .WithSummary("Unblock the other participant in a conversation, matching POST /messages/{id} (Unblock handler).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/messages/{conversationId:guid}/messages/{messageId:guid}/report", ReportMessageAsync)
            .WithName("ReportMemberMessage")
            .WithSummary("Report an abusive private message without notifying the reported member.")
            .Accepts<ReportMessageRequest>("application/json")
            .Produces<ReportMessageDto>(StatusCodes.Status201Created)
            .Produces<ReportMessageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static Task<IResult> GetInboxAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetInboxPageAsync(
            httpContext,
            user,
            page,
            pageSize,
            privateMessageService.GetInboxAsync,
            cancellationToken);

    internal static async Task<IResult> GetUnreadCountAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var count = await privateMessageService.CountUnreadConversationsAsync(
            memberId,
            cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new UnreadConversationsDto(count));
    }

    internal static async Task<IResult> GetConversationAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var size = ApiPagination.Normalize(
            1,
            pageSize,
            PrivateMessageLimits.ConversationPageSize,
            PrivateMessageLimits.MaxConversationPageSize).PageSize;
        int? resolvedPage = page is null or < 1 ? null : page;
        var mapped = await MapConversationAsync(
            privateMessageService,
            conversationId,
            memberId,
            markRead: true,
            resolvedPage,
            size,
            cancellationToken);
        if (mapped is null)
        {
            return ConversationNotFound();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(mapped);
    }

    internal static async Task<IResult> SearchRecipientsAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        string? q,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var matches = await privateMessageService.SearchRecipientsAsync(
            memberId,
            q,
            cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(MessagesApiMapper.ToRecipients(matches));
    }

    internal static Task<IResult> GetArchivedInboxAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetInboxPageAsync(
            httpContext,
            user,
            page,
            pageSize,
            privateMessageService.GetArchivedInboxAsync,
            cancellationToken);

    private static async Task<IResult> GetInboxPageAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        int? page,
        int? pageSize,
        Func<Guid, int, int, CancellationToken, Task<PrivateInboxPage>> load,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var request = ApiPagination.Normalize(
            page,
            pageSize,
            PrivateMessageLimits.InboxPageSize,
            PrivateMessageLimits.MaxInboxPageSize);
        var inbox = await load(memberId, request.Page, request.PageSize, cancellationToken);
        return ApiV1EndpointHelpers.OkNoStorePaged(
            httpContext,
            MessagesApiMapper.ToInboxItems(inbox.Items),
            inbox.Page,
            inbox.PageSize,
            inbox.TotalCount);
    }

    internal static async Task<IResult> ArchiveConversationAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var archived = await privateMessageService.ArchiveConversationAsync(
            conversationId,
            memberId,
            cancellationToken);
        if (!archived)
        {
            return ConversationNotFound();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    internal static async Task<IResult> UnarchiveConversationAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var unarchived = await privateMessageService.UnarchiveConversationAsync(
            conversationId,
            memberId,
            cancellationToken);
        if (!unarchived)
        {
            return ConversationNotFound();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    internal static async Task<IResult> BlockConversationParticipantAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var detail = await privateMessageService.GetConversationAsync(
            conversationId,
            memberId,
            markRead: false,
            page: null,
            cancellationToken: cancellationToken);
        if (detail is null)
        {
            return ConversationNotFound();
        }

        var result = await privateMessageService.BlockAsync(
            memberId,
            detail.OtherParticipantId,
            cancellationToken);
        if (!result.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: result.ErrorMessage ?? "Unable to block member.");
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    internal static async Task<IResult> UnblockConversationParticipantAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var detail = await privateMessageService.GetConversationAsync(
            conversationId,
            memberId,
            markRead: false,
            page: null,
            cancellationToken: cancellationToken);
        if (detail is null)
        {
            return ConversationNotFound();
        }

        await privateMessageService.UnblockAsync(
            memberId,
            detail.OtherParticipantId,
            cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    internal static async Task<IResult> ComposeAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        IIdempotencyStore idempotencyStore,
        ComposeMessageRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "A JSON body is required.");
        }

        if (request.RecipientMemberId is null || request.RecipientMemberId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Choose a recipient.");
        }

        var payloadHash = IdempotentApiWrites.MessageComposePayload(
            request.RecipientMemberId.Value,
            request.Body);
        var executed = await IdempotentApiWrites.ExecuteAsync(
            httpContext.Request,
            memberId,
            IdempotencyOperationKinds.MessageCompose,
            payloadHash,
            idempotencyStore,
            async ct =>
            {
                var result = await privateMessageService.ComposeAsync(
                    memberId,
                    request.RecipientMemberId.Value,
                    request.Body,
                    ct);
                if (!result.Succeeded || result.ConversationId is null)
                {
                    return new IdempotentWrite(MapSendFailure(result), null);
                }

                var mapped = await MapConversationAsync(
                    privateMessageService,
                    result.ConversationId.Value,
                    memberId,
                    markRead: true,
                    page: null,
                    pageSize: PrivateMessageLimits.ConversationPageSize,
                    ct);
                if (mapped is null)
                {
                    return new IdempotentWrite(ConversationNotFound(), null);
                }

                return IdempotentApiWrites.Created(
                    httpContext.Request,
                    mapped.DetailPath,
                    mapped,
                    payloadHash);
            },
            cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return executed;
    }

    internal static async Task<IResult> CreateReplyAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        IIdempotencyStore idempotencyStore,
        Guid conversationId,
        ConversationReplyRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "A JSON body is required.");
        }

        var payloadHash = IdempotentApiWrites.MessageReplyPayload(conversationId, request.Body);
        var executed = await IdempotentApiWrites.ExecuteAsync(
            httpContext.Request,
            memberId,
            IdempotencyOperationKinds.MessageReply,
            payloadHash,
            idempotencyStore,
            async ct =>
            {
                var result = await privateMessageService.ReplyAsync(
                    conversationId,
                    memberId,
                    request.Body,
                    ct);
                if (!result.Succeeded)
                {
                    return new IdempotentWrite(MapSendFailure(result), null);
                }

                var mapped = await MapConversationAsync(
                    privateMessageService,
                    conversationId,
                    memberId,
                    markRead: true,
                    page: null,
                    pageSize: PrivateMessageLimits.ConversationPageSize,
                    ct);
                if (mapped is null)
                {
                    return new IdempotentWrite(ConversationNotFound(), null);
                }

                return IdempotentApiWrites.Created(
                    httpContext.Request,
                    mapped.DetailPath,
                    mapped,
                    payloadHash);
            },
            cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return executed;
    }

    internal static async Task<IResult> ReportMessageAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        PrivateMessageService privateMessageService,
        Guid conversationId,
        Guid messageId,
        ReportMessageRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var result = await privateMessageService.ReportMessageAsync(
            memberId,
            conversationId,
            messageId,
            request?.Reason,
            cancellationToken);
        if (!result.Succeeded || result.ReportId is null)
        {
            return MapReportFailure(result);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        var dto = new ReportMessageDto(result.ReportId.Value, result.AlreadyReported);
        if (result.AlreadyReported)
        {
            return Results.Ok(dto);
        }

        return Results.Created(ReportPath(conversationId, messageId), dto);
    }

    private static async Task<ConversationDetailDto?> MapConversationAsync(
        PrivateMessageService privateMessageService,
        Guid conversationId,
        Guid memberId,
        bool markRead,
        int? page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var detail = await privateMessageService.GetConversationAsync(
            conversationId,
            memberId,
            markRead,
            page,
            pageSize,
            cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var canSendReply = await privateMessageService.CanSendReplyAsync(
            memberId,
            detail,
            cancellationToken);
        var hasBlockedOtherParticipant = await privateMessageService.HasBlockedAsync(
            memberId,
            detail.OtherParticipantId,
            cancellationToken);
        return MessagesApiMapper.ToConversation(detail, canSendReply, hasBlockedOtherParticipant);
    }

    private static IResult MapSendFailure(PrivateMessageSendResult result)
    {
        var message = result.ErrorMessage ?? PrivateMessageService.UnableToSendMessage;
        if (IsConversationMissing(message))
        {
            return ConversationNotFound();
        }

        if (string.Equals(message, PrivateMessageService.RateLimitedMessage, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too Many Requests",
                detail: message);
        }

        if (string.Equals(message, PrivateMessageService.UnableToSendMessage, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: message);
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: message);
    }

    private static IResult MapReportFailure(PrivateMessageReportResult result)
    {
        var message = result.ErrorMessage ?? PrivateMessageReportText.MessageNotFound;
        if (string.Equals(message, PrivateMessageReportText.NotAParticipant, StringComparison.Ordinal)
            || string.Equals(message, PrivateMessageReportText.MessageNotFound, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: message);
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: message);
    }

    private static bool IsConversationMissing(string message) =>
        string.Equals(message, "You are not a participant in this conversation.", StringComparison.Ordinal)
        || string.Equals(message, "Conversation not found.", StringComparison.Ordinal);

    private static IResult ConversationNotFound() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: "Conversation was not found.");

    private static Guid RequireMemberId(ClaimsPrincipal user, out IResult? failure)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            failure = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
            return Guid.Empty;
        }

        failure = null;
        return memberId.Value;
    }
}

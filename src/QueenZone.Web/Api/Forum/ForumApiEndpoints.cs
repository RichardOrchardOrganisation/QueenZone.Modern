using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// <c>/api/v1/forum/*</c> routes for browsing boards, topics, and topic threads
/// (issues #731 / #732), authenticated create-topic and reply writes (#733),
/// plus poll state, vote, and close (#734). Reads require no authentication:
/// the website forum index, category, and topic pages are public. Visibility
/// is the same <see cref="IForumRepository"/> path used by Razor Pages —
/// synthetic boards and unvalidated topic starters stay hidden. Topic posts
/// default and clamp <c>pageSize</c> to <see cref="ForumRoutes.PostsPageSize"/>.
/// Writes require <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>
/// and reuse <see cref="ForumPostWriteService"/> (sanitization, attachments,
/// <see cref="ForumPostRateLimiter"/>). Poll vote/close reuse
/// <see cref="IForumPollRepository"/> plus <see cref="ForumPollVoteMapper"/>
/// (the same one-vote-per-member and closed rules as
/// <c>/forum/poll/{id}/vote</c>). Topic Watch (#735) is <c>GET</c>/<c>POST</c>/
/// <c>DELETE /topics/{id}/watch</c> under <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>
/// via <see cref="TopicWatchService"/>. Attachment metadata includes the existing
/// cookie-gated <c>/forum/attachment/...</c> <c>url</c> plus additive
/// <c>downloadUrl</c> under <c>/api/v1/forum/attachments/...</c>
/// (<see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>). The mobile
/// client opens <c>downloadUrl</c> only.
/// </summary>
public static class ForumApiEndpoints
{
    public const string RootPath = "/api/v1/forum";

    public static void MapForumApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(RootPath)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Forum")
            .DisableAntiforgery();

        var memberGroup = app.MapGroup("/api/v1/me/forum")
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Forum moderation")
            .DisableAntiforgery()
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy);

        memberGroup.MapPost("/posts/{postId:int}/report", ReportPostAsync)
            .WithName("ReportForumPost")
            .WithSummary("Report a visible forum post. Duplicate submissions return the existing report.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.MemberWrite)
            .Accepts<ForumPostReportRequestDto>("application/json")
            .Produces<ForumPostReportResponseDto>(StatusCodes.Status201Created)
            .Produces<ForumPostReportResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        memberGroup.MapPost("/posts/{postId:int}/block", BlockPostAuthorAsync)
            .WithName("BlockForumPostAuthor")
            .WithSummary("Block the linked member who authored a visible forum post.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.MemberWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        memberGroup.MapPost("/posts/moderation-state", GetPostModerationStateAsync)
            .WithName("GetForumPostModerationState")
            .WithSummary("Return report and block state for the supplied forum post page.")
            .Accepts<ForumPostModerationStateRequestDto>("application/json")
            .Produces<ForumPostModerationStateDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/categories", GetCategoriesAsync)
            .WithName("GetForumCategories")
            .WithSummary("Paged list of public forum boards, in website sort order.")
            .Produces<ApiPagedResponse<ForumCategoryListItemDto>>();

        group.MapGet("/categories/{id:int}", GetCategoryAsync)
            .WithName("GetForumCategory")
            .WithSummary("A single public forum board.")
            .Produces<ForumCategoryListItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/stats", GetStatsAsync)
            .WithName("GetForumStats")
            .WithSummary("Public forum index totals (boards, threads, posts). Same ToForumIndexStats source as /forum.")
            .Produces<ForumIndexStatsDto>();

        group.MapGet("/recent-threads", GetRecentThreadsAsync)
            .WithName("GetForumRecentThreads")
            .WithSummary("Cross-board recent-activity thread feed, most-recent first. Same source as the website forum index activity list.")
            .Produces<IReadOnlyList<ForumRecentThreadDto>>();

        group.MapGet("/categories/{id:int}/topics", GetCategoryTopicsAsync)
            .WithName("GetForumCategoryTopics")
            .WithSummary("Paged public topics in a board. Sticky threads first, then last activity.")
            .Produces<ApiPagedResponse<ForumTopicListItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/categories/{id:int}/topics", CreateTopicAsync)
            .WithName("CreateForumTopic")
            .WithSummary("Create a topic in a public board. Same validation and rate limit as /forum/c/{slug}/new-thread.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<ForumWriteRequestDto>("application/json", "multipart/form-data")
            .Produces<ForumTopicCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/topics/{id:int}", GetTopicAsync)
            .WithName("GetForumTopic")
            .WithSummary("A single public forum topic header.")
            .Produces<ForumTopicDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/topics/{id:int}/posts", GetTopicPostsAsync)
            .WithName("GetForumTopicPosts")
            .WithSummary("Paged public posts in a topic, chronological, matching website pages (pageSize defaults to 15).")
            .Produces<ApiPagedResponse<ForumPostDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/topics/{id:int}/posts", CreateReplyAsync)
            .WithName("CreateForumReply")
            .WithSummary("Reply to a public topic. Same validation and rate limit as the website topic form.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<ForumWriteRequestDto>("application/json", "multipart/form-data")
            .Produces<ForumPostCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPatch("/topics/{id:int}/posts/{postId:int}", UpdatePostAsync)
            .WithName("UpdateForumPost")
            .WithSummary("Edit a public forum post. Same owner/admin and edit-window rules as /forum/post/{id}/edit.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<ForumPostUpdateRequestDto>("application/json")
            .Produces<ForumPostCreatedDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/topics/{id:int}/poll", GetTopicPollAsync)
            .WithName("GetForumTopicPoll")
            .WithSummary("Poll on a public topic. Same vote-vs-results flags as the website topic page.")
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/topics/{id:int}/poll/vote", VoteTopicPollAsync)
            .WithName("VoteForumTopicPoll")
            .WithSummary("Cast one ballot on a topic poll. Same one-vote and closed rules as /forum/poll/{id}/vote.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<ForumPollVoteRequestDto>("application/json")
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/topics/{id:int}/poll/close", CloseTopicPollAsync)
            .WithName("CloseForumTopicPoll")
            .WithSummary("Close a topic poll. Same author-or-admin rule as /forum/poll/{id}/close.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/topics/{id:int}/watch", GetTopicWatchAsync)
            .WithName("GetForumTopicWatch")
            .WithSummary("Whether the signed-in member is Watching this public topic. Watch is the opt-in for forum reply pushes.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/topics/{id:int}/watch", WatchTopicAsync)
            .WithName("WatchForumTopic")
            .WithSummary("Watch a public topic. Idempotent. Does not auto-watch on post.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/topics/{id:int}/watch", UnwatchTopicAsync)
            .WithName("UnwatchForumTopic")
            .WithSummary("Stop Watching a public topic. Idempotent when not currently Watching.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/attachments/legacy/{legacyPostId:int}", DownloadLegacyAttachmentAsync)
            .WithName("GetForumLegacyAttachment")
            .WithSummary("Member-gated legacy attachment. Same redirect as /forum/attachment/legacy/{legacyPostId}.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/attachments/{legacyPostId:int}/{attachmentId:guid}", DownloadModernAttachmentAsync)
            .WithName("GetForumAttachment")
            .WithSummary("Member-gated modern attachment. Same stream as /forum/attachment/{legacyPostId}/{attachmentId}.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetCategoriesAsync(
        IForumRepository forumRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var categories = await forumRepository.GetCategoriesAsync(cancellationToken);

        var pageItems = categories
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<ForumCategoryListItemDto>.Create(
            ForumApiMapper.ToCategoryListItems(pageItems),
            request.Page,
            request.PageSize,
            categories.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> ReportPostAsync(
        ClaimsPrincipal user,
        int postId,
        ForumPostReportRequestDto request,
        ForumPostReportService reportService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var result = await reportService.ReportAsync(memberId.Value, postId, request.Category, request.Details, cancellationToken);
        if (!result.Succeeded)
        {
            var notFound = result.ErrorMessage == ForumPostReportText.PostNotFound;
            return Results.Problem(
                statusCode: notFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                title: notFound ? "Not Found" : "Bad Request",
                detail: result.ErrorMessage);
        }

        var dto = new ForumPostReportResponseDto(result.ReportId!.Value, PrivateMessageReportStatus.Open, result.AlreadyReported);
        return result.AlreadyReported ? Results.Ok(dto) : Results.Created($"/api/v1/me/forum/reports/{dto.ReportId}", dto);
    }

    internal static async Task<IResult> BlockPostAuthorAsync(
        ClaimsPrincipal user,
        int postId,
        ForumPostReportService reportService,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (post?.AuthorMemberId is not Guid authorMemberId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "This post does not belong to an active member account.");
        }

        var result = await privateMessageService.BlockAsync(memberId.Value, authorMemberId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: result.ErrorMessage);
    }

    internal static async Task<IResult> GetPostModerationStateAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        ForumPostModerationStateRequestDto request,
        IForumPostReportRepository reports,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }
        if (request.PostIds.Count > ForumRoutes.PostsPageSize || request.AuthorMemberIds.Count > ForumRoutes.PostsPageSize)
        {
            return Results.BadRequest();
        }

        var reported = await reports.GetReportedPostIdsAsync(memberId.Value, request.PostIds, cancellationToken);
        var blocked = await privateMessageService.ListBlockedMemberIdsAsync(memberId.Value, request.AuthorMemberIds, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ForumPostModerationStateDto(reported.ToList(), blocked.ToList()));
    }

    internal static async Task<IResult> GetStatsAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var categories = await publicQueryCache.GetForumCategoriesAsync(cancellationToken);
        var threadCount = await publicQueryCache.GetForumThreadCountAsync(cancellationToken);
        var stats = PublicContentMapper.ToForumIndexStats(categories, threadCount);
        return Results.Ok(ForumApiMapper.ToIndexStats(stats));
    }

    internal static async Task<IResult> GetRecentThreadsAsync(
        PublicQueryCacheService publicQueryCache,
        int? count,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(count ?? 3, 1, ForumRoutes.RecentThreadsCount);
        var threads = await publicQueryCache.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount, cancellationToken);
        return Results.Ok(ForumApiMapper.ToRecentThreads(threads.Take(clamped)));
    }

    internal static async Task<IResult> GetCategoryAsync(
        IForumRepository forumRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var category = await forumRepository.GetCategoryByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public forum board with id '{id}'.");
        }

        return Results.Ok(ForumApiMapper.ToCategoryListItem(category));
    }

    internal static async Task<IResult> GetCategoryTopicsAsync(
        IForumRepository forumRepository,
        int id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var category = await forumRepository.GetCategoryByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public forum board with id '{id}'.");
        }

        var request = ApiPagination.Normalize(page, pageSize);
        var topicsPage = await forumRepository.GetCategoryTopicsPageAsync(
            id,
            request.Page,
            request.PageSize,
            cancellationToken);

        var response = ApiPagedResponse<ForumTopicListItemDto>.Create(
            ForumApiMapper.ToTopicListItems(topicsPage.Topics),
            request.Page,
            request.PageSize,
            topicsPage.TotalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetTopicAsync(
        IForumRepository forumRepository,
        IForumWriteRepository forumWriteRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var topicPage = await forumRepository.GetTopicPostsPageAsync(id, 1, 1, cancellationToken);
        if (topicPage is null)
        {
            return TopicNotFound(id);
        }

        var thread = await forumWriteRepository.GetThreadAsync(id, cancellationToken);
        return Results.Ok(ForumApiMapper.ToTopicDetail(
            topicPage.Header,
            topicPage.TotalCount,
            thread?.IsLocked == true));
    }

    internal static async Task<IResult> GetTopicPostsAsync(
        IForumRepository forumRepository,
        UgcHtml ugcHtml,
        int id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(
            page,
            pageSize,
            ForumRoutes.PostsPageSize,
            ForumRoutes.PostsPageSize);
        var topicPage = await forumRepository.GetTopicPostsPageAsync(
            id,
            request.Page,
            request.PageSize,
            cancellationToken);
        if (topicPage is null)
        {
            return TopicNotFound(id);
        }

        var response = ApiPagedResponse<ForumPostDto>.Create(
            ForumApiMapper.ToPosts(topicPage.Posts, ugcHtml),
            request.Page,
            request.PageSize,
            topicPage.TotalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> CreateTopicAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        int id,
        ForumPostWriteService writeService,
        IIdempotencyStore idempotencyStore,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var (title, body, files) = await ReadWriteRequestAsync(request, cancellationToken);
        var payloadHash = IdempotentApiWrites.ForumPayload(id, title, body, files);
        return await IdempotentApiWrites.ExecuteAsync(
            request,
            memberId.Value,
            IdempotencyOperationKinds.ForumCreateTopic,
            payloadHash,
            idempotencyStore,
            async ct =>
            {
                var outcome = await writeService.CreateTopicAsync(
                    memberId.Value,
                    user.Identity?.Name,
                    id,
                    title,
                    body,
                    files,
                    poll: null,
                    ct);

                if (!outcome.Succeeded)
                {
                    return new IdempotentWrite(MapWriteFailure(outcome, categoryId: id, topicId: null), null);
                }

                var dto = new ForumTopicCreatedDto(
                    outcome.TopicId,
                    outcome.PostId,
                    outcome.Title,
                    ForumRoutes.GetTopicCanonicalPath(outcome.TopicId, outcome.Title));
                return IdempotentApiWrites.Created(
                    request,
                    $"{RootPath}/topics/{outcome.TopicId}",
                    dto,
                    payloadHash);
            },
            cancellationToken);
    }

    internal static async Task<IResult> CreateReplyAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        int id,
        ForumPostWriteService writeService,
        IIdempotencyStore idempotencyStore,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var (_, body, files) = await ReadWriteRequestAsync(request, cancellationToken);
        var payloadHash = IdempotentApiWrites.ForumPayload(id, title: null, body, files);
        return await IdempotentApiWrites.ExecuteAsync(
            request,
            memberId.Value,
            IdempotencyOperationKinds.ForumCreateReply,
            payloadHash,
            idempotencyStore,
            async ct =>
            {
                var outcome = await writeService.CreateReplyAsync(
                    memberId.Value,
                    user.Identity?.Name,
                    id,
                    body,
                    files,
                    ct);

                if (!outcome.Succeeded)
                {
                    return new IdempotentWrite(MapWriteFailure(outcome, categoryId: null, topicId: id), null);
                }

                var detailPath = ForumRoutes.GetTopicCanonicalPath(outcome.TopicId, outcome.Title)
                    + $"#post-{outcome.PostId}";
                var dto = new ForumPostCreatedDto(
                    outcome.PostId,
                    outcome.TopicId,
                    detailPath);
                return IdempotentApiWrites.Created(request, detailPath, dto, payloadHash);
            },
            cancellationToken);
    }

    internal static async Task<IResult> UpdatePostAsync(
        ClaimsPrincipal user,
        int id,
        int postId,
        ForumPostUpdateRequestDto request,
        IForumWriteRepository forumWriteRepository,
        UgcHtml ugcHtml,
        Microsoft.Extensions.Options.IOptions<ForumOptions> forumOptions,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var sanitizedBody = ugcHtml.Sanitize(request.Body ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Body is required.");
        }

        var existing = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (existing is null || existing.TopicId != id)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public forum post with id '{postId}'.");
        }

        var result = await forumWriteRepository.UpdatePostAsync(
            postId,
            memberId.Value,
            sanitizedBody,
            isAdmin: false,
            forumOptions.Value.PostEditWindowMinutes,
            request.UpdatedAt,
            cancellationToken);

        return result.Status switch
        {
            ForumPostUpdateStatus.Success => Results.Ok(new ForumPostCreatedDto(
                postId,
                result.TopicId,
                ForumRoutes.GetTopicCanonicalPath(result.TopicId, result.TopicSubject) + $"#post-{postId}")),
            ForumPostUpdateStatus.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public forum post with id '{postId}'."),
            ForumPostUpdateStatus.ConcurrencyConflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: OptimisticConcurrencyException.UserMessage),
            ForumPostUpdateStatus.EditWindowExpired => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "This post can no longer be edited."),
            ForumPostUpdateStatus.EditingDisabled => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Post editing is currently disabled."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "You do not have permission to edit this post."),
        };
    }

    internal static async Task<IResult> GetTopicPollAsync(
        HttpContext httpContext,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            await TryGetBearerMemberIdAsync(httpContext),
            cancellationToken);
        return loaded.Result ?? Results.Ok(ForumApiMapper.ToPoll(loaded.Poll!));
    }

    internal static async Task<IResult> VoteTopicPollAsync(
        ClaimsPrincipal user,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        ForumPollVoteRequestDto? request,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            memberId,
            cancellationToken);
        if (loaded.Result is not null)
        {
            return loaded.Result;
        }

        var optionIds = ForumPollVoteMapper.ParseOptionIds(request?.OptionIds, request?.OptionId);
        try
        {
            await pollRepository.CastVoteAsync(loaded.Poll!.PollId, memberId.Value, optionIds, cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToProblemResult(ex);
        }

        return await ReloadPollAsync(pollRepository, id, memberId.Value, cancellationToken);
    }

    internal static async Task<IResult> CloseTopicPollAsync(
        ClaimsPrincipal user,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            memberId,
            cancellationToken);
        if (loaded.Result is not null)
        {
            return loaded.Result;
        }

        try
        {
            // Mobile JWTs are never treated as admin (see json-api-v1.md).
            await pollRepository.ClosePollAsync(
                loaded.Poll!.PollId,
                memberId.Value,
                isAdmin: false,
                cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToProblemResult(ex);
        }

        return await ReloadPollAsync(pollRepository, id, memberId.Value, cancellationToken);
    }

    private static IResult MapWriteFailure(ForumWriteOutcome outcome, int? categoryId, int? topicId)
    {
        return outcome.Status switch
        {
            ForumWriteStatus.CategoryNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public forum board with id '{categoryId}'."),
            ForumWriteStatus.TopicNotFound => TopicNotFound(topicId ?? 0),
            ForumWriteStatus.TopicLocked => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: ForumPostWriteService.TopicLockedMessage),
            ForumWriteStatus.MemberSuspended => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: ForumPostWriteService.SuspendedMessage),
            ForumWriteStatus.RateLimited => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too Many Requests",
                detail: ForumPostWriteService.RateLimitedMessage),
            ForumWriteStatus.ValidationFailed or ForumWriteStatus.AttachmentFailed => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: string.Join(' ', outcome.FieldErrors.Select(error => error.Message))),
            _ => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Server Error",
                detail: "Unable to save this post."),
        };
    }

    private static async Task<(string? Title, string? Body, IReadOnlyList<IFormFile> Files)> ReadWriteRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var title = FirstNonEmpty(
                form["title"].ToString(),
                form["Title"].ToString(),
                form["subject"].ToString(),
                form["Subject"].ToString());
            var body = FirstNonEmpty(form["body"].ToString(), form["Body"].ToString());
            var files = form.Files.Where(file => file is { Length: > 0 }).ToList();
            return (title, body, files);
        }

        try
        {
            var json = await request.ReadFromJsonAsync<ForumWriteRequestDto>(cancellationToken);
            return (json?.ResolvedTitle, json?.Body, []);
        }
        catch (JsonException)
        {
            return (null, null, []);
        }
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

    private static async Task<(ForumPollResults? Poll, IResult? Result)> LoadPublicPollAsync(
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int topicId,
        Guid? viewerMemberId,
        CancellationToken cancellationToken)
    {
        var topicPage = await forumRepository.GetTopicPostsPageAsync(topicId, 1, 1, cancellationToken);
        if (topicPage is null)
        {
            return (null, TopicNotFound(topicId));
        }

        var poll = await pollRepository.GetPollWithResultsAsync(
            topicId,
            viewerMemberId,
            viewerIsAdmin: false,
            cancellationToken);
        if (poll is null)
        {
            return (null, PollNotFound(topicId));
        }

        return (poll, null);
    }

    private static async Task<IResult> ReloadPollAsync(
        IForumPollRepository pollRepository,
        int topicId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var poll = await pollRepository.GetPollWithResultsAsync(
            topicId,
            memberId,
            viewerIsAdmin: false,
            cancellationToken);
        return poll is null
            ? PollNotFound(topicId)
            : Results.Ok(ForumApiMapper.ToPoll(poll));
    }

    internal static async Task<IResult> GetTopicWatchAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.GetStatusAsync(memberId.Value, id, cancellationToken);
        if (status is null)
        {
            return TopicNotFound(id);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ForumTopicWatchDto(status.Watching));
    }

    internal static async Task<IResult> WatchTopicAsync(
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.WatchAsync(memberId.Value, id, cancellationToken);
        return status is null
            ? TopicNotFound(id)
            : Results.Ok(new ForumTopicWatchDto(status.Watching));
    }

    internal static async Task<IResult> UnwatchTopicAsync(
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.UnwatchAsync(memberId.Value, id, cancellationToken);
        return status is null
            ? TopicNotFound(id)
            : Results.Ok(new ForumTopicWatchDto(status.Watching));
    }

    internal static Task<IResult> DownloadLegacyAttachmentAsync(
        int legacyPostId,
        IForumAttachmentRepository attachmentRepository,
        CancellationToken cancellationToken) =>
        ForumAttachmentEndpoints.ServeLegacyAsync(legacyPostId, attachmentRepository, cancellationToken);

    internal static Task<IResult> DownloadModernAttachmentAsync(
        int legacyPostId,
        Guid attachmentId,
        IForumAttachmentRepository attachmentRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken) =>
        ForumAttachmentEndpoints.ServeModernAsync(
            legacyPostId,
            attachmentId,
            attachmentRepository,
            blobUploadService,
            cancellationToken);

    private static async Task<Guid?> TryGetBearerMemberIdAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            return null;
        }

        var bearer = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
        return bearer.Succeeded ? ForumMember.GetMemberId(bearer.Principal) : null;
    }

    private static IResult PollNotFound(int topicId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No poll on public forum topic '{topicId}'.");

    private static IResult TopicNotFound(int id) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No public forum topic with id '{id}'.");
}

using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Member-gated forum attachment downloads under <c>/api/v1/forum</c>.
/// Registered by <see cref="ForumApiEndpoints.MapForumApiEndpoints"/> on the
/// existing forum group so paths and route names stay unchanged.
/// </summary>
public static class ForumAttachmentApiEndpoints
{
    internal static void MapForumAttachmentApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/attachments/legacy/{legacyPostId:int}", DownloadLegacyAttachmentAsync)
            .WithName("GetForumLegacyAttachment")
            .WithSummary("Member-gated legacy attachment. Same stream as /forum/attachment/legacy/{legacyPostId}.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces(StatusCodes.Status200OK)
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

    internal static Task<IResult> DownloadLegacyAttachmentAsync(
        int legacyPostId,
        IForumAttachmentRepository attachmentRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken) =>
        ForumAttachmentEndpoints.ServeLegacyAsync(
            legacyPostId,
            attachmentRepository,
            blobUploadService,
            cancellationToken);

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
}

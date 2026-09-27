using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Member-gated forum attachment downloads.
/// Modern and legacy attachments both stream from private storage with
/// <c>Content-Disposition: attachment</c>. Legacy files are not redirected to a CDN.
/// </summary>
public static class ForumAttachmentEndpoints
{
    public static void MapForumAttachmentEndpoints(this WebApplication app)
    {
        app.MapGet("/forum/attachment/legacy/{legacyPostId:int}", async (
                int legacyPostId,
                IForumAttachmentRepository attachmentRepository,
                IBlobUploadService blobUploadService,
                CancellationToken cancellationToken) =>
            await ServeLegacyAsync(
                legacyPostId,
                attachmentRepository,
                blobUploadService,
                cancellationToken))
            .RequireAuthorization(MemberAuthenticationSchemes.MemberPolicy)
            .WithName("DownloadLegacyForumAttachment");

        app.MapGet("/forum/attachment/{legacyPostId:int}/{attachmentId:guid}", async (
                int legacyPostId,
                Guid attachmentId,
                IForumAttachmentRepository attachmentRepository,
                IBlobUploadService blobUploadService,
                CancellationToken cancellationToken) =>
            await ServeModernAsync(
                legacyPostId,
                attachmentId,
                attachmentRepository,
                blobUploadService,
                cancellationToken))
            .RequireAuthorization(MemberAuthenticationSchemes.MemberPolicy)
            .WithName("DownloadForumAttachment");
    }

    internal static async Task<IResult> ServeLegacyAsync(
        int legacyPostId,
        IForumAttachmentRepository attachmentRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken)
    {
        var legacy = await attachmentRepository.GetLegacyAsync(legacyPostId, cancellationToken);
        if (legacy is null || string.IsNullOrWhiteSpace(legacy.FileName))
        {
            return Results.NotFound();
        }

        // Legacy filenames must be bare blob names, not paths (check raw value before GetFileName,
        // which would strip directory segments including ".." on some platforms).
        var rawName = legacy.FileName.Trim();
        if (string.IsNullOrWhiteSpace(rawName)
            || rawName.Contains("..", StringComparison.Ordinal)
            || rawName.Contains('/', StringComparison.Ordinal)
            || rawName.Contains('\\', StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var fileName = Path.GetFileName(rawName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Results.NotFound();
        }

        try
        {
            var content = await blobUploadService.OpenReadAsync(
                ForumAttachmentPaths.LegacyContainerName,
                fileName,
                cancellationToken);

            if (content is null)
            {
                return Results.NotFound();
            }

            var contentType = string.IsNullOrWhiteSpace(content.ContentType)
                ? ForumAttachmentValidator.GuessContentType(fileName)
                : content.ContentType;

            // Download disposition so PDF/HTML is not rendered on the site origin.
            return Results.File(
                content.Stream,
                contentType,
                fileDownloadName: fileName,
                enableRangeProcessing: false);
        }
        catch (NotSupportedException)
        {
            return Results.NotFound();
        }
    }

    internal static async Task<IResult> ServeModernAsync(
        int legacyPostId,
        Guid attachmentId,
        IForumAttachmentRepository attachmentRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken)
    {
        var attachment = await attachmentRepository.GetAsync(legacyPostId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Results.NotFound();
        }

        try
        {
            var content = await blobUploadService.OpenReadAsync(
                attachment.ContainerName,
                attachment.BlobPath,
                cancellationToken);

            if (content is null)
            {
                return Results.NotFound();
            }

            await attachmentRepository.IncrementDownloadCountAsync(attachmentId, cancellationToken);

            var contentType = string.IsNullOrWhiteSpace(attachment.MimeType)
                ? content.ContentType ?? MediaTypeNames.Application.Octet
                : attachment.MimeType;

            // Prefer original filename for Content-Disposition download.
            return Results.File(
                content.Stream,
                contentType,
                fileDownloadName: attachment.OriginalFileName,
                enableRangeProcessing: false);
        }
        catch (NotSupportedException)
        {
            return Results.NotFound();
        }
    }
}

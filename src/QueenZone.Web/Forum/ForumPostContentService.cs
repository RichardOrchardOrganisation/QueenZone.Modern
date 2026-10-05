namespace QueenZone.Web;

public sealed class ForumPostContentService(
    UgcHtml ugcHtml,
    ForumAttachmentValidator attachmentValidator,
    ForumAttachmentUploadService attachmentUploadService)
{
    public string NormalizeForStorage(string? body) => ugcHtml.NormalizeForStorage(body);

    public ForumAttachmentValidationResult ValidateAttachments(IReadOnlyList<IFormFile>? attachments) =>
        attachmentValidator.Validate(SelectFiles(attachments));

    public async Task UploadAttachmentsAsync(
        int postId,
        Guid memberId,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        await attachmentUploadService.UploadAndSaveAsync(postId, memberId, files, cancellationToken);
    }

    private static IReadOnlyList<IFormFile> SelectFiles(IReadOnlyList<IFormFile>? attachments) =>
        attachments?.Where(file => file is { Length: > 0 }).ToList() ?? [];
}

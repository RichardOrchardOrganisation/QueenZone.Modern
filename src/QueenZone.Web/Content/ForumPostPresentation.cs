namespace QueenZone.Web;

public sealed record ForumPostPresentation(ForumPostViewModel Post, bool CanReply, bool IsAdmin, string ReturnPath)
{
    public static ForumPostPresentation Create(ForumPostViewModel post, bool canReply, bool isAdmin, string returnPath) =>
        new(post, canReply, isAdmin, returnPath + $"#post-{post.Id}");

    public string? AuthorHref => AuthorPath(Post.AuthorMemberId, Post.AuthorLegacyUserId);
    public static string? AuthorPath(Guid? memberId, int? legacyId) => memberId is Guid id
        ? $"/members/{id}" : legacyId is int legacy ? $"/forum/archive-authors/{legacy}" : null;
    public bool CanEdit => Post.IsAuthor && Post.CanEdit;
    public bool CanReport => !Post.IsAuthor;
    public bool ShowActions => CanEdit || Post.CanMessage || CanReply || IsAdmin || CanReport;
    public string? MessageHref => Post.CanMessage && Post.AuthorMemberId is Guid id ? $"/messages/compose?to={id}" : null;
    public bool HasSignature => !string.IsNullOrWhiteSpace(Post.Signature);
    public bool HasEditedLabel => !string.IsNullOrWhiteSpace(Post.EditedLabel);
    public bool HasMemberSince => Post.AuthorMemberSince is not null;
    public string? MemberSinceAttribute => Post.AuthorMemberSince?.ToString("yyyy-MM-dd");
    public string? MemberSinceLabel => Post.AuthorMemberSince?.ToString("MMMM yyyy");
    public string PostedAtAttribute => Post.PostedAt.ToString("yyyy-MM-ddTHH:mm:ss");
    public string PostedAtLabel => Post.PostedAt.ToString("dd MMMM yyyy HH:mm");
    public bool HasAttachments => Post.Attachments is { Count: > 0 };
    public string AttachmentsLabel => Post.Attachments.Count > 1 ? "ATTACHMENTS" : "ATTACHMENT";
    public string ReportReturnUrl => Uri.EscapeDataString(ReturnPath);
    public static bool HasThumbnail(ForumAttachmentViewModel attachment) => attachment.IsImage && !string.IsNullOrEmpty(attachment.ThumbnailUrl);
}

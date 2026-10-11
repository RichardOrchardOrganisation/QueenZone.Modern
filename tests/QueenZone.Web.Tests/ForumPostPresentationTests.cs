namespace QueenZone.Web.Tests;

public sealed class ForumPostPresentationTests
{
    private static readonly Guid MemberId = Guid.Parse("c04a265a-f33e-4a18-883c-55ef50b0d211");
    private static ForumPostViewModel Post => new(123, "Body", new DateTime(2026, 7, 1, 12, 30, 0), "Fan", null, null, []);

    [Fact]
    public void Action_matrix_preserves_author_reply_message_and_admin_permissions()
    {
        foreach (var author in new[] { false, true })
            foreach (var editable in new[] { false, true })
                foreach (var message in new[] { false, true })
                    foreach (var reply in new[] { false, true })
                        foreach (var admin in new[] { false, true })
                        {
                            var view = ForumPostPresentation.Create(Post with { IsAuthor = author, CanEdit = editable, CanMessage = message, AuthorMemberId = MemberId }, reply, admin, "/forum/topic/1002/topic?page=2");
                            Assert.Equal(author && editable, view.CanEdit);
                            Assert.Equal(!author, view.CanReport);
                            Assert.Equal(author && editable || message || reply || admin || !author, view.ShowActions);
                            Assert.Equal(reply, view.CanReply);
                            Assert.Equal(admin, view.IsAdmin);
                            Assert.False(view.CanBlock);
                            Assert.Equal(message ? $"/messages/compose?to={MemberId}" : null, view.MessageHref);
                        }
        Assert.Null(ForumPostPresentation.Create(Post with { CanMessage = true }, false, false, "/forum").MessageHref);
        Assert.True(ForumPostPresentation.Create(
            Post with { AuthorMemberId = MemberId }, false, false, "/forum", isSignedIn: true).CanBlock);
        Assert.False(ForumPostPresentation.Create(Post, false, false, "/forum", isSignedIn: true).CanBlock);
    }

    [Fact]
    public void Author_destination_prefers_modern_identity_then_legacy_identity()
    {
        Assert.Equal($"/members/{MemberId}", ForumPostPresentation.AuthorPath(MemberId, 47));
        Assert.Equal("/forum/archive-authors/47", ForumPostPresentation.AuthorPath(null, 47));
        Assert.Null(ForumPostPresentation.AuthorPath(null, null));
        Assert.Equal($"/members/{MemberId}", ForumPostPresentation.Create(Post with { AuthorMemberId = MemberId }, false, false, "/forum").AuthorHref);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("Edited yesterday", true)]
    public void Optional_text_keeps_whitespace_fallbacks(string? text, bool visible)
    {
        var view = ForumPostPresentation.Create(Post with { Signature = text, EditedLabel = text }, false, false, "/forum");
        Assert.Equal(visible, view.HasSignature);
        Assert.Equal(visible, view.HasEditedLabel);
    }

    [Fact]
    public void Date_labels_and_report_return_url_keep_existing_formats()
    {
        var view = ForumPostPresentation.Create(Post with { AuthorMemberSince = new DateTime(2020, 2, 3) }, false, false, "/forum/topic/1002/topic?page=2");
        Assert.True(view.HasMemberSince);
        Assert.Equal("2020-02-03", view.MemberSinceAttribute);
        Assert.Equal("February 2020", view.MemberSinceLabel);
        Assert.Equal("2026-07-01T12:30:00", view.PostedAtAttribute);
        Assert.Equal("01 July 2026 12:30", view.PostedAtLabel);
        Assert.Equal("/forum/topic/1002/topic?page=2#post-123", view.ReturnPath);
        Assert.Equal(Uri.EscapeDataString(view.ReturnPath), view.ReportReturnUrl);
        var empty = ForumPostPresentation.Create(Post, false, false, "/forum");
        Assert.False(empty.HasMemberSince);
        Assert.Null(empty.MemberSinceAttribute);
        Assert.Null(empty.MemberSinceLabel);
    }

    [Fact]
    public void Attachment_counts_and_thumbnail_require_both_image_and_url()
    {
        var attachment = new ForumAttachmentViewModel("file.png", "/file", ".png", "1 KB");
        var empty = ForumPostPresentation.Create(Post, false, false, "/forum");
        Assert.False(empty.HasAttachments);
        Assert.Equal("ATTACHMENT", empty.AttachmentsLabel);
        var single = empty with { Post = Post with { Attachments = [attachment] } };
        Assert.True(single.HasAttachments);
        Assert.Equal("ATTACHMENT", single.AttachmentsLabel);
        Assert.Equal("ATTACHMENTS", (single with { Post = Post with { Attachments = [attachment, attachment] } }).AttachmentsLabel);
        Assert.False(ForumPostPresentation.HasThumbnail(attachment));
        Assert.False(ForumPostPresentation.HasThumbnail(attachment with { ThumbnailUrl = "/thumb" }));
        Assert.False(ForumPostPresentation.HasThumbnail(attachment with { IsImage = true, ThumbnailUrl = "" }));
        Assert.True(ForumPostPresentation.HasThumbnail(attachment with { IsImage = true, ThumbnailUrl = "/thumb" }));
    }
}

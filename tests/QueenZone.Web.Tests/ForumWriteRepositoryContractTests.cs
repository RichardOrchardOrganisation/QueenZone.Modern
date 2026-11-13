using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class ForumWriteRepositoryContractTests
{
    protected abstract IForumWriteRepository Repository { get; }
    protected abstract Task<(Guid Id, string DisplayName)> SeedMemberAsync(string displayName);
    protected abstract Task<int> SeedCategoryAsync();

    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const int MissingId = 999_999;

    [Fact]
    public async Task CreateThread_TrimsSubjectAndPersistsStarter()
    {
        var member = await SeedMemberAsync("Alice");
        var category = await SeedCategoryAsync();
        var created = await Repository.CreateThreadAsync(new NewForumThread(
            category, member.Id, member.DisplayName, "  Discussion  ", "Starter body", Start));

        var thread = await Repository.GetThreadAsync(created.TopicId);
        Assert.NotNull(thread);
        Assert.Equal(created.TopicId, thread.TopicId);
        Assert.Equal(category, thread.CategoryId);
        Assert.Equal("Discussion", thread.Subject);
        Assert.Equal(Start, thread.CreatedAt);
        Assert.Equal(Start, thread.LastPostAt);
        Assert.Equal(1, thread.PostCount);
        var post = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(post);
        Assert.Equal(created.StarterPostId, post.PostId);
        Assert.Equal(created.TopicId, post.TopicId);
        Assert.Equal(thread.Subject, post.TopicSubject);
        Assert.Equal("Starter body", post.Body);
        Assert.Equal(member.Id, post.AuthorMemberId);
        Assert.Equal(member.DisplayName, post.AuthorDisplayName);
        Assert.Equal(Start, post.PostedAt);
        Assert.Equal(1, post.PositionInThread);
        Assert.Equal(0, post.EditCount);
        Assert.Null(post.EditedAt);
    }

    [Fact]
    public async Task Create_AllocatesIncreasingThreadAndPostIds()
    {
        var member = await SeedMemberAsync("Alice");
        var category = await SeedCategoryAsync();
        var first = await Repository.CreateThreadAsync(new NewForumThread(
            category, member.Id, member.DisplayName, "First", "First body", Start));
        var second = await Repository.CreateThreadAsync(new NewForumThread(
            category, member.Id, member.DisplayName, "Second", "Second body", Start));
        var reply = await Repository.CreatePostAsync(new NewForumPost(
            first.TopicId, member.Id, member.DisplayName, "Reply", Start.AddMinutes(1)));

        Assert.True(second.TopicId > first.TopicId);
        Assert.True(second.StarterPostId > first.StarterPostId);
        Assert.True(reply > second.StarterPostId);
    }

    [Fact]
    public async Task CreatePost_AppendsReplyAndUpdatesThread()
    {
        var member = await SeedMemberAsync("Alice");
        var created = await CreateThreadAsync(member, Start);
        var replyId = await Repository.CreatePostAsync(new NewForumPost(
            created.TopicId, member.Id, member.DisplayName, "Reply body", Start.AddMinutes(1)));

        var thread = await Repository.GetThreadAsync(created.TopicId);
        Assert.NotNull(thread);
        Assert.Equal(2, thread.PostCount);
        Assert.Equal(Start.AddMinutes(1), thread.LastPostAt);
        var reply = await Repository.GetPostAsync(replyId);
        Assert.NotNull(reply);
        Assert.Equal(created.TopicId, reply.TopicId);
        Assert.Equal(2, reply.PositionInThread);
        Assert.Equal("Reply body", reply.Body);
        Assert.Equal(Start.AddMinutes(1), reply.PostedAt);
        var starter = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(starter);
        Assert.Equal(1, starter.PositionInThread);
        Assert.Equal("Original", starter.Body);
    }

    [Fact]
    public async Task CreateThread_RejectsMissingCategory()
    {
        var member = await SeedMemberAsync("Alice");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository.CreateThreadAsync(new NewForumThread(
                MissingId, member.Id, member.DisplayName, "Missing", "Body", Start)));
        Assert.Equal("Forum category not found.", exception.Message);
    }

    [Fact]
    public async Task CreatePost_RejectsMissingThread()
    {
        var member = await SeedMemberAsync("Alice");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.CreatePostAsync(
            new NewForumPost(MissingId, member.Id, member.DisplayName, "Body", Start)));
    }

    [Fact]
    public async Task Get_ReturnsNullForMissingIds()
    {
        Assert.Null(await Repository.GetPostAsync(MissingId));
        Assert.Null(await Repository.GetThreadAsync(MissingId));
    }

    [Fact]
    public async Task Update_OwnerCanEditRepeatedlyWithUnlimitedWindow()
    {
        var member = await SeedMemberAsync("Alice");
        var created = await CreateThreadAsync(member, Start);
        var original = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(original);
        var result = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "First edit", false, -1);
        Assert.Equal(ForumPostUpdateStatus.Success, result.Status);
        Assert.Equal(created.TopicId, result.TopicId);
        Assert.Equal("Discussion", result.TopicSubject);
        var edited = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(edited);
        Assert.Equal("First edit", edited.Body);
        Assert.Equal(1, edited.EditCount);
        Assert.NotNull(edited.EditedAt);
        Assert.NotNull(edited.UpdatedAt);
        Assert.NotEqual(original.UpdatedAt, edited.UpdatedAt);

        var second = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "Second edit", false, -1);
        Assert.Equal(ForumPostUpdateStatus.Success, second.Status);
        var twiceEdited = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(twiceEdited);
        Assert.Equal("Second edit", twiceEdited.Body);
        Assert.Equal(2, twiceEdited.EditCount);
    }

    [Fact]
    public async Task Update_DisabledWindowPreservesPost()
    {
        var member = await SeedMemberAsync("Alice");
        var created = await CreateThreadAsync(member, Start);
        var original = await Repository.GetPostAsync(created.StarterPostId);
        var result = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "Rejected", false, 0);
        Assert.Equal(ForumPostUpdateStatus.EditingDisabled, result.Status);
        Assert.Equal(original, await Repository.GetPostAsync(created.StarterPostId));
    }

    [Fact]
    public async Task Update_ExpiredWindowPreservesPost()
    {
        var member = await SeedMemberAsync("Alice");
        var created = await CreateThreadAsync(member, Old);
        var original = await Repository.GetPostAsync(created.StarterPostId);
        var result = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "Rejected", false, 60);
        Assert.Equal(ForumPostUpdateStatus.EditWindowExpired, result.Status);
        Assert.Equal(original, await Repository.GetPostAsync(created.StarterPostId));
    }

    [Fact]
    public async Task Update_OtherMemberIsForbidden()
    {
        var author = await SeedMemberAsync("Alice");
        var editor = await SeedMemberAsync("Bob");
        var created = await CreateThreadAsync(author, Start);
        var original = await Repository.GetPostAsync(created.StarterPostId);
        var result = await Repository.UpdatePostAsync(created.StarterPostId, editor.Id, "Rejected", false, -1);
        Assert.Equal(ForumPostUpdateStatus.Forbidden, result.Status);
        Assert.Equal(original, await Repository.GetPostAsync(created.StarterPostId));
    }

    [Fact]
    public async Task Update_AdminCanEditOldPostWithoutChangingAuthor()
    {
        var author = await SeedMemberAsync("Alice");
        var admin = await SeedMemberAsync("Admin");
        var created = await CreateThreadAsync(author, Old);
        var result = await Repository.UpdatePostAsync(created.StarterPostId, admin.Id, "Admin edit", true, 60);
        Assert.Equal(ForumPostUpdateStatus.Success, result.Status);
        var post = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(post);
        Assert.Equal("Admin edit", post.Body);
        Assert.Equal(1, post.EditCount);
        Assert.Equal(author.Id, post.AuthorMemberId);
    }

    [Fact]
    public async Task Update_MissingPostReturnsNotFound()
    {
        var member = await SeedMemberAsync("Alice");
        var result = await Repository.UpdatePostAsync(MissingId, member.Id, "Missing", false, -1);
        Assert.Equal(ForumPostUpdateStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Update_RejectsStaleTokenAndAcceptsCurrentToken()
    {
        var member = await SeedMemberAsync("Alice");
        var created = await CreateThreadAsync(member, Start);
        var original = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(original);
        Assert.NotNull(original.UpdatedAt);
        var first = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "First edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.Success, first.Status);
        var edited = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(edited);
        Assert.Equal("First edit", edited.Body);
        Assert.Equal(1, edited.EditCount);
        Assert.NotNull(edited.UpdatedAt);
        Assert.NotEqual(original.UpdatedAt, edited.UpdatedAt);

        var stale = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "Stale edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(edited, await Repository.GetPostAsync(created.StarterPostId));
        var current = await Repository.UpdatePostAsync(created.StarterPostId, member.Id, "Current edit", false, -1, edited.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.Success, current.Status);
        var latest = await Repository.GetPostAsync(created.StarterPostId);
        Assert.NotNull(latest);
        Assert.Equal("Current edit", latest.Body);
        Assert.Equal(2, latest.EditCount);
    }

    [Fact]
    public async Task HideAndUnhide_AreIdempotentAndIsolatedByAuthor()
    {
        var alice = await SeedMemberAsync("Alice");
        var bob = await SeedMemberAsync("Bob");
        var created = await CreateThreadAsync(alice, Start);
        await Repository.CreatePostAsync(new NewForumPost(created.TopicId, alice.Id, alice.DisplayName, "Alice reply", Start));
        await Repository.CreatePostAsync(new NewForumPost(created.TopicId, bob.Id, bob.DisplayName, "Bob reply", Start));
        await CreateThreadAsync(bob, Start);
        Assert.Equal(2, await Repository.CountApprovedPostsByMemberAsync(alice.Id));
        Assert.Equal(2, await Repository.CountApprovedPostsByMemberAsync(bob.Id));
        var aliceSummary = await Repository.GetAuthorForumContentSummaryAsync(alice.Id, alice.DisplayName);
        var bobSummary = await Repository.GetAuthorForumContentSummaryAsync(bob.Id, bob.DisplayName);
        Assert.Equal(2, aliceSummary.PostCount);
        Assert.Equal(1, aliceSummary.ThreadCount);
        Assert.False(aliceSummary.IsHidden);

        await Repository.HideAuthorForumContentAsync(alice.Id, alice.DisplayName);
        Assert.Equal(0, await Repository.CountApprovedPostsByMemberAsync(alice.Id));
        Assert.Equal(2, await Repository.CountApprovedPostsByMemberAsync(bob.Id));
        Assert.Equal(aliceSummary with { IsHidden = true }, await Repository.GetAuthorForumContentSummaryAsync(alice.Id, alice.DisplayName));
        Assert.Equal(bobSummary, await Repository.GetAuthorForumContentSummaryAsync(bob.Id, bob.DisplayName));
        await Repository.HideAuthorForumContentAsync(alice.Id, alice.DisplayName);
        Assert.Equal(0, await Repository.CountApprovedPostsByMemberAsync(alice.Id));
        Assert.Equal(aliceSummary with { IsHidden = true }, await Repository.GetAuthorForumContentSummaryAsync(alice.Id, alice.DisplayName));

        await Repository.UnhideAuthorForumContentAsync(alice.Id, alice.DisplayName);
        Assert.Equal(2, await Repository.CountApprovedPostsByMemberAsync(alice.Id));
        Assert.Equal(2, await Repository.CountApprovedPostsByMemberAsync(bob.Id));
        Assert.Equal(aliceSummary, await Repository.GetAuthorForumContentSummaryAsync(alice.Id, alice.DisplayName));
        Assert.Equal(bobSummary, await Repository.GetAuthorForumContentSummaryAsync(bob.Id, bob.DisplayName));
    }

    [Fact]
    public async Task Counts_UnknownMemberReturnsZero()
    {
        var unknown = Guid.NewGuid();
        Assert.Equal(0, await Repository.CountApprovedPostsByMemberAsync(unknown));
        Assert.Equal(0, await Repository.CountPostsByMemberSinceAsync(unknown, Start));
    }

    [Fact]
    public async Task CountSince_IncludesBoundaryAndOnlyMembersVisiblePosts()
    {
        var alice = await SeedMemberAsync("Alice");
        var bob = await SeedMemberAsync("Bob");
        var created = await CreateThreadAsync(alice, Start.AddMinutes(-1));
        await Repository.CreatePostAsync(new NewForumPost(created.TopicId, alice.Id, alice.DisplayName, "At boundary", Start));
        await Repository.CreatePostAsync(new NewForumPost(created.TopicId, alice.Id, alice.DisplayName, "After boundary", Start.AddMinutes(1)));
        await Repository.CreatePostAsync(new NewForumPost(created.TopicId, bob.Id, bob.DisplayName, "Other member", Start));
        Assert.Equal(2, await Repository.CountPostsByMemberSinceAsync(alice.Id, Start));
        Assert.Equal(1, await Repository.CountPostsByMemberSinceAsync(bob.Id, Start));
        await Repository.HideAuthorForumContentAsync(alice.Id, alice.DisplayName);
        Assert.Equal(0, await Repository.CountPostsByMemberSinceAsync(alice.Id, Start));
        Assert.Equal(1, await Repository.CountPostsByMemberSinceAsync(bob.Id, Start));
        await Repository.UnhideAuthorForumContentAsync(alice.Id, alice.DisplayName);
        Assert.Equal(2, await Repository.CountPostsByMemberSinceAsync(alice.Id, Start));
    }

    private async Task<ForumThreadCreateResult> CreateThreadAsync((Guid Id, string DisplayName) member, DateTimeOffset createdAt) =>
        await Repository.CreateThreadAsync(new NewForumThread(
            await SeedCategoryAsync(), member.Id, member.DisplayName, "Discussion", "Original", createdAt));
}

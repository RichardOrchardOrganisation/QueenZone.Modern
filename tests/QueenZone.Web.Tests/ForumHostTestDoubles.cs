using System.Text;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

internal sealed class LockedForumWriteRepository : IForumWriteRepository
{
    public Task<ForumThreadCreateResult> CreateThreadAsync(
        NewForumThread thread,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ForumThreadCreateResult(200_001, 2_000_001));

    public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Locked.");

    public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ForumEditablePost?>(null);

    public Task<ForumPostUpdateResult> UpdatePostAsync(
        int postId,
        Guid editorMemberId,
        string sanitisedBody,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.Forbidden));

    public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ForumWriteThread?>(new ForumWriteThread(
            topicId,
            1,
            "Ranking every studio album",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            IsLocked: true));

    public Task<int> CountPostsByMemberSinceAsync(
        Guid memberId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<int> CountApprovedPostsByMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task HideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task UnhideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> EnsureCategoryAsync(
        string slug,
        string name,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(1);
}

internal sealed class FixedIdAttachmentRepository : IForumAttachmentRepository
{
    public static readonly Guid ModernAttachmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly StoredForumAttachment attachment;

    public FixedIdAttachmentRepository(StoredForumAttachment? attachment = null)
    {
        this.attachment = attachment ?? CreateModernAttachment();
    }

    public int DownloadCount { get; private set; }

    public void Reset() => DownloadCount = 0;

    public static StoredForumAttachment CreateModernAttachment() =>
        new(
            ModernAttachmentId,
            PostId: 1,
            LegacyPostId: 9001,
            OriginalFileName: "notes.txt",
            BlobPath: "members/test/notes.txt",
            ContainerName: BlobUploadContainers.Forum,
            FileSizeBytes: 16,
            MimeType: "text/plain",
            UploadedAt: DateTimeOffset.UtcNow,
            DownloadCount: 0);

    public Task AddAttachmentsAsync(
        int legacyPostId,
        IEnumerable<NewForumAttachment> attachments,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<StoredForumAttachment>> GetByLegacyPostIdsAsync(
        IReadOnlyCollection<int> legacyPostIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredForumAttachment>>(
            legacyPostIds.Contains(attachment.LegacyPostId) ? [attachment] : []);

    public Task<StoredForumAttachment?> GetAsync(
        int legacyPostId,
        Guid attachmentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            legacyPostId == attachment.LegacyPostId && attachmentId == attachment.Id
                ? attachment
                : null);

    public Task<LegacyForumAttachmentLookup?> GetLegacyAsync(
        int legacyPostId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LegacyForumAttachmentLookup?>(null);

    public Task IncrementDownloadCountAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (attachmentId == attachment.Id)
        {
            DownloadCount += 1;
        }

        return Task.CompletedTask;
    }
}

/// <summary>In-memory blob store for attachment download/upload tests.</summary>
internal sealed class MemoryBlobUploadService : IBlobUploadService
{
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> store = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> seed = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BlobUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string containerName,
        BlobUploadContext? context = null,
        CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var blobName = context?.PreferredBlobName
            ?? $"members/{Guid.NewGuid():N}/{Path.GetFileName(originalFileName)}";
        var contentType = ForumAttachmentValidator.GuessContentType(originalFileName);
        store[$"{containerName}/{blobName}"] = (buffer.ToArray(), contentType);
        return new BlobUploadResult
        {
            Container = containerName,
            BlobName = blobName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
        };
    }

    public Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        store.Remove($"{containerName}/{blobName}");
        return Task.CompletedTask;
    }

    public Task<BlobContent?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        if (!store.TryGetValue($"{containerName}/{blobName}", out var entry))
        {
            return Task.FromResult<BlobContent?>(null);
        }

        return Task.FromResult<BlobContent?>(new BlobContent
        {
            Stream = new MemoryStream(entry.Bytes),
            ContentType = entry.ContentType,
            ETag = $"\"{entry.Bytes.Length:x8}\"",
            ContentLength = entry.Bytes.LongLength,
        });
    }

    public void Reset()
    {
        store.Clear();
        foreach (var pair in seed)
        {
            store[pair.Key] = pair.Value;
        }
    }

    private void CaptureSeed()
    {
        seed.Clear();
        foreach (var pair in store)
        {
            seed[pair.Key] = pair.Value;
        }
    }

    public static MemoryBlobUploadService WithLegacyForumBlobs()
    {
        var memoryBlob = new MemoryBlobUploadService();
        memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("scan-bytes")),
            "anoto-setlist-scan.jpg",
            ForumAttachmentPaths.LegacyContainerName,
            new BlobUploadContext { PreferredBlobName = "anoto-setlist-scan.jpg" }).GetAwaiter().GetResult();
        memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-notes")),
            "opera-side-two-notes.pdf",
            ForumAttachmentPaths.LegacyContainerName,
            new BlobUploadContext { PreferredBlobName = "opera-side-two-notes.pdf" }).GetAwaiter().GetResult();
        memoryBlob.CaptureSeed();
        return memoryBlob;
    }

    public static MemoryBlobUploadService WithModernForumAttachment()
    {
        var memoryBlob = new MemoryBlobUploadService();
        memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("hello attachment")),
            "notes.txt",
            BlobUploadContainers.Forum,
            new BlobUploadContext { PreferredBlobName = "members/test/notes.txt" }).GetAwaiter().GetResult();
        memoryBlob.CaptureSeed();
        return memoryBlob;
    }
}

internal sealed class ThrowingForumArchiveAuthorRepository : IForumArchiveAuthorRepository
{
    public Task<ForumArchiveAuthorSummary?> GetSummaryAsync(
        int legacyUserId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ForumArchiveAuthorSummary?>(
            new ForumArchiveAuthorSummary(legacyUserId, "throwing-author", new DateTime(1970, 11, 27), PostCount: 1));

    public Task<MemberPublicActivityPage> GetPostsPageAsync(
        int legacyUserId,
        int page,
        int pageSize,
        int totalCount,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated archive-author query failure.");
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfMemberAccountRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfMemberAccountRepository repository;

    public EfMemberAccountRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        CreateLegacyPromotionTables();
        repository = new EfMemberAccountRepository(dbContext);
    }

    [Fact]
    public async Task ListExternalProvidersAsync_ReturnsDistinctOrderedProviders()
    {
        var account = await SeedAccountAsync("ef-fan@example.com", "EF Fan");
        await repository.AddExternalLoginAsync(account.Id, "GitHub", "gh-1", account.Email);
        await repository.AddExternalLoginAsync(account.Id, "Google", "g-1", account.Email);
        await repository.AddExternalLoginAsync(account.Id, "Google", "g-2", account.Email);

        var providers = await repository.ListExternalProvidersAsync(account.Id);

        Assert.Equal(["GitHub", "Google"], providers);
    }

    [Fact]
    public async Task ListExternalProvidersAsync_ReturnsEmpty_WhenNoneLinked()
    {
        var account = await SeedAccountAsync("lonely@example.com", "Lonely");

        var providers = await repository.ListExternalProvidersAsync(account.Id);

        Assert.Empty(providers);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_PersistsNewName()
    {
        var account = await SeedAccountAsync("rename@example.com", "Before");

        var updated = await repository.UpdateDisplayNameAsync(account.Id, "After");

        Assert.NotNull(updated);
        Assert.Equal("After", updated.DisplayName);

        var reloaded = await repository.FindByIdAsync(account.Id);
        Assert.Equal("After", reloaded!.DisplayName);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_ReturnsNull_WhenAccountMissing()
    {
        var updated = await repository.UpdateDisplayNameAsync(Guid.NewGuid(), "Ghost");

        Assert.Null(updated);
    }

    [Fact]
    public async Task UpdateMessagePrivacyAsync_PersistsSetting()
    {
        var account = await SeedAccountAsync("privacy-ef@example.com", "EF Privacy");

        var updated = await repository.UpdateMessagePrivacyAsync(account.Id, MemberMessagePrivacy.Nobody);
        Assert.Equal(MemberMessagePrivacy.Nobody, updated!.MessagePrivacy);

        var reloaded = await repository.FindByIdAsync(account.Id);
        Assert.Equal(MemberMessagePrivacy.Nobody, reloaded!.MessagePrivacy);
    }

    [Fact]
    public async Task UpdateAvatarUrlAsync_PersistsAndClearsPath()
    {
        var account = await SeedAccountAsync("avatar-ef@example.com", "EF Avatar");

        var updated = await repository.UpdateAvatarUrlAsync(account.Id, "members/x/avatar.webp");
        Assert.Equal("members/x/avatar.webp", updated!.AvatarUrl);

        var cleared = await repository.UpdateAvatarUrlAsync(account.Id, null);
        Assert.Null(cleared!.AvatarUrl);
    }

    [Fact]
    public async Task LinkLegacyUserIdAsync_PersistsAndIsFindable()
    {
        var account = await SeedAccountAsync("legacy-link@example.com", "Legacy Link");

        var linked = await repository.LinkLegacyUserIdAsync(account.Id, 4242);
        Assert.Equal(4242, linked!.LinkedLegacyUserId);

        var found = await repository.FindByLinkedLegacyUserIdAsync(4242);
        Assert.NotNull(found);
        Assert.Equal(account.Id, found.Id);

        // Second link is a no-op when already set.
        var again = await repository.LinkLegacyUserIdAsync(account.Id, 9999);
        Assert.Equal(4242, again!.LinkedLegacyUserId);

        var unlinked = await repository.UnlinkLegacyUserIdAsync(account.Id);
        Assert.Null(unlinked!.LinkedLegacyUserId);
        Assert.Null(await repository.FindByLinkedLegacyUserIdAsync(4242));

        var relinked = await repository.LinkLegacyUserIdAsync(account.Id, 9999);
        Assert.Equal(9999, relinked!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task SearchByDisplayNameAsync_MatchesAndExcludesMember()
    {
        var alice = await SeedAccountAsync("search-alice@example.com", "Search Alice");
        var bob = await SeedAccountAsync("search-bob@example.com", "Search Bob");
        await SeedAccountAsync("search-bobby@example.com", "Search Bobby");

        var matches = await repository.SearchByDisplayNameAsync("Search Bo", excludeMemberId: alice.Id);

        Assert.Contains(matches, m => m.MemberId == bob.Id);
        Assert.DoesNotContain(matches, m => m.MemberId == alice.Id);
        Assert.Contains(matches, m => m.DisplayName == "Search Bobby");
    }

    [Fact]
    public async Task SearchMembersAsync_MatchesDisplayNameOrEmail_AndPaginates()
    {
        await SeedAccountAsync("spammer@example.com", "Spam Bot");
        await SeedAccountAsync("regular@example.com", "Regular Fan");
        await SeedAccountAsync("other-spammer@junk.com", "Other Member");

        var byName = await repository.SearchMembersAsync("Spam", pageNumber: 1, pageSize: 50);
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal("Spam Bot", byName.Members[0].DisplayName);

        var byEmail = await repository.SearchMembersAsync("junk.com", pageNumber: 1, pageSize: 50);
        Assert.Equal(1, byEmail.TotalCount);
        Assert.Equal("Other Member", byEmail.Members[0].DisplayName);

        var page1 = await repository.SearchMembersAsync(null, pageNumber: 1, pageSize: 2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Members.Count);

        var page2 = await repository.SearchMembersAsync(null, pageNumber: 2, pageSize: 2);
        Assert.Single(page2.Members);
    }

    [Fact]
    public async Task SuspendAsync_ThenReinstateAsync_RoundTrips()
    {
        var account = await SeedAccountAsync("spammer2@example.com", "Spam Bot 2");
        var suspendedAt = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        var suspended = await repository.SuspendAsync(account.Id, "Posting spam links", "admin@queenzone.org", suspendedAt);
        Assert.True(suspended!.IsSuspended);
        Assert.Equal("Posting spam links", suspended.SuspendedReason);
        Assert.Equal("admin@queenzone.org", suspended.SuspendedByAdminEmail);
        Assert.Equal(suspendedAt, suspended.SuspendedAt);

        var reinstated = await repository.ReinstateAsync(account.Id);
        Assert.False(reinstated!.IsSuspended);
        Assert.Null(reinstated.SuspendedReason);
        Assert.Null(reinstated.SuspendedByAdminEmail);
        Assert.Null(reinstated.SuspendedAt);
    }

    [Fact]
    public async Task SuspendAsync_ReturnsNull_WhenAccountMissing()
    {
        var result = await repository.SuspendAsync(Guid.NewGuid(), "reason", "admin@queenzone.org", DateTime.UtcNow);
        Assert.Null(result);
    }

    [Fact]
    public async Task SocialLinks_AreUniquePerMemberAndChannel_AndAnonymiseDeletesRows()
    {
        var account = await SeedAccountAsync("socials-ef@example.com", "EF Socials");
        var other = await SeedAccountAsync("socials-other@example.com", "Other Socials");
        await repository.ReplaceSocialLinksAsync(account.Id,
        [
            new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
            new MemberSocialLink(MemberSocialChannel.TikTok, "https://www.tiktok.com/@queen"),
        ]);
        await repository.ReplaceSocialLinksAsync(other.Id,
        [
            new MemberSocialLink(MemberSocialChannel.X, "https://x.com/other"),
        ]);

        var listed = await repository.ListSocialLinksAsync(account.Id);
        Assert.Equal(
            [
                new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                new MemberSocialLink(MemberSocialChannel.TikTok, "https://www.tiktok.com/@queen"),
            ],
            listed);

        dbContext.ChangeTracker.Clear();
        dbContext.MemberSocialLinks.Add(new MemberSocialLinkEntity
        {
            MemberId = account.Id,
            Channel = "x",
            Url = "https://x.com/duplicate",
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();

        await repository.RequestDeletionAsync(account.Id, DateTime.UtcNow);
        Assert.Empty(await repository.ListSocialLinksAsync(account.Id));
        Assert.Equal(
            [new MemberSocialLink(MemberSocialChannel.X, "https://x.com/other")],
            await repository.ListSocialLinksAsync(other.Id));
    }

    [Fact]
    public async Task RequestDeletionAsync_AnonymisesImmediately_CancelRestores_AndPurgeMakesPermanent()
    {
        var account = await SeedAccountAsync("delete-me@example.com", "Delete Me");
        var otherMember = await SeedAccountAsync("other-member@example.com", "Other Member");
        account.AvatarUrl = $"members/{account.Id:N}/avatar.webp";
        await dbContext.SaveChangesAsync();
        dbContext.ModernForumCategories.Add(new ModernForumCategoryEntity
        {
            Id = 1,
            LegacyForumId = 1,
            Name = "Deletion tests",
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var thread = new ModernForumThreadEntity
        {
            LegacyTopicId = 8001,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = "Delete test",
            StartedByDisplayName = account.DisplayName,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.ModernForumThreads.Add(thread);
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            LegacyPostId = 9001,
            LegacyThreadTopicId = thread.LegacyTopicId,
            Thread = thread,
            LegacyForumId = 1,
            AuthorMemberId = account.Id,
            AuthorDisplayName = account.DisplayName,
            BodyHtml = "Retained body",
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            LegacyPostId = 9002,
            LegacyThreadTopicId = thread.LegacyTopicId,
            Thread = thread,
            LegacyForumId = 1,
            AuthorMemberId = otherMember.Id,
            AuthorDisplayName = otherMember.DisplayName,
            BodyHtml = "<p>Other member reply</p>",
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        dbContext.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = account.Id,
            Title = "Retained article",
            Slug = "retained-article",
            Body = "Retained article body",
            Status = ArticleSubmissionStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow,
        });
        dbContext.SearchDocuments.Add(new SearchDocumentEntity
        {
            Id = Guid.NewGuid(),
            SourceKey = "article:retained-article",
            ContentType = "article",
            Title = "Retained article",
            Body = "Retained article body",
            Url = "/articles/community/retained-article",
            AuthorDisplayName = account.DisplayName,
            IndexedAt = DateTimeOffset.UtcNow,
        });
        dbContext.SearchDocuments.Add(new SearchDocumentEntity
        {
            Id = Guid.NewGuid(),
            SourceKey = "forum-thread:8001",
            ContentType = "forum",
            Title = thread.Title,
            Body = "Retained body from starter",
            Summary = "Retained body from starter",
            Url = "/forum/topic/8001",
            AuthorDisplayName = account.DisplayName,
            ImageUrl = account.AvatarUrl,
            IndexedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        var requestedAt = new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc);

        var result = await repository.RequestDeletionAsync(account.Id, requestedAt);
        var second = await repository.RequestDeletionAsync(account.Id, requestedAt.AddMinutes(1));

        Assert.NotNull(result);
        Assert.False(result.AlreadyRequested);
        Assert.True(second!.AlreadyRequested);
        var reloaded = await dbContext.MemberAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.False(reloaded.IsSuspended);
        Assert.Equal(requestedAt, reloaded.DeletionRequestedAt);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, reloaded.DisplayName);
        Assert.Null(reloaded.AvatarUrl);
        Assert.Equal("Delete Me", reloaded.DeletionRecoveryDisplayName);
        Assert.Equal($"members/{account.Id:N}/avatar.webp", reloaded.DeletionRecoveryAvatarUrl);
        var post = await dbContext.ModernForumPosts.AsNoTracking().SingleAsync(item => item.LegacyPostId == 9001);
        Assert.Equal(account.Id, post.AuthorMemberId);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, post.AuthorDisplayName);
        var persistedThread = await dbContext.ModernForumThreads.AsNoTracking().SingleAsync();
        Assert.Equal("Delete test", persistedThread.Title);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, persistedThread.StartedByDisplayName);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, (await dbContext.SearchDocuments.AsNoTracking().SingleAsync(document => document.ContentType == "article")).AuthorDisplayName);
        var forumSearch = await dbContext.SearchDocuments.AsNoTracking().SingleAsync(document => document.ContentType == "forum");
        Assert.Equal("Delete test", forumSearch.Body);
        Assert.Equal("Delete test", forumSearch.Summary);
        Assert.Null(forumSearch.AuthorDisplayName);
        Assert.Null(forumSearch.ImageUrl);
        var audit = await dbContext.MemberAccountDeletionAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(MemberAccountDeletionPolicy.RequestedAuditAction, audit.Action);
        Assert.Equal(account.Id, audit.MemberAccountId);

        await repository.CancelDeletionAsync(account.Id, requestedAt.AddDays(2));

        reloaded = await dbContext.MemberAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal("Delete Me", reloaded.DisplayName);
        Assert.Equal($"members/{account.Id:N}/avatar.webp", reloaded.AvatarUrl);
        Assert.Null(reloaded.DeletionRecoveryDisplayName);
        Assert.Null(reloaded.DeletionRecoveryAvatarUrl);
        post = await dbContext.ModernForumPosts.AsNoTracking().SingleAsync(item => item.LegacyPostId == 9001);
        Assert.Equal(account.Id, post.AuthorMemberId);
        Assert.Equal("Delete Me", post.AuthorDisplayName);
        Assert.Equal("Delete Me", (await dbContext.ModernForumThreads.AsNoTracking().SingleAsync()).StartedByDisplayName);
        Assert.Equal("Delete Me", (await dbContext.SearchDocuments.AsNoTracking().SingleAsync(document => document.ContentType == "article")).AuthorDisplayName);

        var secondRequestAt = requestedAt.AddDays(3);
        await repository.RequestDeletionAsync(account.Id, secondRequestAt);
        var purge = await repository.PurgeDeletedAccountsAsync(secondRequestAt, secondRequestAt.AddDays(30));

        Assert.Equal(1, purge.PurgedCount);
        Assert.Equal([$"members/{account.Id:N}/avatar.webp"], purge.AvatarBlobPaths);
        post = await dbContext.ModernForumPosts.AsNoTracking().SingleAsync(item => item.LegacyPostId == 9001);
        Assert.Null(post.AuthorMemberId);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, post.AuthorDisplayName);
        Assert.Equal("<p>Post deleted by member.</p>", post.BodyHtml);
        persistedThread = await dbContext.ModernForumThreads.AsNoTracking().SingleAsync();
        Assert.Equal("Delete test", persistedThread.Title);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, persistedThread.StartedByDisplayName);
        var otherPost = await dbContext.ModernForumPosts.AsNoTracking().SingleAsync(item => item.LegacyPostId == 9002);
        Assert.Equal(otherMember.Id, otherPost.AuthorMemberId);
        Assert.Equal("Other Member", otherPost.AuthorDisplayName);
        Assert.Equal("<p>Other member reply</p>", otherPost.BodyHtml);
        var indexedThread = await dbContext.SearchDocuments.AsNoTracking().SingleAsync();
        Assert.Equal("forum-thread:8001", indexedThread.SourceKey);
        Assert.Equal("Delete test", indexedThread.Title);
        var article = await dbContext.ArticleSubmissions.AsNoTracking().SingleAsync();
        Assert.Equal(string.Empty, article.Body);
        Assert.Equal("Deleted", article.Status);
    }

    [Fact]
    public async Task CancelDeletionAsync_ClearsPendingRequest_AndPreventsPurge()
    {
        var account = await SeedAccountAsync("changed-mind@example.com", "Changed Mind");
        var requestedAt = new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc);
        await repository.RequestDeletionAsync(account.Id, requestedAt);

        var cancelled = await repository.CancelDeletionAsync(account.Id, requestedAt.AddDays(2));
        var purge = await repository.PurgeDeletedAccountsAsync(requestedAt, requestedAt.AddDays(30));

        Assert.NotNull(cancelled);
        Assert.Null(cancelled.DeletionRequestedAt);
        Assert.False(cancelled.IsSuspended);
        Assert.Equal("Changed Mind", cancelled.DisplayName);
        Assert.Null(cancelled.DeletionRecoveryDisplayName);
        Assert.Null(cancelled.DeletionRecoveryAvatarUrl);
        Assert.Equal(0, purge.PurgedCount);
        Assert.Equal(
            [MemberAccountDeletionPolicy.RequestedAuditAction, MemberAccountDeletionPolicy.CancelledAuditAction],
            await dbContext.MemberAccountDeletionAuditLogs
                .OrderBy(log => log.OccurredAt)
                .Select(log => log.Action)
                .ToListAsync());
    }

    [Fact]
    public async Task CancelDeletionAsync_AtThirtyDayCutoff_KeepsRequestPending()
    {
        var account = await SeedAccountAsync("too-late@example.com", "Too Late");
        var requestedAt = new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc);
        await repository.RequestDeletionAsync(account.Id, requestedAt);

        var result = await repository.CancelDeletionAsync(account.Id, requestedAt.AddDays(30));

        Assert.Equal(requestedAt, result!.DeletionRequestedAt);
        Assert.Equal(
            [MemberAccountDeletionPolicy.RequestedAuditAction],
            await dbContext.MemberAccountDeletionAuditLogs.Select(log => log.Action).ToListAsync());
    }

    [Fact]
    public async Task PurgeDeletedAccountsAsync_AfterThirtyDays_RemovesCredentialsAndLegacyLink()
    {
        var account = await SeedAccountAsync("purge-me@example.com", "Purge Me");
        account.PasswordHash = "hashed-secret";
        account.LinkedLegacyUserId = 4242;
        account.LastLoginAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await dbContext.SaveChangesAsync();
        await repository.AddExternalLoginAsync(account.Id, "Google", "purge-provider-key", account.Email);
        var requestedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        await repository.RequestDeletionAsync(account.Id, requestedAt);

        var early = await repository.PurgeDeletedAccountsAsync(
            requestedAt.AddTicks(-1),
            requestedAt.AddDays(30));
        var purgedAt = requestedAt.AddDays(30);
        var purged = await repository.PurgeDeletedAccountsAsync(requestedAt, purgedAt);

        Assert.Equal(0, early.PurgedCount);
        Assert.Equal(1, purged.PurgedCount);
        var reloaded = await dbContext.MemberAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal(MemberAccountDeletionPolicy.CreateDeletedEmail(account.Id), reloaded.Email);
        Assert.Null(reloaded.PasswordHash);
        Assert.Null(reloaded.LastLoginAt);
        Assert.Null(reloaded.LinkedLegacyUserId);
        Assert.Equal(purgedAt, reloaded.PersonalDataPurgedAt);
        Assert.Empty(await repository.ListExternalProvidersAsync(account.Id));
        Assert.Equal(
            [MemberAccountDeletionPolicy.RequestedAuditAction, MemberAccountDeletionPolicy.PurgedAuditAction],
            await dbContext.MemberAccountDeletionAuditLogs
                .OrderBy(log => log.OccurredAt)
                .Select(log => log.Action)
                .ToListAsync());
    }

    [Fact]
    public async Task PurgeDeletedAccountsAsync_OutboxesPromotedGalleryAndStage_AndRemovesPublicRows()
    {
        var first = await SeedAccountAsync("gallery-one@example.com", "Gallery One");
        var second = await SeedAccountAsync("gallery-two@example.com", "Gallery Two");
        await SeedPromotedPhotoAsync(first.Id, picId: 101, "/Brian_May/one.jpg", "/Brian_May/one-thumb.webp");
        await SeedPromotedPhotoAsync(second.Id, picId: 102, "/Brian_May/two.jpg", "/Brian_May/two-thumb.webp");
        await SeedPromotedStageAsync(first.Id, stageId: 201, "one.mp3");
        await SeedPromotedStageAsync(second.Id, stageId: 202, "two.mp3");
        var requestedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await repository.RequestDeletionAsync(first.Id, requestedAt, immediate: true);
        await repository.RequestDeletionAsync(second.Id, requestedAt, immediate: true);

        var purge = await repository.PurgeDeletedAccountsAsync(
            requestedAt.AddDays(-MemberAccountDeletionPolicy.RetentionDays),
            requestedAt);

        Assert.Equal(2, purge.PurgedCount);
        Assert.Equal(0, await CountLegacyRowsAsync("PIC_FILES_T"));
        Assert.Equal(0, await CountLegacyRowsAsync("Q_STAGE_T"));
        var queued = await repository.ListPendingDeletionBlobsAsync(50);
        Assert.Contains(queued, blob => blob is { Container: "brian-may", Path: "one.jpg" });
        Assert.Contains(queued, blob => blob is { Container: "brian-may", Path: "one-thumb.webp" });
        Assert.Contains(queued, blob => blob is { Container: "brian-may", Path: "two.jpg" });
        Assert.Contains(queued, blob => blob.Container == SongFileUrl.ContainerName && blob.Path == "one.mp3");
        Assert.Contains(queued, blob => blob.Container == SongFileUrl.ContainerName && blob.Path == "two.mp3");
        Assert.Equal(2, await dbContext.PhotoSubmissions.CountAsync(photo => photo.PromotedPicId == null && photo.Status == "Deleted"));
        Assert.Equal(2, await dbContext.FanPerformanceSubmissions.CountAsync(performance => performance.PromotedStageId == null && performance.Status == "Deleted"));
        var firstReloaded = await repository.FindByIdAsync(first.Id);
        var secondReloaded = await repository.FindByIdAsync(second.Id);
        Assert.NotNull(firstReloaded!.PersonalDataPurgedAt);
        Assert.NotNull(secondReloaded!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task PurgeDeletedAccountsAsync_WhenGalleryPathUnresolved_StillRemovesRowAndPurges()
    {
        var account = await SeedAccountAsync("unresolved-gallery@example.com", "Unresolved");
        await SeedPromotedPhotoAsync(account.Id, picId: 303, "not-a-legacy-path", null);
        var requestedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await repository.RequestDeletionAsync(account.Id, requestedAt, immediate: true);

        var purge = await repository.PurgeDeletedAccountsAsync(
            requestedAt.AddDays(-MemberAccountDeletionPolicy.RetentionDays),
            requestedAt);

        Assert.Equal(1, purge.PurgedCount);
        Assert.Equal(0, await CountLegacyRowsAsync("PIC_FILES_T"));
        var queued = await repository.ListPendingDeletionBlobsAsync(50);
        Assert.DoesNotContain(queued, blob => blob.Path == "not-a-legacy-path");
        Assert.NotNull((await repository.FindByIdAsync(account.Id))!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task ImmediateDeletion_PurgesNow_AndQueuesAvatarBlobsForRetry()
    {
        var account = await SeedAccountAsync("immediate-ef@example.com", "Immediate EF");
        await repository.UpdateAvatarUrlAsync(account.Id, $"members/{account.Id:N}/avatar.webp");
        var requestedAt = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

        await repository.RequestDeletionAsync(account.Id, requestedAt, immediate: true);
        var purge = await repository.PurgeDeletedAccountsAsync(
            requestedAt.AddDays(-MemberAccountDeletionPolicy.RetentionDays), requestedAt);

        Assert.Equal(1, purge.PurgedCount);
        var stored = await repository.FindByIdAsync(account.Id);
        Assert.NotNull(stored!.PersonalDataPurgedAt);
        Assert.True(stored.IsSuspended);
        var queued = await repository.ListPendingDeletionBlobsAsync(10);
        Assert.Equal(2, queued.Count);
        Assert.All(queued, blob => Assert.Equal(account.Id, blob.MemberAccountId));
        Assert.False((await repository.GetDeletionProgressAsync(account.Id))!.IsComplete);
        foreach (var blob in queued)
        {
            await repository.CompleteDeletionBlobAsync(blob.Id);
        }
        Assert.Empty(await repository.ListPendingDeletionBlobsAsync(10));
        Assert.True((await repository.GetDeletionProgressAsync(account.Id))!.IsComplete);
    }

    [Fact]
    public async Task LocalPasswordAccountOperations_ProjectUpdateAndRemoveCredential()
    {
        var account = await SeedAccountAsync("reviewer-ef@example.com", "EF Reviewer");
        account.PasswordHash = "initial-hash";
        await dbContext.SaveChangesAsync();

        var listed = Assert.Single(await repository.ListLocalPasswordAccountsAsync());
        Assert.Equal(account.Id, listed.Id);
        Assert.Equal(account.Email, listed.Email);

        var updated = await repository.UpdateLocalPasswordAccountAsync(
            account.Id,
            "updated-reviewer@example.com",
            "Updated EF Reviewer",
            "replacement-hash");

        Assert.NotNull(updated);
        Assert.Equal("UPDATED-REVIEWER@EXAMPLE.COM", updated.NormalizedEmail);
        Assert.Equal("replacement-hash", updated.PasswordHash);

        Assert.True(await repository.RemoveLocalPasswordAsync(account.Id));
        var retained = await repository.FindByIdAsync(account.Id);
        Assert.NotNull(retained);
        Assert.Null(retained.PasswordHash);
        Assert.Empty(await repository.ListLocalPasswordAccountsAsync());
    }

    [Fact]
    public async Task ListActiveMemberIdsAsync_ExcludesDeletionRequestedAndUnknownIds()
    {
        var active = await SeedAccountAsync("active@example.com", "Active");
        var leaving = await SeedAccountAsync("leaving@example.com", "Leaving");
        var unknownId = Guid.NewGuid();
        await repository.RequestDeletionAsync(
            leaving.Id,
            new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc));

        var result = await repository.ListActiveMemberIdsAsync([active.Id, leaving.Id, unknownId]);

        Assert.Equal([active.Id], result);
    }

    [Fact]
    public async Task ListActiveMemberIdsAsync_EmptyInput_DoesNotQuery()
    {
        var result = await repository.ListActiveMemberIdsAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RecordPasswordFailureAndSignIn_PersistsTheCounterAndOptionalHash()
    {
        var account = await SeedAccountAsync("lock-ef@example.com", "Lock EF");
        var started = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        await repository.RecordPasswordFailureAsync(account.Id, 4, started);
        await repository.RecordPasswordFailureAsync(Guid.NewGuid(), 9, started);

        var failed = await repository.FindByIdAsync(account.Id);
        Assert.Equal(4, failed!.PasswordFailureCount);
        Assert.Equal(started, failed.PasswordFailureWindowStartedAt);

        var loginAt = started.AddMinutes(2);
        await repository.RecordPasswordSignInAsync(account.Id, loginAt, "rewritten-hash");

        var signedIn = await repository.FindByIdAsync(account.Id);
        Assert.Equal(0, signedIn!.PasswordFailureCount);
        Assert.Null(signedIn.PasswordFailureWindowStartedAt);
        Assert.Equal(loginAt, signedIn.LastLoginAt);
        Assert.Equal("rewritten-hash", signedIn.PasswordHash);
    }

    private async Task<MemberAccount> SeedAccountAsync(string email, string displayName)
    {
        return await repository.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task SeedPromotedPhotoAsync(Guid memberId, int picId, string? url, string? thumbUrl)
    {
        dbContext.PhotoSubmissions.Add(new PhotoSubmissionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = memberId,
            Title = $"Photo {picId}",
            BlobPath = $"original/{picId}.jpg",
            WebOptimizedBlobPath = $"web/{picId}.webp",
            ThumbnailBlobPath = $"thumb/{picId}.webp",
            OriginalFileName = $"{picId}.jpg",
            MimeType = "image/jpeg",
            Status = PhotoSubmissionStatus.Approved,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            PromotedPicId = picId,
        });
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO PIC_FILES_T (PIC_ID, Url, Thumb_URL, Name, DISPLAY)
            VALUES ({0}, {1}, {2}, {3}, 1)
            """,
            picId,
            url ?? string.Empty,
            thumbUrl ?? string.Empty,
            $"Photo {picId}");
    }

    private async Task SeedPromotedStageAsync(Guid memberId, int stageId, string audioFileName)
    {
        dbContext.FanPerformanceSubmissions.Add(new FanPerformanceSubmissionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = memberId,
            Title = $"Stage {stageId}",
            CoveredSong = "Somebody to Love",
            PerformedBy = "Member",
            BlobPath = $"ugc/{stageId}.mp3",
            OriginalFileName = audioFileName,
            MimeType = "audio/mpeg",
            Status = FanPerformanceSubmissionStatus.Approved,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            RightsDeclaredAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            RightsDeclarationVersion = FanPerformanceSubmissionRights.DeclarationVersion,
            PromotedStageId = stageId,
        });
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Q_STAGE_T (Q_STAGE_ID, URL, TITLE, PERFORMED_BY, DESCRIPTION, DISPLAY)
            VALUES ({0}, {1}, {2}, {3}, {4}, 1)
            """,
            stageId,
            audioFileName,
            $"Stage {stageId}",
            "Member",
            "A cover");
    }

    private Task<int> CountLegacyRowsAsync(string tableName) =>
        tableName switch
        {
            "PIC_FILES_T" => dbContext.Database
                .SqlQueryRaw<int>("SELECT COUNT(1) AS Value FROM PIC_FILES_T")
                .SingleAsync(),
            "Q_STAGE_T" => dbContext.Database
                .SqlQueryRaw<int>("SELECT COUNT(1) AS Value FROM Q_STAGE_T")
                .SingleAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unknown legacy table."),
        };

    private void CreateLegacyPromotionTables()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE PIC_FILES_T
            (
                PIC_ID INTEGER PRIMARY KEY,
                Url TEXT NULL,
                Thumb_URL TEXT NULL,
                Name TEXT NULL,
                DISPLAY INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE Q_STAGE_T
            (
                Q_STAGE_ID INTEGER PRIMARY KEY,
                URL TEXT NULL,
                TITLE TEXT NULL,
                PERFORMED_BY TEXT NULL,
                DESCRIPTION TEXT NULL,
                DISPLAY INTEGER NOT NULL DEFAULT 1
            );
            """);
    }

    private void CreateModernForumTables()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumCategory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL,
                LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE ModernForumThread
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyTopicId INTEGER NOT NULL UNIQUE,
                LegacyForumId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL,
                StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL,
                LastActivityAt TEXT NULL,
                ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL,
                IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL,
                StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL,
                StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES ModernForumCategory (Id)
            );

            CREATE TABLE ModernForumPost
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyPostId INTEGER NOT NULL UNIQUE,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                AuthorMemberId TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread (Id)
            );
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}

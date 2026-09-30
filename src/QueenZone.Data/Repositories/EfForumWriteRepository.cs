using System.Collections.Frozen;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfForumWriteRepository(QueenZoneDbContext dbContext) : IForumWriteRepository
{
    private const int BodyHtmlMaxLength = 8000;
    internal const string TopicIdSequence = "ForumLegacyTopicIdSeq";
    internal const string PostIdSequence = "ForumLegacyPostIdSeq";

    private static readonly FrozenSet<string> KnownLegacyIdSequences =
        FrozenSet.ToFrozenSet([TopicIdSequence, PostIdSequence], StringComparer.Ordinal);

    public Task<ForumThreadCreateResult> CreateThreadAsync(NewForumThread thread, CancellationToken cancellationToken = default)
    {
        // Explicit transactions under EnableRetryOnFailure must run inside the execution strategy
        // so Azure SQL transient failures can retry the whole unit of work (see QueenZoneSqlServerOptions).
        // Join an ambient idempotency transaction when present so the receipt commits with the thread.
        return QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            IsolationLevel.ReadCommitted,
            async innerCt =>
        {
            var now = ToUtcDateTime(thread.CreatedAt);

            var category = await dbContext.ModernForumCategories
                .SingleOrDefaultAsync(item => item.LegacyForumId == thread.CategoryId && !item.IsSynthetic, innerCt);
            if (category is null)
            {
                throw new InvalidOperationException("Forum category not found.");
            }

            var topicId = await AllocateNextTopicIdAsync(innerCt);
            var postId = await AllocateNextPostIdAsync(innerCt);

            var forumThread = new ModernForumThreadEntity
            {
                LegacyTopicId = topicId,
                LegacyForumId = category.LegacyForumId,
                CategoryId = category.Id,
                Title = thread.Subject.Trim(),
                StartedByDisplayName = thread.AuthorDisplayName.Trim(),
                StartedAt = now,
                LastActivityAt = now,
                ReplyCount = 0,
                IsSticky = false,
                IsLegacyTopicStarter = true,
                LegacyDiscography = 0,
                StartedByUserValidated = true,
                StarterAttachCount = 0,
                ImportedAt = now,
                UpdatedAt = now,
            };

            var firstPost = new ModernForumPostEntity
            {
                LegacyPostId = postId,
                LegacyThreadTopicId = topicId,
                Thread = forumThread,
                LegacyForumId = category.LegacyForumId,
                AuthorMemberId = thread.AuthorMemberId,
                AuthorDisplayName = thread.AuthorDisplayName.Trim(),
                BodyHtml = TruncateBody(thread.Body),
                PostedAt = now,
                LegacyDiscography = 0,
                AuthorUserValidated = true,
                AttachCount = 0,
                EditCount = 0,
                ImportedAt = now,
                UpdatedAt = now,
            };

            dbContext.ModernForumThreads.Add(forumThread);
            dbContext.ModernForumPosts.Add(firstPost);

            if (thread.Poll is not null)
            {
                var poll = EfForumPollRepository.BuildPollEntity(
                    forumThread,
                    thread.Poll with { CreatedByMemberId = thread.AuthorMemberId },
                    thread.CreatedAt);
                dbContext.ForumPolls.Add(poll);
            }

            category.LegacyPostCount += 1;
            category.LastActivityAt = now;
            category.UpdatedAt = now;

            await dbContext.SaveChangesAsync(innerCt);

            await ApplyCreateThreadStatsAsync(
                forumThread.Id,
                topicId,
                category.Id,
                now,
                titleCountsForSitemap: !string.IsNullOrWhiteSpace(forumThread.Title),
                innerCt);
            return new ForumThreadCreateResult(topicId, postId);
        },
            cancellationToken);
    }

    public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default)
    {
        return QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            IsolationLevel.ReadCommitted,
            async innerCt =>
        {
            var now = ToUtcDateTime(post.CreatedAt);

            var thread = await dbContext.ModernForumThreads
                .Include(item => item.Category)
                .SingleOrDefaultAsync(item => item.LegacyTopicId == post.TopicId, innerCt);
            if (thread is null)
            {
                throw new InvalidOperationException("Forum thread not found.");
            }

            var postId = await AllocateNextPostIdAsync(innerCt);
            dbContext.ModernForumPosts.Add(new ModernForumPostEntity
            {
                LegacyPostId = postId,
                LegacyThreadTopicId = thread.LegacyTopicId,
                ThreadId = thread.Id,
                LegacyForumId = thread.LegacyForumId,
                AuthorMemberId = post.AuthorMemberId,
                AuthorDisplayName = post.AuthorDisplayName.Trim(),
                BodyHtml = TruncateBody(post.Body),
                PostedAt = now,
                LegacyDiscography = thread.LegacyDiscography,
                AuthorUserValidated = true,
                AttachCount = 0,
                EditCount = 0,
                ImportedAt = now,
                UpdatedAt = now,
            });

            thread.ReplyCount += 1;
            thread.LastActivityAt = now;
            thread.UpdatedAt = now;
            if (thread.Category is not null)
            {
                thread.Category.LegacyPostCount += 1;
                thread.Category.LastActivityAt = now;
                thread.Category.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(innerCt);
            await ApplyCreatePostStatsAsync(thread.Id, thread.LegacyTopicId, now, innerCt);
            return postId;
        },
            cancellationToken);
    }

    public async Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.LegacyPostId == postId)
            .Select(post => new
            {
                post.LegacyPostId,
                post.LegacyThreadTopicId,
                TopicSubject = post.Thread!.Title,
                post.BodyHtml,
                post.AuthorMemberId,
                post.AuthorDisplayName,
                post.PostedAt,
                post.EditedAt,
                post.EditCount,
                post.UpdatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var position = await dbContext.ModernForumPosts
            .AsNoTracking()
            .CountAsync(
                post => post.LegacyThreadTopicId == row.LegacyThreadTopicId && post.LegacyPostId <= postId,
                cancellationToken);

        return new ForumEditablePost(
            row.LegacyPostId,
            row.LegacyThreadTopicId,
            row.TopicSubject,
            row.BodyHtml,
            row.AuthorMemberId,
            row.AuthorDisplayName,
            LegacyDateTime.ToOffset(row.PostedAt),
            row.EditedAt.HasValue ? LegacyDateTime.ToOffset(row.EditedAt) : null,
            row.EditCount,
            Math.Max(1, position),
            LegacyDateTime.ToOffset(row.UpdatedAt));
    }

    public async Task<ForumPostUpdateResult> UpdatePostAsync(
        int postId,
        Guid editorMemberId,
        string sanitisedBody,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var post = await dbContext.ModernForumPosts
            .Include(item => item.Thread)
            .SingleOrDefaultAsync(item => item.LegacyPostId == postId, cancellationToken);
        if (post?.Thread is null)
        {
            return new ForumPostUpdateResult(ForumPostUpdateStatus.NotFound);
        }

        var postedAt = LegacyDateTime.ToOffset(post.PostedAt);
        var canEdit = ForumPostEditRules.CanEdit(
            post.AuthorMemberId,
            editorMemberId,
            isAdmin,
            postedAt,
            editWindowMinutes,
            DateTimeOffset.UtcNow);

        if (!canEdit)
        {
            if (!isAdmin
                && post.AuthorMemberId == editorMemberId
                && editWindowMinutes == 0)
            {
                return new ForumPostUpdateResult(ForumPostUpdateStatus.EditingDisabled, post.LegacyThreadTopicId, post.Thread.Title);
            }

            if (!isAdmin
                && post.AuthorMemberId == editorMemberId
                && editWindowMinutes > 0
                && DateTimeOffset.UtcNow > postedAt.AddMinutes(editWindowMinutes))
            {
                return new ForumPostUpdateResult(ForumPostUpdateStatus.EditWindowExpired, post.LegacyThreadTopicId, post.Thread.Title);
            }

            return new ForumPostUpdateResult(ForumPostUpdateStatus.Forbidden, post.LegacyThreadTopicId, post.Thread.Title);
        }

        if (expectedUpdatedAt is DateTimeOffset expected
            && LegacyDateTime.ToOffset(post.UpdatedAt) != expected)
        {
            return new ForumPostUpdateResult(
                ForumPostUpdateStatus.ConcurrencyConflict,
                post.LegacyThreadTopicId,
                post.Thread.Title);
        }

        var now = DateTime.UtcNow;
        if (expectedUpdatedAt is DateTimeOffset original)
        {
            dbContext.Entry(post).Property(item => item.UpdatedAt).OriginalValue = original.UtcDateTime;
        }

        post.BodyHtml = TruncateBody(sanitisedBody);
        post.EditedAt = now;
        post.EditCount += 1;
        post.UpdatedAt = now;
        try
        {
            await QueenZoneConcurrency.SaveChangesAsync(dbContext, cancellationToken);
        }
        catch (OptimisticConcurrencyException)
        {
            return new ForumPostUpdateResult(
                ForumPostUpdateStatus.ConcurrencyConflict,
                post.LegacyThreadTopicId,
                post.Thread.Title);
        }

        return new ForumPostUpdateResult(ForumPostUpdateStatus.Success, post.LegacyThreadTopicId, post.Thread.Title);
    }

    public async Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
        await dbContext.ModernForumThreads
            .AsNoTracking()
            .Where(thread => thread.LegacyTopicId == topicId)
            .Select(thread => new ForumWriteThread(
                thread.LegacyTopicId,
                thread.LegacyForumId,
                thread.Title,
                thread.StartedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(thread.StartedAt.Value, DateTimeKind.Utc)) : DateTimeOffset.MinValue,
                thread.LastActivityAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(thread.LastActivityAt.Value, DateTimeKind.Utc)) : DateTimeOffset.MinValue,
                thread.ReplyCount + 1,
                IsLocked: false))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> CountPostsByMemberSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var displayName = await GetDisplayNameAsync(memberId, cancellationToken);
        if (displayName is null)
        {
            return 0;
        }

        var sinceUtc = ToUtcDateTime(since);
        return await dbContext.ModernForumPosts
            .AsNoTracking()
            .CountAsync(
                post => post.AuthorDisplayName == displayName && post.PostedAt >= sinceUtc && !post.IsHidden,
                cancellationToken);
    }

    public async Task<int> CountApprovedPostsByMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var displayName = await GetDisplayNameAsync(memberId, cancellationToken);
        if (displayName is null)
        {
            return 0;
        }

        return await dbContext.ModernForumPosts
            .AsNoTracking()
            .CountAsync(post => post.AuthorDisplayName == displayName && !post.IsHidden, cancellationToken);
    }

    public async Task<ForumAuthorContentSummary> GetAuthorForumContentSummaryAsync(
        Guid? memberId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var name = displayName.Trim();
        var posts = AuthorPosts(memberId, name);
        var postCount = await posts.CountAsync(cancellationToken);
        var visiblePosts = await posts.AnyAsync(post => !post.IsHidden, cancellationToken);

        var threadIds = await StartedThreadIds(memberId, name, cancellationToken);
        var threadCount = threadIds.Count;
        var visibleThreads = threadIds.Count > 0 && await dbContext.ModernForumThreads
            .AnyAsync(thread => threadIds.Contains(thread.Id) && !thread.IsHidden, cancellationToken);
        return new ForumAuthorContentSummary(memberId, name, postCount, threadCount,
            postCount + threadCount > 0 && !visiblePosts && !visibleThreads);
    }

    public async Task<ForumAuthorContentSummary?> FindNoAccountForumAuthorAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var summary = await GetAuthorForumContentSummaryAsync(null, displayName, cancellationToken);
        return summary.PostCount == 0 && summary.ThreadCount == 0 ? null : summary;
    }

    public async Task HideAuthorForumContentAsync(
        Guid? memberId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var name = displayName.Trim();

        // Set-based UPDATEs by AuthorMemberId (plus unlinked exact-name posts). Do not
        // materialize starter ids and .Contains() them — that IN-list plan timed out on
        // the 1M+ post archive. Do not refresh all forum read stats here: that full
        // MERGE belongs after import/reconciliation, not on an admin POST.
        await StartedAuthorThreads(memberId, name)
            .Where(thread => !thread.IsHidden)
            .ExecuteUpdateAsync(setters => setters.SetProperty(thread => thread.IsHidden, true), cancellationToken);

        await AuthorPosts(memberId, name)
            .Where(post => !post.IsHidden)
            .ExecuteUpdateAsync(setters => setters.SetProperty(post => post.IsHidden, true), cancellationToken);
    }

    public async Task UnhideAuthorForumContentAsync(
        Guid? memberId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var name = displayName.Trim();

        await StartedAuthorThreads(memberId, name)
            .Where(thread => thread.IsHidden)
            .ExecuteUpdateAsync(setters => setters.SetProperty(thread => thread.IsHidden, false), cancellationToken);

        await AuthorPosts(memberId, name)
            .Where(post => post.IsHidden)
            .ExecuteUpdateAsync(setters => setters.SetProperty(post => post.IsHidden, false), cancellationToken);
    }

    // SQL Server's column collation (SQL_Latin1_General_CP1_CI_AS) is already case-insensitive
    // and imported display names are already trimmed, so on SQL Server we compare directly
    // against the raw column: wrapping it in Trim()/ToUpper() makes the predicate non-sargable,
    // forcing a full scan of the 1M+ row forum archive on every search miss. SQLite (used by the
    // in-memory test suite) defaults to a case-sensitive binary collation, so it still needs the
    // explicit ToUpper() for correctness — those tables are tiny in tests, so it costs nothing there.
    private bool UseSargableDisplayNameComparison => dbContext.Database.IsSqlServer();

    private IQueryable<ModernForumPostEntity> AuthorPosts(Guid? memberId, string displayName)
    {
        var name = displayName.Trim();
        if (UseSargableDisplayNameComparison)
        {
            return dbContext.ModernForumPosts.Where(post => memberId.HasValue
                ? post.AuthorMemberId == memberId.Value
                    || (post.AuthorMemberId == null && post.AuthorDisplayName == name)
                : post.AuthorMemberId == null && post.AuthorDisplayName == name);
        }

        var normalizedName = name.ToUpperInvariant();
        return dbContext.ModernForumPosts.Where(post => memberId.HasValue
            ? post.AuthorMemberId == memberId.Value
                || (post.AuthorMemberId == null && post.AuthorDisplayName.ToUpper() == normalizedName)
            : post.AuthorMemberId == null && post.AuthorDisplayName.ToUpper() == normalizedName);
    }

    // Threads this author started: their AuthorMemberId (or unlinked exact-name) post is
    // the starter. Composed as IQueryable so hide/unhide stay one set-based UPDATE.
    private IQueryable<ModernForumThreadEntity> StartedAuthorThreads(Guid? memberId, string displayName) =>
        dbContext.ModernForumThreads.Where(thread =>
            AuthorPosts(memberId, displayName).Any(post =>
                post.ThreadId == thread.Id
                && !dbContext.ModernForumPosts.Any(earlier =>
                    earlier.ThreadId == post.ThreadId && earlier.LegacyPostId < post.LegacyPostId)));

    // Materializes each stage instead of composing one deeply nested query: embedding these
    // correlated-subquery-heavy pieces via .Contains() against unmaterialized IQueryables (as
    // before) let SQL Server's optimizer fall back to a plan that re-scanned the 1M+ row forum
    // archive per candidate, timing out on the production database. Running each stage as its
    // own simple, sargable query and combining the (small) id lists in memory keeps every
    // individual query fast. Used by the summary read path only — hide/unhide must not use this.
    private async Task<IReadOnlyList<long>> StartedThreadIds(
        Guid? memberId, string displayName, CancellationToken cancellationToken)
    {
        var name = displayName.Trim();

        // Thread ids where the author's own post is that thread's starter post.
        var matchingStarterThreadIds = await AuthorPosts(memberId, displayName)
            .Where(post => !dbContext.ModernForumPosts.Any(earlier =>
                earlier.ThreadId == post.ThreadId && earlier.LegacyPostId < post.LegacyPostId))
            .Select(post => post.ThreadId)
            .ToListAsync(cancellationToken);

        // Narrow to threads whose recorded starter name matches (the much smaller
        // ModernForumThreads table, ~90K rows) before checking which of those threads have an
        // unlinked starter post, instead of scanning every unlinked post in the 1M+ row forum
        // archive to find starters and only filtering by name afterward.
        var candidateThreadIdsQuery = UseSargableDisplayNameComparison
            ? dbContext.ModernForumThreads.Where(thread => thread.StartedByDisplayName == name)
            : dbContext.ModernForumThreads.Where(thread => thread.StartedByDisplayName.ToUpper() == name.ToUpperInvariant());
        var candidateThreadIds = await candidateThreadIdsQuery.Select(thread => thread.Id).ToListAsync(cancellationToken);

        var unlinkedStarterThreadIds = candidateThreadIds.Count == 0
            ? []
            : await dbContext.ModernForumPosts
                .Where(post => post.AuthorMemberId == null && candidateThreadIds.Contains(post.ThreadId))
                .Where(post => !dbContext.ModernForumPosts.Any(earlier =>
                    earlier.ThreadId == post.ThreadId && earlier.LegacyPostId < post.LegacyPostId))
                .Select(post => post.ThreadId)
                .ToListAsync(cancellationToken);

        return matchingStarterThreadIds.Union(unlinkedStarterThreadIds).ToList();
    }

    private async Task<int> AllocateNextTopicIdAsync(CancellationToken cancellationToken) =>
        await AllocateNextLegacyIdAsync(
            TopicIdSequence,
            static async (db, ct) =>
                (await db.ModernForumThreads.MaxAsync(thread => (int?)thread.LegacyTopicId, ct) ?? 0) + 1,
            cancellationToken);

    private async Task<int> AllocateNextPostIdAsync(CancellationToken cancellationToken) =>
        await AllocateNextLegacyIdAsync(
            PostIdSequence,
            static async (db, ct) =>
                (await db.ModernForumPosts.MaxAsync(post => (int?)post.LegacyPostId, ct) ?? 0) + 1,
            cancellationToken);

    internal async Task<int> AllocateNextLegacyIdAsync(
        string sequenceName,
        Func<QueenZoneDbContext, CancellationToken, Task<int>> fallback,
        CancellationToken cancellationToken)
    {
        if (!KnownLegacyIdSequences.Contains(sequenceName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceName),
                sequenceName,
                "Unknown forum legacy id sequence.");
        }

        if (!IsSqlServer())
        {
            return await fallback(dbContext, cancellationToken);
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT NEXT VALUE FOR dbo.[{sequenceName}]";
        if (dbContext.Database.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return await fallback(dbContext, cancellationToken);
        }

        return Convert.ToInt32(result);
    }

    private async Task<string?> GetDisplayNameAsync(Guid memberId, CancellationToken cancellationToken) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => member.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task ApplyCreateThreadStatsAsync(
        long threadId,
        int legacyTopicId,
        int categoryId,
        DateTime updatedAt,
        bool titleCountsForSitemap,
        CancellationToken cancellationToken)
    {
        if (!IsSqlServer())
        {
            return;
        }

        var sitemapDelta = titleCountsForSitemap ? 1 : 0;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            IF OBJECT_ID(N'dbo.ModernForumThreadReadStats', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.ModernForumThreadReadStats (ThreadId, LegacyTopicId, PostCount, UpdatedAt)
                VALUES ({threadId}, {legacyTopicId}, 1, {updatedAt});
            END;

            IF OBJECT_ID(N'dbo.ModernForumCategoryReadStats', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ModernForumCategoryReadStats
                SET TotalThreads = TotalThreads + 1,
                    ValidatedDisplayThreads = ValidatedDisplayThreads + 1,
                    UpdatedAt = {updatedAt}
                WHERE CategoryId = {categoryId};
            END;

            IF OBJECT_ID(N'dbo.ModernForumArchiveReadStats', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ModernForumArchiveReadStats
                SET TotalThreads = TotalThreads + 1,
                    SitemapTopicCount = SitemapTopicCount + {sitemapDelta},
                    UpdatedAt = {updatedAt}
                WHERE Id = 1;
            END;
            """, cancellationToken);
    }

    private async Task ApplyCreatePostStatsAsync(
        long threadId,
        int legacyTopicId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        if (!IsSqlServer())
        {
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            IF OBJECT_ID(N'dbo.ModernForumThreadReadStats', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ModernForumThreadReadStats
                SET PostCount = PostCount + 1,
                    UpdatedAt = {updatedAt}
                WHERE ThreadId = {threadId};

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.ModernForumThreadReadStats (ThreadId, LegacyTopicId, PostCount, UpdatedAt)
                    SELECT
                        {threadId},
                        {legacyTopicId},
                        CONVERT(int, COUNT_BIG(*)),
                        {updatedAt}
                    FROM dbo.ModernForumPost
                    WHERE ThreadId = {threadId}
                      AND IsHidden = 0;
                END;
            END;
            """, cancellationToken);
    }

    private bool IsSqlServer() =>
        string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal);

    private static DateTime ToUtcDateTime(DateTimeOffset value) =>
        value.UtcDateTime;

    public async Task<int> EnsureCategoryAsync(
        string slug,
        string name,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindMatchingCategoryAsync(slug, name, cancellationToken);
        if (existing is not null)
        {
            return existing.LegacyForumId;
        }

        var now = DateTime.UtcNow;
        var categories = await dbContext.ModernForumCategories
            .ToListAsync(cancellationToken);
        var nextLegacyId = categories.Select(category => category.LegacyForumId).DefaultIfEmpty(0).Max() + 1;
        if (nextLegacyId < 2)
        {
            nextLegacyId = 2;
        }

        var entity = new ModernForumCategoryEntity
        {
            LegacyForumId = nextLegacyId,
            Name = name.Trim(),
            Description = "Discussion of published QueenZone news articles.",
            SortOrder = categories.Select(category => category.SortOrder).DefaultIfEmpty(0).Max() + 10,
            LegacyPostCount = 0,
            IsSynthetic = false,
            ImportedAt = now,
            UpdatedAt = now,
        };

        dbContext.ModernForumCategories.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity.LegacyForumId;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            var retry = await FindMatchingCategoryAsync(slug, name, cancellationToken);
            return retry?.LegacyForumId
                ?? throw new InvalidOperationException("News forum category could not be created.");
        }
    }

    private async Task<ModernForumCategoryEntity?> FindMatchingCategoryAsync(
        string slug,
        string name,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.ModernForumCategories
            .AsNoTracking()
            .Where(category => !category.IsSynthetic)
            .ToListAsync(cancellationToken);
        return NewsForumDiscussion.FindExistingCategory(
            categories,
            category => category.Name,
            slug,
            name);
    }

    private static string TruncateBody(string body) =>
        body.Length <= BodyHtmlMaxLength ? body : body[..BodyHtmlMaxLength];
}

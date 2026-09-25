using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfPrivateMessageRepository(QueenZoneDbContext dbContext) : IPrivateMessageRepository
{
    private const int MaxUniqueConflictRetries = 3;

    public Task<PrivateInboxPage> GetInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default) =>
        GetInboxCoreAsync(memberId, page, pageSize, isArchived: false, cancellationToken);

    public Task<PrivateInboxPage> GetArchivedInboxAsync(
        Guid memberId,
        int page = 1,
        int pageSize = PrivateMessageLimits.InboxPageSize,
        CancellationToken cancellationToken = default) =>
        GetInboxCoreAsync(memberId, page, pageSize, isArchived: true, cancellationToken);

    public Task<bool> ArchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        SetArchivedAsync(conversationId, memberId, isArchived: true, cancellationToken);

    public Task<bool> UnarchiveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        SetArchivedAsync(conversationId, memberId, isArchived: false, cancellationToken);

    private async Task<bool> SetArchivedAsync(
        Guid conversationId,
        Guid memberId,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        // Tracked query + mutate + save, matching UnarchiveAsync below: ExecuteUpdateAsync bypasses
        // the change tracker and would leave any already-tracked participant instance stale within
        // the same DbContext (e.g. a subsequent reply's own unarchive-on-send within one request).
        var participant = await dbContext.PrivateConversationParticipants
            .SingleOrDefaultAsync(
                p => p.ConversationId == conversationId && p.MemberId == memberId,
                cancellationToken);
        if (participant is null)
        {
            return false;
        }

        participant.IsArchived = isArchived;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<PrivateInboxPage> GetInboxCoreAsync(
        Guid memberId,
        int page,
        int pageSize,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, PrivateMessageLimits.MaxInboxPageSize);

        if (IsSqliteDatabase())
        {
            return await GetInboxSqliteAsync(memberId, page, pageSize, isArchived, cancellationToken);
        }

        var totalCount = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .CountAsync(
                p => p.MemberId == memberId && p.IsArchived == isArchived && !p.IsRemoved,
                cancellationToken);
        var totalPages = totalCount <= 0 ? 1 : (totalCount + pageSize - 1) / pageSize;
        page = Math.Min(page, totalPages);

        // Unread count is folded into this projection (a correlated scalar subquery SQL Server
        // executes as an APPLY within the same SELECT) so the page and its unread counts come back
        // in one round trip instead of two.
        var pageRows = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .Where(p => p.MemberId == memberId && p.IsArchived == isArchived && !p.IsRemoved)
            .OrderByDescending(p => p.Conversation!.LastMessageSortKey)
            .ThenByDescending(p => p.ConversationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new InboxPageRowWithUnread(
                p.ConversationId,
                p.Conversation!.LastMessagePreview,
                p.Conversation.LastMessageAt,
                p.Conversation.MemberLowId == memberId
                    ? (p.Conversation.MemberHigh != null ? p.Conversation.MemberHigh.DisplayName : string.Empty)
                    : (p.Conversation.MemberLow != null ? p.Conversation.MemberLow.DisplayName : string.Empty),
                p.Conversation.MemberLowId == memberId
                    ? p.Conversation.MemberHighId
                    : p.Conversation.MemberLowId,
                dbContext.PrivateMessages.Count(m =>
                    m.ConversationId == p.ConversationId
                    && m.SenderMemberId != memberId
                    && (p.LastReadSortKey == null || m.SortKey > p.LastReadSortKey))))
            .ToListAsync(cancellationToken);

        return new PrivateInboxPage(MapInboxWithUnread(pageRows), totalCount, page, pageSize);
    }

    public Task<int> CountUnreadConversationsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        IsSqliteDatabase()
            ? CountUnreadConversationsSqliteAsync(memberId, cancellationToken)
            : CountUnreadConversationsSqlAsync(memberId, cancellationToken);

    public async Task<PrivateConversationDetail?> GetConversationAsync(
        Guid conversationId,
        Guid memberId,
        int? page = null,
        int pageSize = PrivateMessageLimits.ConversationPageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, PrivateMessageLimits.MaxConversationPageSize);

        // Participant and conversation (with both member navigations) in one round trip, via the
        // participant's Conversation navigation, instead of two separate SingleOrDefaultAsync calls.
        var participant = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .Include(p => p.Conversation!.MemberLow)
            .Include(p => p.Conversation!.MemberHigh)
            .SingleOrDefaultAsync(
                p => p.ConversationId == conversationId && p.MemberId == memberId,
                cancellationToken);
        if (participant?.Conversation is null)
        {
            return null;
        }

        var conversation = participant.Conversation;

        var otherId = conversation.MemberLowId == memberId
            ? conversation.MemberHighId
            : conversation.MemberLowId;
        var otherName = conversation.MemberLowId == memberId
            ? conversation.MemberHigh?.DisplayName
            : conversation.MemberLow?.DisplayName;

        var totalCount = await dbContext.PrivateMessages
            .AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId, cancellationToken);
        var totalPages = totalCount <= 0
            ? 1
            : (totalCount + pageSize - 1) / pageSize;

        // Default and explicit last page use a keyset "latest window" (newest pageSize messages).
        // That avoids count/offset TOCTOU races and always surfaces the tip; when the final
        // offset page would be a short remainder, this window can overlap the previous page.
        var effectivePage = page is null or < 1
            ? totalPages
            : Math.Min(page.Value, totalPages);
        var useLatestWindow = page is null or < 1 || effectivePage >= totalPages;

        List<ConversationMessageRow> messageRows;
        if (useLatestWindow)
        {
            var latestRows = await dbContext.PrivateMessages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SortKey)
                .Take(pageSize)
                .Select(m => new ConversationMessageRow(
                    m.Id,
                    m.SenderMemberId,
                    m.Sender != null ? m.Sender.DisplayName : string.Empty,
                    m.Body,
                    m.CreatedAt,
                    m.SortKey))
                .ToListAsync(cancellationToken);
            messageRows = latestRows
                .OrderBy(m => m.SortKey)
                .ToList();
            effectivePage = totalPages;
        }
        else
        {
            messageRows = await dbContext.PrivateMessages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SortKey)
                .Skip((effectivePage - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new ConversationMessageRow(
                    m.Id,
                    m.SenderMemberId,
                    m.Sender != null ? m.Sender.DisplayName : string.Empty,
                    m.Body,
                    m.CreatedAt,
                    m.SortKey))
                .ToListAsync(cancellationToken);
        }

        var reportedIds = await LoadReportedMessageIdsAsync(
            conversationId,
            memberId,
            messageRows.Select(m => m.Id),
            cancellationToken);
        IReadOnlyList<PrivateMessageItem> items = messageRows
            .Select(m => new PrivateMessageItem(
                m.Id,
                m.SenderMemberId,
                string.IsNullOrWhiteSpace(m.SenderName) ? "Unknown member" : m.SenderName,
                m.Body,
                m.CreatedAt,
                m.SenderMemberId == memberId,
                m.SortKey,
                reportedIds.Contains(m.Id)))
            .ToList();

        return new PrivateConversationDetail(
            conversationId,
            otherId,
            string.IsNullOrWhiteSpace(otherName) ? "Unknown member" : otherName,
            items,
            totalCount,
            effectivePage,
            pageSize);
    }

    public Task<bool> IsParticipantAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.MemberId == memberId, cancellationToken);

    public async Task MarkConversationReadAsync(
        Guid conversationId,
        Guid memberId,
        long lastReadSortKey,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        // Conditional update so concurrent opens cannot move the cursor backwards.
        await dbContext.PrivateConversationParticipants
            .Where(p =>
                p.ConversationId == conversationId
                && p.MemberId == memberId
                && (p.LastReadSortKey == null || p.LastReadSortKey < lastReadSortKey))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.LastReadSortKey, lastReadSortKey)
                    .SetProperty(p => p.LastReadAt, readAt),
                cancellationToken);
    }

    public async Task<PrivateMessageSendResult> SendNewOrExistingAsync(
        Guid senderMemberId,
        Guid recipientMemberId,
        string body,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        body = NormalizeBody(body);

        var recipientExists = await dbContext.MemberAccounts
            .AsNoTracking()
            .AnyAsync(m => m.Id == recipientMemberId, cancellationToken);
        if (!recipientExists)
        {
            return new PrivateMessageSendResult(false, null, "Recipient was not found.");
        }

        var preview = TruncatePreview(body);
        for (var attempt = 1; attempt <= MaxUniqueConflictRetries; attempt++)
        {
            try
            {
                var conversationId = await SendOnceAsync(
                    senderMemberId,
                    recipientMemberId,
                    body,
                    preview,
                    sentAt,
                    cancellationToken);
                return new PrivateMessageSendResult(true, conversationId, null);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxUniqueConflictRetries)
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        return new PrivateMessageSendResult(false, null, "Unable to start the conversation. Please try again.");
    }

    public async Task<PrivateMessageSendResult> ReplyAsync(
        Guid conversationId,
        Guid senderMemberId,
        string body,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        body = NormalizeBody(body);

        var isParticipant = await IsParticipantAsync(conversationId, senderMemberId, cancellationToken);
        if (!isParticipant)
        {
            return new PrivateMessageSendResult(
                false,
                null,
                "You are not a participant in this conversation.");
        }

        var preview = TruncatePreview(body);
        // Explicit transactions must run through the configured execution strategy so Azure SQL
        // can retry the entire unit of work rather than rejecting a user-started transaction.
        // Join an ambient idempotency transaction when present so the receipt commits with the message.
        return await QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            async innerCt =>
            {
                var conversation = await LockConversationForWriteAsync(conversationId, innerCt);
                if (conversation is null)
                {
                    return new PrivateMessageSendResult(false, null, "Conversation not found.");
                }

                var message = new PrivateMessageEntity
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    SenderMemberId = senderMemberId,
                    Body = body,
                    CreatedAt = sentAt,
                };
                await PrepareMessageSortKeyAsync(message, innerCt);
                dbContext.PrivateMessages.Add(message);

                await ReactivateParticipantAsync(conversationId, conversation.MemberLowId, innerCt);
                await ReactivateParticipantAsync(conversationId, conversation.MemberHighId, innerCt);
                await dbContext.SaveChangesAsync(innerCt);

                await UpdateConversationSummaryForInsertedMessageAsync(
                    conversationId,
                    sentAt,
                    preview,
                    senderMemberId,
                    message.SortKey,
                    innerCt);

                return new PrivateMessageSendResult(true, conversationId, null);
            },
            cancellationToken);
    }

    private async Task<Guid> SendOnceAsync(
        Guid senderMemberId,
        Guid recipientMemberId,
        string body,
        string preview,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        var (low, high) = OrderPair(senderMemberId, recipientMemberId);
        // See ReplyAsync: retry the complete transaction when SQL Server reports a transient fault.
        // Join an ambient idempotency transaction when present so the receipt commits with the message.
        return await QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            async innerCt =>
            {
                var conversation = await LockConversationForWriteAsync(low, high, innerCt);

                if (conversation is null)
                {
                    conversation = new PrivateConversationEntity
                    {
                        Id = Guid.NewGuid(),
                        MemberLowId = low,
                        MemberHighId = high,
                        CreatedAt = sentAt,
                        LastMessageAt = sentAt,
                        LastMessageSortKey = 0,
                        LastMessagePreview = preview,
                        LastMessageSenderId = senderMemberId,
                    };
                    dbContext.PrivateConversations.Add(conversation);
                    dbContext.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
                    {
                        ConversationId = conversation.Id,
                        MemberId = senderMemberId,
                        LastReadAt = sentAt,
                        LastReadSortKey = null,
                        IsArchived = false,
                        IsRemoved = false,
                    });
                    dbContext.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
                    {
                        ConversationId = conversation.Id,
                        MemberId = recipientMemberId,
                        LastReadAt = null,
                        LastReadSortKey = null,
                        IsArchived = false,
                        IsRemoved = false,
                    });

                    var firstMessage = new PrivateMessageEntity
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = conversation.Id,
                        SenderMemberId = senderMemberId,
                        Body = body,
                        CreatedAt = sentAt,
                    };
                    await PrepareMessageSortKeyAsync(firstMessage, innerCt);
                    dbContext.PrivateMessages.Add(firstMessage);
                    await dbContext.SaveChangesAsync(innerCt);

                    // Sender has seen their own first message; align SortKey cursor with LastReadAt.
                    // Tip SortKey is only known after IDENTITY (SQL Server) or in-process assignment (SQLite).
                    conversation.LastMessageSortKey = firstMessage.SortKey;
                    var senderParticipant = await dbContext.PrivateConversationParticipants
                        .SingleAsync(
                            p => p.ConversationId == conversation.Id && p.MemberId == senderMemberId,
                            innerCt);
                    senderParticipant.LastReadSortKey = firstMessage.SortKey;
                    senderParticipant.LastReadAt = sentAt;
                    await dbContext.SaveChangesAsync(innerCt);

                    return conversation.Id;
                }

                var conversationId = conversation.Id;

                await EnsureParticipantAsync(conversationId, senderMemberId, innerCt);
                await EnsureParticipantAsync(conversationId, recipientMemberId, innerCt);
                await ReactivateParticipantAsync(conversationId, senderMemberId, innerCt);
                await ReactivateParticipantAsync(conversationId, recipientMemberId, innerCt);

                var message = new PrivateMessageEntity
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    SenderMemberId = senderMemberId,
                    Body = body,
                    CreatedAt = sentAt,
                };
                await PrepareMessageSortKeyAsync(message, innerCt);
                dbContext.PrivateMessages.Add(message);
                await dbContext.SaveChangesAsync(innerCt);

                await UpdateConversationSummaryForInsertedMessageAsync(
                    conversationId,
                    sentAt,
                    preview,
                    senderMemberId,
                    message.SortKey,
                    innerCt);

                return conversationId;
            },
            cancellationToken);
    }

    private async Task<PrivateConversationEntity?> LockConversationForWriteAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        // The no-op update acquires a write lock held until commit. Every message for an existing
        // conversation therefore receives its identity SortKey only after the previous writer has
        // committed, making SortKey a safe visible-order/read cursor within that conversation.
        var affected = await dbContext.PrivateConversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.LastMessageAt, c => c.LastMessageAt),
                cancellationToken);
        if (affected == 0)
        {
            return null;
        }

        return await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == conversationId, cancellationToken);
    }

    private async Task<PrivateConversationEntity?> LockConversationForWriteAsync(
        Guid memberLowId,
        Guid memberHighId,
        CancellationToken cancellationToken)
    {
        // Serialize the existing-conversation path before allocating the next message SortKey.
        // A concurrent first-send can still see no row; the unique-pair retry handles that race.
        var affected = await dbContext.PrivateConversations
            .Where(c => c.MemberLowId == memberLowId && c.MemberHighId == memberHighId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.LastMessageAt, c => c.LastMessageAt),
                cancellationToken);
        if (affected == 0)
        {
            return null;
        }

        return await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(
                c => c.MemberLowId == memberLowId && c.MemberHighId == memberHighId,
                cancellationToken);
    }

    private async Task PrepareMessageSortKeyAsync(
        PrivateMessageEntity message,
        CancellationToken cancellationToken)
    {
        // SQL Server uses IDENTITY via ValueGeneratedOnAdd. SQLite EnsureCreated does not
        // auto-generate non-PK integers, so assign a monotonic SortKey in-process.
        if (!IsSqliteDatabase())
        {
            return;
        }

        var max = await dbContext.PrivateMessages
            .AsNoTracking()
            .Select(m => (long?)m.SortKey)
            .MaxAsync(cancellationToken) ?? 0L;
        message.SortKey = max + 1;
    }

    private async Task UpdateConversationSummaryForInsertedMessageAsync(
        Guid conversationId,
        DateTimeOffset sentAt,
        string preview,
        Guid senderMemberId,
        long sortKey,
        CancellationToken cancellationToken)
    {
        // Callers hold the conversation write lock, so this insert is the tip by SortKey.
        // Always refresh preview/sender/SortKey tip. Keep LastMessageAt monotonic (max of existing
        // and sentAt) for display; inbox ranking uses LastMessageSortKey instead.
        var conversation = await dbContext.PrivateConversations
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        if (sentAt > conversation.LastMessageAt)
        {
            conversation.LastMessageAt = sentAt;
        }

        conversation.LastMessageSortKey = sortKey;
        conversation.LastMessagePreview = preview;
        conversation.LastMessageSenderId = senderMemberId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> CountUnreadConversationsSqlAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        // Join + Distinct/Count instead of a per-row correlated Any() subquery, matching the
        // batched aggregate join CountUnreadForPageSqlAsync already uses.
        return await (
            from m in dbContext.PrivateMessages.AsNoTracking()
            join p in dbContext.PrivateConversationParticipants.AsNoTracking()
                on m.ConversationId equals p.ConversationId
            where p.MemberId == memberId
                && !p.IsArchived
                && !p.IsRemoved
                && m.SenderMemberId != memberId
                && (p.LastReadSortKey == null || m.SortKey > p.LastReadSortKey)
            select m.ConversationId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private async Task<int> CountUnreadConversationsSqliteAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var pageRows = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .Where(p => p.MemberId == memberId && !p.IsArchived && !p.IsRemoved)
            .Select(p => new InboxPageRow(
                p.ConversationId,
                p.LastReadSortKey,
                string.Empty,
                default,
                string.Empty,
                Guid.Empty))
            .ToListAsync(cancellationToken);
        if (pageRows.Count == 0)
        {
            return 0;
        }

        var unread = await CountUnreadForPageSqlAsync(memberId, pageRows, cancellationToken);
        return unread.Count(pair => pair.Value > 0);
    }

    private async Task<PrivateInboxPage> GetInboxSqliteAsync(
        Guid memberId,
        int page,
        int pageSize,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .Where(p => p.MemberId == memberId && p.IsArchived == isArchived && !p.IsRemoved)
            .Select(p => new
            {
                p.ConversationId,
                p.LastReadSortKey,
                Preview = p.Conversation!.LastMessagePreview,
                LastMessageAt = p.Conversation.LastMessageAt,
                LastMessageSortKey = p.Conversation.LastMessageSortKey,
                OtherDisplayName = p.Conversation.MemberLowId == memberId
                    ? (p.Conversation.MemberHigh != null ? p.Conversation.MemberHigh.DisplayName : string.Empty)
                    : (p.Conversation.MemberLow != null ? p.Conversation.MemberLow.DisplayName : string.Empty),
                OtherId = p.Conversation.MemberLowId == memberId
                    ? p.Conversation.MemberHighId
                    : p.Conversation.MemberLowId,
            })
            .ToListAsync(cancellationToken);

        var totalCount = rows.Count;
        var totalPages = totalCount <= 0 ? 1 : (totalCount + pageSize - 1) / pageSize;
        page = Math.Min(page, totalPages);

        var pageRows = rows
            .OrderByDescending(r => r.LastMessageSortKey)
            .ThenByDescending(r => r.ConversationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new InboxPageRow(
                r.ConversationId,
                r.LastReadSortKey,
                r.Preview,
                r.LastMessageAt,
                r.OtherDisplayName,
                r.OtherId))
            .ToList();

        var unreadByConversation = await CountUnreadForPageSqlAsync(memberId, pageRows, cancellationToken);
        return new PrivateInboxPage(MapInbox(pageRows, unreadByConversation), totalCount, page, pageSize);
    }

    private async Task<Dictionary<Guid, int>> CountUnreadForPageSqlAsync(
        Guid memberId,
        IReadOnlyList<InboxPageRow> pageRows,
        CancellationToken cancellationToken)
    {
        if (pageRows.Count == 0)
        {
            return [];
        }

        var conversationIds = pageRows.Select(r => r.ConversationId).ToList();

        // SQL filtered aggregate: only unread (SortKey > cursor) messages, grouped by conversation.
        var aggregated = await (
            from m in dbContext.PrivateMessages.AsNoTracking()
            join p in dbContext.PrivateConversationParticipants.AsNoTracking()
                on m.ConversationId equals p.ConversationId
            where conversationIds.Contains(m.ConversationId)
                && p.MemberId == memberId
                && m.SenderMemberId != memberId
                && (p.LastReadSortKey == null || m.SortKey > p.LastReadSortKey)
            group m by m.ConversationId
            into g
            select new { ConversationId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var result = conversationIds.ToDictionary(id => id, _ => 0);
        foreach (var row in aggregated)
        {
            result[row.ConversationId] = row.Count;
        }

        return result;
    }

    private static IReadOnlyList<PrivateConversationListItem> MapInbox(
        IReadOnlyList<InboxPageRow> pageRows,
        IReadOnlyDictionary<Guid, int> unreadByConversation) =>
        pageRows
            .Select(r =>
            {
                var unread = unreadByConversation.GetValueOrDefault(r.ConversationId);
                return new PrivateConversationListItem(
                    r.ConversationId,
                    r.OtherId,
                    string.IsNullOrWhiteSpace(r.OtherDisplayName) ? "Unknown member" : r.OtherDisplayName,
                    r.Preview,
                    r.LastMessageAt,
                    unread > 0,
                    unread);
            })
            .ToList();

    private static IReadOnlyList<PrivateConversationListItem> MapInboxWithUnread(
        IReadOnlyList<InboxPageRowWithUnread> pageRows) =>
        pageRows
            .Select(r => new PrivateConversationListItem(
                r.ConversationId,
                r.OtherId,
                string.IsNullOrWhiteSpace(r.OtherDisplayName) ? "Unknown member" : r.OtherDisplayName,
                r.Preview,
                r.LastMessageAt,
                r.UnreadCount > 0,
                r.UnreadCount))
            .ToList();

    private async Task EnsureParticipantAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.PrivateConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.MemberId == memberId, cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
        {
            ConversationId = conversationId,
            MemberId = memberId,
            LastReadAt = null,
            LastReadSortKey = null,
            IsArchived = false,
            IsRemoved = false,
        });
    }

    private async Task ReactivateParticipantAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        // New activity restores visibility rather than silently dropping the message on an
        // archived or removed conversation.
        var participant = await dbContext.PrivateConversationParticipants
            .SingleOrDefaultAsync(
                p => p.ConversationId == conversationId && p.MemberId == memberId,
                cancellationToken);
        if (participant is not null)
        {
            participant.IsArchived = false;
            participant.IsRemoved = false;
        }
    }

    public async Task<bool> RemoveConversationAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        // Tracked query + mutate + save (not ExecuteUpdateAsync) so this stays consistent with
        // ReactivateParticipantAsync within the same DbContext: bypassing the change tracker would
        // leave an already-tracked participant instance stale for a later reply in the same request.
        var participant = await dbContext.PrivateConversationParticipants
            .SingleOrDefaultAsync(
                p => p.ConversationId == conversationId && p.MemberId == memberId,
                cancellationToken);
        if (participant is null)
        {
            return false;
        }

        participant.IsRemoved = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Guid?> GetOtherParticipantIdAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.PrivateConversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId
                && (c.MemberLowId == memberId || c.MemberHighId == memberId))
            .Select(c => new { c.MemberLowId, c.MemberHighId })
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        return conversation.MemberLowId == memberId
            ? conversation.MemberHighId
            : conversation.MemberLowId;
    }

    public Task<bool> HasConversationBetweenAsync(
        Guid memberA,
        Guid memberB,
        CancellationToken cancellationToken = default)
    {
        var (low, high) = OrderPair(memberA, memberB);
        return dbContext.PrivateConversations
            .AsNoTracking()
            .AnyAsync(c => c.MemberLowId == low && c.MemberHighId == high, cancellationToken);
    }

    public Task<int> CountMessagesBySenderSinceAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        IsSqliteDatabase()
            ? CountMessagesBySenderSinceInMemoryAsync(senderMemberId, sinceUtc, cancellationToken)
            : dbContext.PrivateMessages
                .AsNoTracking()
                .CountAsync(
                    message => message.SenderMemberId == senderMemberId && message.CreatedAt >= sinceUtc,
                    cancellationToken);

    public Task<int> CountIdenticalMessagesBySenderSinceAsync(
        Guid senderMemberId,
        string body,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        IsSqliteDatabase()
            ? CountIdenticalMessagesBySenderSinceInMemoryAsync(senderMemberId, body, sinceUtc, cancellationToken)
            : dbContext.PrivateMessages
                .AsNoTracking()
                .CountAsync(
                    message => message.SenderMemberId == senderMemberId
                        && message.CreatedAt >= sinceUtc
                        && message.Body == body,
                    cancellationToken);

    public Task<int> CountDistinctNewRecipientsSinceAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        IsSqliteDatabase()
            ? CountDistinctNewRecipientsSinceInMemoryAsync(senderMemberId, sinceUtc, cancellationToken)
            : dbContext.PrivateMessages
                .AsNoTracking()
                .Where(message => message.SenderMemberId == senderMemberId && message.CreatedAt >= sinceUtc)
                .Join(
                    dbContext.PrivateConversations.AsNoTracking().Where(c => c.CreatedAt >= sinceUtc),
                    message => message.ConversationId,
                    conversation => conversation.Id,
                    (message, conversation) => message.ConversationId)
                .Distinct()
                .CountAsync(cancellationToken);

    private async Task<HashSet<Guid>> LoadReportedMessageIdsAsync(
        Guid conversationId,
        Guid reporterMemberId,
        IEnumerable<Guid> messageIds,
        CancellationToken cancellationToken)
    {
        var ids = messageIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var reported = await dbContext.PrivateMessageReports
            .AsNoTracking()
            .Where(r =>
                r.ConversationId == conversationId
                && r.ReporterMemberId == reporterMemberId
                && ids.Contains(r.MessageId))
            .Select(r => r.MessageId)
            .ToListAsync(cancellationToken);
        return reported.ToHashSet();
    }

    // SQLite fallback (also exercised in tests): the provider cannot translate DateTimeOffset
    // range comparisons, so materialise the sender's messages then filter by CreatedAt in
    // memory. Production always targets SQL Server; the efficient path above is covered by
    // tests/QueenZone.SqlServerTests (see docs/architecture/testing-policy.md).
    private async Task<int> CountMessagesBySenderSinceInMemoryAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var createdAtValues = await dbContext.PrivateMessages
            .AsNoTracking()
            .Where(message => message.SenderMemberId == senderMemberId)
            .Select(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
        return createdAtValues.Count(createdAt => createdAt >= sinceUtc);
    }

    private async Task<int> CountIdenticalMessagesBySenderSinceInMemoryAsync(
        Guid senderMemberId,
        string body,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PrivateMessages
            .AsNoTracking()
            .Where(message => message.SenderMemberId == senderMemberId && message.Body == body)
            .Select(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Count(createdAt => createdAt >= sinceUtc);
    }

    private async Task<int> CountDistinctNewRecipientsSinceInMemoryAsync(
        Guid senderMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var senderRows = await dbContext.PrivateMessages
            .AsNoTracking()
            .Where(message => message.SenderMemberId == senderMemberId)
            .Select(message => new { message.ConversationId, message.CreatedAt })
            .ToListAsync(cancellationToken);

        var recentConversationIds = senderRows
            .Where(row => row.CreatedAt >= sinceUtc)
            .Select(row => row.ConversationId)
            .Distinct()
            .ToList();
        if (recentConversationIds.Count == 0)
        {
            return 0;
        }

        var conversationCreatedAtValues = await dbContext.PrivateConversations
            .AsNoTracking()
            .Where(conversation => recentConversationIds.Contains(conversation.Id))
            .Select(conversation => new { conversation.Id, conversation.CreatedAt })
            .ToListAsync(cancellationToken);

        return conversationCreatedAtValues.Count(row => row.CreatedAt >= sinceUtc);
    }

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);

    internal static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && sql.Number is 2601 or 2627)
            {
                return true;
            }

            if (inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || inner.Message.Contains("unique index", StringComparison.OrdinalIgnoreCase)
                || inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static (Guid Low, Guid High) OrderPair(Guid a, Guid b) =>
        a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private static string NormalizeBody(string body) =>
        string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();

    private static string TruncatePreview(string body) =>
        body.Length <= PrivateMessageLimits.PreviewLength
            ? body
            : body[..PrivateMessageLimits.PreviewLength];

    private sealed record InboxPageRow(
        Guid ConversationId,
        long? LastReadSortKey,
        string Preview,
        DateTimeOffset LastMessageAt,
        string OtherDisplayName,
        Guid OtherId);

    private sealed record InboxPageRowWithUnread(
        Guid ConversationId,
        string Preview,
        DateTimeOffset LastMessageAt,
        string OtherDisplayName,
        Guid OtherId,
        int UnreadCount);

    private sealed record ConversationMessageRow(
        Guid Id,
        Guid SenderMemberId,
        string SenderName,
        string Body,
        DateTimeOffset CreatedAt,
        long SortKey);
}

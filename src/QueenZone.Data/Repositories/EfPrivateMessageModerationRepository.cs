using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfPrivateMessageModerationRepository(QueenZoneDbContext dbContext)
    : IPrivateMessageModerationRepository
{
    public Task<bool> IsBlockedAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default) =>
        dbContext.MemberMessageBlocks
            .AsNoTracking()
            .AnyAsync(
                b => b.BlockerMemberId == blockerMemberId && b.BlockedMemberId == blockedMemberId,
                cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListBlockedMemberIdsAsync(
        Guid blockerMemberId,
        IReadOnlyCollection<Guid> candidateMemberIds,
        CancellationToken cancellationToken = default)
    {
        var candidates = candidateMemberIds.Distinct().ToList();
        if (candidates.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var blocked = await dbContext.MemberMessageBlocks
            .AsNoTracking()
            .Where(b => b.BlockerMemberId == blockerMemberId
                && candidates.Contains(b.BlockedMemberId))
            .Select(b => b.BlockedMemberId)
            .ToListAsync(cancellationToken);
        return blocked.ToHashSet();
    }

    public Task<bool> IsMessagingBlockedAsync(
        Guid memberA,
        Guid memberB,
        CancellationToken cancellationToken = default) =>
        dbContext.MemberMessageBlocks
            .AsNoTracking()
            .AnyAsync(
                b => (b.BlockerMemberId == memberA && b.BlockedMemberId == memberB)
                    || (b.BlockerMemberId == memberB && b.BlockedMemberId == memberA),
                cancellationToken);

    public async Task BlockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        DateTimeOffset blockedAt,
        CancellationToken cancellationToken = default)
    {
        var exists = await IsBlockedAsync(blockerMemberId, blockedMemberId, cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.MemberMessageBlocks.Add(new MemberMessageBlockEntity
        {
            Id = Guid.NewGuid(),
            BlockerMemberId = blockerMemberId,
            BlockedMemberId = blockedMemberId,
            CreatedAt = blockedAt,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EfPrivateMessageRepository.IsUniqueConstraintViolation(ex))
        {
            // Concurrent block insert — treat as already blocked.
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<bool> UnblockAsync(
        Guid blockerMemberId,
        Guid blockedMemberId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.MemberMessageBlocks
            .Where(b => b.BlockerMemberId == blockerMemberId && b.BlockedMemberId == blockedMemberId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<PrivateMessageReportResult> CreateReportAsync(
        Guid reporterMemberId,
        Guid conversationId,
        Guid messageId,
        string? reason,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.PrivateMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null || message.ConversationId != conversationId)
        {
            return new PrivateMessageReportResult(false, null, PrivateMessageReportText.MessageNotFound);
        }

        var isParticipant = await IsParticipantAsync(conversationId, reporterMemberId, cancellationToken);
        if (!isParticipant)
        {
            return new PrivateMessageReportResult(false, null, PrivateMessageReportText.NotAParticipant);
        }

        if (message.SenderMemberId == reporterMemberId)
        {
            return new PrivateMessageReportResult(false, null, PrivateMessageReportText.CannotReportOwn);
        }

        var existing = await dbContext.PrivateMessageReports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.ReporterMemberId == reporterMemberId && r.MessageId == messageId,
                cancellationToken);
        if (existing is not null)
        {
            return new PrivateMessageReportResult(true, existing.Id, null, AlreadyReported: true);
        }

        var senderName = await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(m => m.Id == message.SenderMemberId)
            .Select(m => m.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = "Unknown member";
        }

        var precedingRows = await dbContext.PrivateMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.SortKey < message.SortKey)
            .OrderByDescending(m => m.SortKey)
            .Take(PrivateMessageLimits.ReportPrecedingMessageCount)
            .Select(m => new
            {
                m.Id,
                m.SenderMemberId,
                SenderName = m.Sender != null ? m.Sender.DisplayName : string.Empty,
                m.Body,
                m.CreatedAt,
                m.SortKey,
            })
            .ToListAsync(cancellationToken);
        var preceding = precedingRows
            .OrderBy(m => m.SortKey)
            .Select(m => new PrivateMessageReportContextItem(
                m.Id,
                m.SenderMemberId,
                string.IsNullOrWhiteSpace(m.SenderName) ? "Unknown member" : m.SenderName,
                m.Body,
                m.CreatedAt))
            .ToList();

        var entity = PrivateMessageReportMapping.CreateEntity(
            reporterMemberId,
            message,
            senderName,
            preceding,
            reason,
            createdAt);
        dbContext.PrivateMessageReports.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PrivateMessageReportResult(true, entity.Id, null);
        }
        catch (DbUpdateException ex) when (EfPrivateMessageRepository.IsUniqueConstraintViolation(ex))
        {
            dbContext.ChangeTracker.Clear();
            var raced = await dbContext.PrivateMessageReports
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.ReporterMemberId == reporterMemberId && r.MessageId == messageId,
                    cancellationToken);
            if (raced is not null)
            {
                return new PrivateMessageReportResult(true, raced.Id, null, AlreadyReported: true);
            }

            throw;
        }
    }

    public async Task<PrivateMessageReport?> GetReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PrivateMessageReports
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        return entity is null ? null : PrivateMessageReportMapping.ToModel(entity);
    }

    public async Task<IReadOnlySet<Guid>> GetReportedMessageIdsAsync(
        Guid conversationId,
        Guid reporterMemberId,
        CancellationToken cancellationToken = default)
    {
        var ids = await dbContext.PrivateMessageReports
            .AsNoTracking()
            .Where(r => r.ConversationId == conversationId && r.ReporterMemberId == reporterMemberId)
            .Select(r => r.MessageId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<PrivateMessageReportListPage> ListReportsAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var statusFilter = NormalizeOptionalReportStatus(status);

        var query = dbContext.PrivateMessageReports.AsNoTracking();
        if (statusFilter is not null)
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        if (IsSqliteDatabase())
        {
            var allRows = await query
                .Select(r => new PrivateMessageReportListItem(
                    r.Id,
                    r.MessageId,
                    r.ConversationId,
                    r.ReporterMemberId,
                    r.Reporter != null ? r.Reporter.DisplayName : "Unknown member",
                    r.ReportedMemberId,
                    r.Reported != null ? r.Reported.DisplayName : "Unknown member",
                    r.Reason,
                    r.Status,
                    r.CreatedAt))
                .ToListAsync(cancellationToken);

            var ordered = allRows.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
            var pagedItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PrivateMessageReportListPage(pagedItems, ordered.Count, statusFilter);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new PrivateMessageReportListItem(
                r.Id,
                r.MessageId,
                r.ConversationId,
                r.ReporterMemberId,
                r.Reporter != null ? r.Reporter.DisplayName : "Unknown member",
                r.ReportedMemberId,
                r.Reported != null ? r.Reported.DisplayName : "Unknown member",
                r.Reason,
                r.Status,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PrivateMessageReportListPage(items, totalCount, statusFilter);
    }

    public async Task<int> CountOpenReportsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.PrivateMessageReports
            .AsNoTracking()
            .CountAsync(r => r.Status == PrivateMessageReportStatus.Open, cancellationToken);

    public async Task<PrivateMessageReport?> UpdateReportStatusAsync(
        Guid reportId,
        string status,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PrivateMessageReports
            .SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var normalizedStatus = PrivateMessageReportStatus.Normalize(status);
        var previousStatus = entity.Status;
        entity.Status = normalizedStatus;

        if (!string.Equals(previousStatus, normalizedStatus, StringComparison.Ordinal))
        {
            dbContext.PrivateMessageReportAuditLogs.Add(new PrivateMessageReportAuditLogEntity
            {
                ReportId = reportId,
                Action = PrivateMessageReportAuditAction.StatusChanged,
                ActorEmail = actorEmail,
                OccurredAt = DateTimeOffset.UtcNow,
                Details = $"{previousStatus} -> {normalizedStatus}",
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return PrivateMessageReportMapping.ToModel(entity);
    }

    public async Task AppendReportViewedAuditAsync(
        Guid reportId,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        dbContext.PrivateMessageReportAuditLogs.Add(new PrivateMessageReportAuditLogEntity
        {
            ReportId = reportId,
            Action = PrivateMessageReportAuditAction.Viewed,
            ActorEmail = actorEmail,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredReportsAsync(
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var cutoff = asOfUtc - PrivateMessageLimits.ReportRetentionAfterTerminalStatus;

        var terminalReportIds = await dbContext.PrivateMessageReports
            .AsNoTracking()
            .Where(r => r.Status == PrivateMessageReportStatus.Dismissed
                || r.Status == PrivateMessageReportStatus.Actioned)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (terminalReportIds.Count == 0)
        {
            return 0;
        }

        // SQLite (tests) cannot translate MAX() over DateTimeOffset; materialise the relevant
        // audit rows and aggregate client-side. Production always targets SQL Server.
        var statusChangeRows = await dbContext.PrivateMessageReportAuditLogs
            .AsNoTracking()
            .Where(log =>
                log.Action == PrivateMessageReportAuditAction.StatusChanged
                && terminalReportIds.Contains(log.ReportId))
            .Select(log => new { log.ReportId, log.OccurredAt })
            .ToListAsync(cancellationToken);

        var eligibleIds = statusChangeRows
            .GroupBy(row => row.ReportId)
            .Where(g => g.Max(row => row.OccurredAt) <= cutoff)
            .Select(g => g.Key)
            .ToList();
        if (eligibleIds.Count == 0)
        {
            return 0;
        }

        return await dbContext.PrivateMessageReports
            .Where(r => eligibleIds.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string? NormalizeOptionalReportStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return PrivateMessageReportStatus.Normalize(status);
    }

    private Task<bool> IsParticipantAsync(
        Guid conversationId,
        Guid memberId,
        CancellationToken cancellationToken) =>
        dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.MemberId == memberId, cancellationToken);

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);
}

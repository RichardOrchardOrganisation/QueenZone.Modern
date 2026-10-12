using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfPhotoCorrectionRepository(QueenZoneDbContext dbContext) : IPhotoCorrectionRepository
{
    public async Task<PhotoCorrectionProposal> CreateAsync(
        NewPhotoCorrectionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if (proposal.Id is { } retryId && retryId != Guid.Empty)
        {
            var existing = await dbContext.PhotoCorrectionProposals
                .AsNoTracking()
                .Include(row => row.Submitter)
                .SingleOrDefaultAsync(row => row.Id == retryId, cancellationToken);
            if (existing is not null)
            {
                return existing.SubmitterMemberId == proposal.SubmitterMemberId
                    ? PhotoCorrectionRules.Map(existing, existing.Submitter?.DisplayName)
                    : throw new InvalidOperationException("This proposal id is already in use.");
            }
        }

        var open = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == proposal.SubmitterMemberId
                && (row.Status == PhotoCorrectionStatus.Pending || row.Status == PhotoCorrectionStatus.NeedsInfo))
            .ToListAsync(cancellationToken);
        if (open.Any(row => PhotoCorrectionRules.IsDuplicateOpen(row, proposal)))
        {
            throw new DuplicatePhotoCorrectionException();
        }

        if (open.Count >= PhotoCorrectionValidation.MaxOpenPerMember)
        {
            throw new PhotoCorrectionLimitException(PhotoCorrectionValidation.MaxOpenPerMember);
        }

        var entity = PhotoCorrectionRules.NewEntity(proposal);
        dbContext.PhotoCorrectionProposals.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PhotoCorrectionRules.Map(entity, null);
    }

    public async Task<PhotoCorrectionProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return entity is null ? null : PhotoCorrectionRules.Map(entity, entity.Submitter?.DisplayName);
    }

    public async Task<IReadOnlyList<PhotoCorrectionListItem>> GetOpenAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        var rows = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Include(row => row.Submitter)
            .Where(row => row.Status == PhotoCorrectionStatus.Pending || row.Status == PhotoCorrectionStatus.NeedsInfo)
            .ToListAsync(cancellationToken);

        // DateTimeOffset cannot be ordered in SQLite, so order client-side; the open queue is small.
        return rows
            .OrderBy(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => PhotoCorrectionRules.MapListItem(row, row.Submitter?.DisplayName))
            .ToList();
    }

    public async Task<IReadOnlyList<PhotoCorrectionProposal>> GetForPhotoAsync(
        int picId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Include(row => row.Submitter)
            .Where(row => row.PicId == picId)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Select(row => PhotoCorrectionRules.Map(row, row.Submitter?.DisplayName))
            .ToList();
    }

    public async Task<SubmissionListPage<PhotoCorrectionProposal>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        var rows = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Where(row => row.SubmitterMemberId == submitterMemberId)
            .ToListAsync(cancellationToken);
        return PhotoCorrectionRules.ToMemberPage(rows, skip, take);
    }

    public async Task<IReadOnlyList<PublicPhotoCorrection>> GetApprovedPublicAsync(
        int picId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.PhotoCorrectionProposals
            .AsNoTracking()
            .Where(row => row.PicId == picId && row.Status == PhotoCorrectionStatus.Approved)
            .ToListAsync(cancellationToken);
        return PhotoCorrectionRules.ToPublic(rows);
    }

    public async Task<IReadOnlyList<PhotoCorrectionAuditEntry>> GetAuditAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.PhotoCorrectionAuditLogs
            .AsNoTracking()
            .Where(log => log.PhotoCorrectionProposalId == id)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(log => log.OccurredAt).ThenBy(log => log.Id).Select(PhotoCorrectionRules.MapAudit).ToList();
    }

    public Task<PhotoCorrectionProposal?> ApproveAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        string? appliedFromValue,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            id,
            entity =>
            {
                var audit = PhotoCorrectionRules.Decide(
                    entity,
                    PhotoCorrectionStatus.Approved,
                    "Approved",
                    reviewerEmail,
                    reviewNotes,
                    null,
                    appliedFromValue is null
                        ? $"Accepted {entity.Kind} correction (recorded; not applied automatically)."
                        : $"Accepted and applied {entity.Kind}. Previous value: {appliedFromValue}");
                if (appliedFromValue is not null)
                {
                    entity.AppliedFromValue = appliedFromValue;
                    entity.AppliedAt = entity.ReviewedAt;
                }

                return audit;
            },
            cancellationToken);

    public Task<PhotoCorrectionProposal?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.Rejected,
                "Rejected",
                reviewerEmail,
                reviewNotes,
                PhotoCorrectionRules.RequireMessage(messageToMember, "rejecting a proposal"),
                "Rejected."),
            cancellationToken);

    public Task<PhotoCorrectionProposal?> RequestInfoAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.NeedsInfo,
                "InfoRequested",
                reviewerEmail,
                reviewNotes,
                PhotoCorrectionRules.RequireMessage(messageToMember, "asking for more information"),
                "Asked the member for more information."),
            cancellationToken);

    public Task<PhotoCorrectionProposal?> RevertAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.Reverted,
                "Reverted",
                reviewerEmail,
                reviewNotes,
                null,
                $"Reverted {entity.Kind} correction; restored: {entity.AppliedFromValue ?? "(nothing applied)"}"),
            cancellationToken);

    private async Task<PhotoCorrectionProposal?> DecideAsync(
        Guid id,
        Func<PhotoCorrectionProposalEntity, PhotoCorrectionAuditLogEntity> decide,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.PhotoCorrectionProposals
            .Include(row => row.Submitter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        dbContext.PhotoCorrectionAuditLogs.Add(decide(entity));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OptimisticConcurrencyException();
        }

        return PhotoCorrectionRules.Map(entity, entity.Submitter?.DisplayName);
    }
}

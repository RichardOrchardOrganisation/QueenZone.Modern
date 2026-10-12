using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryPhotoCorrectionRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    : IPhotoCorrectionRepository
{
    private readonly object sync = new();
    private readonly List<PhotoCorrectionProposalEntity> proposals = [];
    private readonly List<PhotoCorrectionAuditLogEntity> auditLogs = [];
    private long nextAuditId = 1;

    public Task<PhotoCorrectionProposal> CreateAsync(
        NewPhotoCorrectionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        lock (sync)
        {
            if (proposal.Id is { } retryId && retryId != Guid.Empty
                && proposals.FirstOrDefault(row => row.Id == retryId) is { } existing)
            {
                return existing.SubmitterMemberId == proposal.SubmitterMemberId
                    ? Task.FromResult(Map(existing))
                    : throw new InvalidOperationException("This proposal id is already in use.");
            }

            var open = proposals
                .Where(row => row.SubmitterMemberId == proposal.SubmitterMemberId && PhotoCorrectionStatus.IsOpen(row.Status))
                .ToList();
            if (open.Any(row => PhotoCorrectionRules.IsDuplicateOpen(row, proposal)))
            {
                throw new DuplicatePhotoCorrectionException();
            }

            if (open.Count >= PhotoCorrectionValidation.MaxOpenPerMember)
            {
                throw new PhotoCorrectionLimitException(PhotoCorrectionValidation.MaxOpenPerMember);
            }

            var entity = PhotoCorrectionRules.NewEntity(proposal);
            foreach (var log in entity.AuditLogs)
            {
                log.Id = nextAuditId++;
                auditLogs.Add(log);
            }

            entity.AuditLogs.Clear();
            proposals.Add(entity);
            return Task.FromResult(Map(entity));
        }
    }

    public Task<PhotoCorrectionProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = proposals.FirstOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<IReadOnlyList<PhotoCorrectionListItem>> GetOpenAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        lock (sync)
        {
            IReadOnlyList<PhotoCorrectionListItem> result = proposals
                .Where(row => PhotoCorrectionStatus.IsOpen(row.Status))
                .OrderBy(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Skip(skip)
                .Take(take)
                .Select(row => PhotoCorrectionRules.MapListItem(row, resolveMember?.Invoke(row.SubmitterMemberId)?.DisplayName))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<PhotoCorrectionProposal>> GetForPhotoAsync(
        int picId,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<PhotoCorrectionProposal> result = proposals
                .Where(row => row.PicId == picId)
                .OrderByDescending(row => row.SubmittedAt)
                .ThenBy(row => row.Id)
                .Select(Map)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<SubmissionListPage<PhotoCorrectionProposal>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        lock (sync)
        {
            return Task.FromResult(PhotoCorrectionRules.ToMemberPage(
                proposals.Where(row => row.SubmitterMemberId == submitterMemberId).ToList(),
                skip,
                take));
        }
    }

    public Task<IReadOnlyList<PublicPhotoCorrection>> GetApprovedPublicAsync(
        int picId,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(PhotoCorrectionRules.ToPublic(proposals.Where(row => row.PicId == picId)));
        }
    }

    public Task<IReadOnlyList<PhotoCorrectionAuditEntry>> GetAuditAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IReadOnlyList<PhotoCorrectionAuditEntry> result = auditLogs
                .Where(log => log.PhotoCorrectionProposalId == id)
                .OrderBy(log => log.OccurredAt)
                .ThenBy(log => log.Id)
                .Select(PhotoCorrectionRules.MapAudit)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<PhotoCorrectionProposal?> ApproveAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        string? appliedFromValue,
        CancellationToken cancellationToken = default) =>
        Decide(
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
            });

    public Task<PhotoCorrectionProposal?> RejectAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        Decide(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.Rejected,
                "Rejected",
                reviewerEmail,
                reviewNotes,
                PhotoCorrectionRules.RequireMessage(messageToMember, "rejecting a proposal"),
                "Rejected."));

    public Task<PhotoCorrectionProposal?> RequestInfoAsync(
        Guid id,
        string reviewerEmail,
        string messageToMember,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        Decide(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.NeedsInfo,
                "InfoRequested",
                reviewerEmail,
                reviewNotes,
                PhotoCorrectionRules.RequireMessage(messageToMember, "asking for more information"),
                "Asked the member for more information."));

    public Task<PhotoCorrectionProposal?> RespondAsync(
        Guid id,
        Guid submitterMemberId,
        string reply,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return proposals.Any(row => row.Id == id && row.SubmitterMemberId == submitterMemberId)
                ? Decide(id, entity => PhotoCorrectionRules.Respond(entity, reply))
                : Task.FromResult<PhotoCorrectionProposal?>(null);
        }
    }

    public Task<PhotoCorrectionProposal?> RevertAsync(
        Guid id,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        Decide(
            id,
            entity => PhotoCorrectionRules.Decide(
                entity,
                PhotoCorrectionStatus.Reverted,
                "Reverted",
                reviewerEmail,
                reviewNotes,
                null,
                $"Reverted {entity.Kind} correction; restored: {entity.AppliedFromValue ?? "(nothing applied)"}"));

    private Task<PhotoCorrectionProposal?> Decide(
        Guid id,
        Func<PhotoCorrectionProposalEntity, PhotoCorrectionAuditLogEntity> decide)
    {
        lock (sync)
        {
            var entity = proposals.FirstOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<PhotoCorrectionProposal?>(null);
            }

            var audit = decide(entity);
            audit.Id = nextAuditId++;
            auditLogs.Add(audit);
            return Task.FromResult<PhotoCorrectionProposal?>(Map(entity));
        }
    }

    private PhotoCorrectionProposal Map(PhotoCorrectionProposalEntity entity) =>
        PhotoCorrectionRules.Map(entity, resolveMember?.Invoke(entity.SubmitterMemberId)?.DisplayName);
}

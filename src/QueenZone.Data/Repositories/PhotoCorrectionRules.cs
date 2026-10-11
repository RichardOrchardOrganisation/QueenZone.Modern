using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Entity-level rules shared by the EF and in-memory photo correction repositories so the
/// two implementations cannot drift.
/// </summary>
internal static class PhotoCorrectionRules
{
    internal static PhotoCorrectionProposalEntity NewEntity(NewPhotoCorrectionProposal proposal)
    {
        var entity = new PhotoCorrectionProposalEntity
        {
            Id = SubmissionInput.IdOrNew(proposal.Id),
            PicId = proposal.PicId,
            SubmitterMemberId = proposal.SubmitterMemberId,
            Kind = proposal.Kind.Trim(),
            CurrentValue = SubmissionInput.NormalizeOptional(proposal.CurrentValue, PhotoCorrectionValidation.MaxValueLength),
            ProposedValue = proposal.ProposedValue.Trim(),
            Explanation = proposal.Explanation.Trim(),
            EvidenceUrl = PhotoCorrectionValidation.IsSafeEvidenceUrl(proposal.EvidenceUrl)
                ? proposal.EvidenceUrl!.Trim()
                : null,
            EvidenceType = SubmissionInput.NormalizeOptional(proposal.EvidenceType, 50),
            Confidence = proposal.Confidence.Trim(),
            PublicCreditName = SubmissionInput.NormalizeOptional(
                proposal.PublicCreditName,
                PhotoCorrectionValidation.MaxCreditNameLength),
            Status = PhotoCorrectionStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        entity.AuditLogs.Add(Audit(entity.Id, "Submitted", string.Empty, entity.SubmittedAt, $"Member proposed a {entity.Kind} correction."));
        return entity;
    }

    internal static bool IsDuplicateOpen(PhotoCorrectionProposalEntity existing, NewPhotoCorrectionProposal proposal) =>
        existing.SubmitterMemberId == proposal.SubmitterMemberId
        && existing.PicId == proposal.PicId
        && string.Equals(existing.Kind, proposal.Kind.Trim(), StringComparison.Ordinal)
        && PhotoCorrectionStatus.IsOpen(existing.Status);

    internal static PhotoCorrectionAuditLogEntity Decide(
        PhotoCorrectionProposalEntity entity,
        string newStatus,
        string action,
        string reviewerEmail,
        string? reviewNotes,
        string? messageToMember,
        string? details)
    {
        SubmissionReview.EnsureTransition(entity.Status, newStatus, PhotoCorrectionWorkflow.TryValidateStatusChange);

        var reviewedAt = DateTimeOffset.UtcNow;
        entity.Status = newStatus;
        entity.ReviewedAt = reviewedAt;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);
        if (messageToMember is not null)
        {
            entity.MessageToMember = SubmissionInput.NormalizeOptional(
                messageToMember,
                PhotoCorrectionValidation.MaxMessageLength);
        }

        return Audit(entity.Id, action, entity.ReviewerEmail ?? string.Empty, reviewedAt, details);
    }

    internal static string RequireMessage(string? message, string whatFor) =>
        SubmissionInput.NormalizeOptional(message, PhotoCorrectionValidation.MaxMessageLength)
            ?? throw new InvalidOperationException($"A message for the member is required when {whatFor}.");

    internal static PhotoCorrectionAuditLogEntity Audit(
        Guid proposalId,
        string action,
        string actorEmail,
        DateTimeOffset occurredAt,
        string? details) =>
        new()
        {
            PhotoCorrectionProposalId = proposalId,
            Action = action,
            ActorEmail = actorEmail,
            OccurredAt = occurredAt,
            Details = details is { Length: > 2000 } ? details[..2000] : details,
        };

    internal static PhotoCorrectionProposal Map(PhotoCorrectionProposalEntity entity, string? submitterDisplayName) =>
        new(
            entity.Id,
            entity.PicId,
            entity.SubmitterMemberId,
            entity.Kind,
            entity.CurrentValue,
            entity.ProposedValue,
            entity.Explanation,
            entity.EvidenceUrl,
            entity.EvidenceType,
            entity.Confidence,
            entity.PublicCreditName,
            entity.Status,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.MessageToMember,
            entity.AppliedFromValue,
            entity.AppliedAt,
            submitterDisplayName);

    internal static PhotoCorrectionListItem MapListItem(PhotoCorrectionProposalEntity entity, string? submitterDisplayName) =>
        new(
            entity.Id,
            entity.PicId,
            entity.Kind,
            entity.ProposedValue,
            submitterDisplayName ?? "Unknown member",
            entity.SubmittedAt,
            entity.Status);

    internal static PhotoCorrectionAuditEntry MapAudit(PhotoCorrectionAuditLogEntity log) =>
        new(log.Id, log.Action, log.ActorEmail, log.OccurredAt, log.Details);
}

namespace QueenZone.Data.Entities;

public sealed class PhotoCorrectionProposalEntity
{
    public Guid Id { get; set; }

    /// <summary>PIC_FILES_T.PIC_ID of the photograph this proposal is about.</summary>
    public int PicId { get; set; }

    public Guid SubmitterMemberId { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>The value shown to the member when they proposed; used to detect stale approvals.</summary>
    public string? CurrentValue { get; set; }

    public string ProposedValue { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    /// <summary>Editor-only. Never rendered publicly.</summary>
    public string? EvidenceUrl { get; set; }

    public string? EvidenceType { get; set; }

    public string Confidence { get; set; } = string.Empty;

    /// <summary>Null means the member chose no public credit.</summary>
    public string? PublicCreditName { get; set; }

    public string Status { get; set; } = PhotoCorrectionStatus.Pending;

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewerEmail { get; set; }

    /// <summary>Internal editor notes. Never shown to the member or the public.</summary>
    public string? ReviewNotes { get; set; }

    /// <summary>Shown to the submitter: rejection reason or the question for a needs-info request.</summary>
    public string? MessageToMember { get; set; }

    /// <summary>Value that was in place before an approved caption was applied; enables revert.</summary>
    public string? AppliedFromValue { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public MemberAccount? Submitter { get; set; }

    public ICollection<PhotoCorrectionAuditLogEntity> AuditLogs { get; set; } =
        new List<PhotoCorrectionAuditLogEntity>();
}

public sealed class PhotoCorrectionAuditLogEntity
{
    public long Id { get; set; }

    public Guid PhotoCorrectionProposalId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ActorEmail { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? Details { get; set; }

    public PhotoCorrectionProposalEntity? Proposal { get; set; }
}

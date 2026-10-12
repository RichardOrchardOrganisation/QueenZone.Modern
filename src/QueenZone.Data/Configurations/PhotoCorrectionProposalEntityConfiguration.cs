using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PhotoCorrectionProposalEntityConfiguration : IEntityTypeConfiguration<PhotoCorrectionProposalEntity>
{
    public void Configure(EntityTypeBuilder<PhotoCorrectionProposalEntity> builder)
    {
        builder.ToTable("PhotoCorrectionProposals");
        builder.HasKey(proposal => proposal.Id);

        builder.Property(proposal => proposal.Kind).HasMaxLength(50).IsRequired();
        builder.Property(proposal => proposal.CurrentValue).HasMaxLength(PhotoCorrectionValidation.MaxValueLength);
        builder.Property(proposal => proposal.ProposedValue)
            .HasMaxLength(PhotoCorrectionValidation.MaxValueLength)
            .IsRequired();
        builder.Property(proposal => proposal.Explanation)
            .HasMaxLength(PhotoCorrectionValidation.MaxExplanationLength)
            .IsRequired();
        builder.Property(proposal => proposal.EvidenceUrl).HasMaxLength(PhotoCorrectionValidation.MaxEvidenceUrlLength);
        builder.Property(proposal => proposal.EvidenceType).HasMaxLength(50);
        builder.Property(proposal => proposal.Confidence).HasMaxLength(50).IsRequired();
        builder.Property(proposal => proposal.PublicCreditName).HasMaxLength(PhotoCorrectionValidation.MaxCreditNameLength);
        // Status doubles as the concurrency token: two editors deciding the same proposal
        // cannot both win, because the second UPDATE no longer matches the old status.
        builder.Property(proposal => proposal.Status).HasMaxLength(50).IsRequired().IsConcurrencyToken();
        builder.Property(proposal => proposal.SubmittedAt).IsRequired();
        builder.Property(proposal => proposal.ReviewerEmail).HasMaxLength(256);
        builder.Property(proposal => proposal.ReviewNotes).HasMaxLength(500);
        builder.Property(proposal => proposal.MessageToMember).HasMaxLength(PhotoCorrectionValidation.MaxMessageLength);
        builder.Property(proposal => proposal.MemberReply).HasMaxLength(PhotoCorrectionValidation.MaxExplanationLength);
        builder.Property(proposal => proposal.AppliedFromValue).HasMaxLength(PhotoCorrectionValidation.MaxValueLength);

        builder.HasIndex(proposal => new { proposal.Status, proposal.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoCorrectionProposals_Status_SubmittedAt");
        builder.HasIndex(proposal => new { proposal.PicId, proposal.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoCorrectionProposals_Photo_SubmittedAt");
        builder.HasIndex(proposal => new { proposal.SubmitterMemberId, proposal.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoCorrectionProposals_Submitter_SubmittedAt");

        builder.HasOne(proposal => proposal.Submitter)
            .WithMany()
            .HasForeignKey(proposal => proposal.SubmitterMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PhotoCorrectionAuditLogEntityConfiguration : IEntityTypeConfiguration<PhotoCorrectionAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<PhotoCorrectionAuditLogEntity> builder)
    {
        builder.ToTable("PhotoCorrectionAuditLog");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);

        builder.HasIndex(log => new { log.PhotoCorrectionProposalId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoCorrectionAuditLog_Proposal_OccurredAt");

        builder.HasOne(log => log.Proposal)
            .WithMany(proposal => proposal.AuditLogs)
            .HasForeignKey(log => log.PhotoCorrectionProposalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

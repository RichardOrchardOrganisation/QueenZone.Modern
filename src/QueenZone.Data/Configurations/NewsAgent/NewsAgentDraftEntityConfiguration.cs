using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentDraftEntityConfiguration : IEntityTypeConfiguration<NewsAgentDraftEntity>
{
    public void Configure(EntityTypeBuilder<NewsAgentDraftEntity> builder)
    {
        builder.ToTable("NewsAgentDrafts");
        builder.HasKey(draft => draft.Id);

        builder.Property(draft => draft.ProposedTitle).HasMaxLength(500).IsRequired();
        builder.Property(draft => draft.ProposedSlug).HasMaxLength(200);
        builder.Property(draft => draft.ProposedExcerpt).HasMaxLength(2000).IsRequired();
        builder.Property(draft => draft.ProposedBody).IsRequired();
        builder.Property(draft => draft.AttributionText).HasMaxLength(2000);
        builder.Property(draft => draft.SourceNotes).HasMaxLength(2000);
        builder.Property(draft => draft.ConfidenceNotes).HasMaxLength(2000);
        builder.Property(draft => draft.CreatedAt).IsRequired();
        builder.Property(draft => draft.UpdatedAt).IsRequired();

        builder.HasIndex(draft => draft.CandidateId)
            .IsUnique()
            .HasDatabaseName("IX_NewsAgentDrafts_CandidateId");

        builder.HasOne(draft => draft.Candidate)
            .WithOne(candidate => candidate.Draft)
            .HasForeignKey<NewsAgentDraftEntity>(draft => draft.CandidateId)
            .OnDelete(DeleteBehavior.ClientCascade);

        builder.HasOne(draft => draft.AiRun)
            .WithMany()
            .HasForeignKey(draft => draft.AiRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsCandidateEvidenceEntityConfiguration : IEntityTypeConfiguration<NewsCandidateEvidenceEntity>
{
    public void Configure(EntityTypeBuilder<NewsCandidateEvidenceEntity> builder)
    {
        builder.ToTable("NewsCandidateEvidence");
        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.SourceUrl).HasMaxLength(2000).IsRequired();
        builder.Property(evidence => evidence.CanonicalUrl).HasMaxLength(2000).IsRequired();
        builder.Property(evidence => evidence.SourceName).HasMaxLength(200).IsRequired();
        builder.Property(evidence => evidence.SourceTrustTier).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(evidence => evidence.FetchedTitle).HasMaxLength(500).IsRequired();
        builder.Property(evidence => evidence.Excerpt).HasMaxLength(4000);
        builder.Property(evidence => evidence.ContentHash).HasMaxLength(64);
        builder.Property(evidence => evidence.Etag).HasMaxLength(256);
        builder.Property(evidence => evidence.FetchedAt).IsRequired();
        builder.Property(evidence => evidence.CreatedAt).IsRequired();

        builder.HasIndex(evidence => new { evidence.CandidateId, evidence.FetchedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsCandidateEvidence_CandidateId_FetchedAt");

        builder.HasOne(evidence => evidence.Candidate)
            .WithMany(candidate => candidate.Evidence)
            .HasForeignKey(evidence => evidence.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

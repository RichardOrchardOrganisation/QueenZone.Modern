using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsCandidateEntityConfiguration : IEntityTypeConfiguration<NewsCandidateEntity>
{
    public void Configure(EntityTypeBuilder<NewsCandidateEntity> builder)
    {
        builder.ToTable("NewsCandidates");
        builder.HasKey(candidate => candidate.Id);

        builder.Property(candidate => candidate.SourceUrl).HasMaxLength(2000).IsRequired();
        builder.Property(candidate => candidate.CanonicalUrl).HasMaxLength(2000).IsRequired();
        builder.Property(candidate => candidate.CanonicalUrlHash).HasMaxLength(64).IsRequired();
        builder.Property(candidate => candidate.SourceTitle).HasMaxLength(500).IsRequired();
        builder.Property(candidate => candidate.ContentHash).HasMaxLength(64);
        builder.Property(candidate => candidate.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(candidate => candidate.RelevanceScore).HasPrecision(5, 4);
        builder.Property(candidate => candidate.ConfidenceScore).HasPrecision(5, 4);
        builder.Property(candidate => candidate.ReviewNotes).HasMaxLength(2000);
        builder.Property(candidate => candidate.DiscoveredAt).IsRequired();
        builder.Property(candidate => candidate.CreatedAt).IsRequired();
        builder.Property(candidate => candidate.UpdatedAt).IsRequired();

        builder.HasIndex(candidate => candidate.CanonicalUrlHash)
            .IsUnique()
            .HasDatabaseName("IX_NewsCandidates_CanonicalUrlHash");

        builder.HasIndex(candidate => new { candidate.Status, candidate.DiscoveredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsCandidates_Status_DiscoveredAt");

        builder.HasIndex(candidate => candidate.ContentHash)
            .HasDatabaseName("IX_NewsCandidates_ContentHash");

        builder.HasOne(candidate => candidate.Source)
            .WithMany()
            .HasForeignKey(candidate => candidate.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(candidate => candidate.DuplicateOfCandidate)
            .WithMany()
            .HasForeignKey(candidate => candidate.DuplicateOfCandidateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

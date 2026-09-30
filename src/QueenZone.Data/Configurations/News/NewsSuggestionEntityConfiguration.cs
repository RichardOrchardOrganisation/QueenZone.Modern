using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsSuggestionEntityConfiguration : IEntityTypeConfiguration<NewsSuggestionEntity>
{
    public void Configure(EntityTypeBuilder<NewsSuggestionEntity> builder)
    {
        builder.ToTable("NewsSuggestions");
        builder.HasKey(suggestion => suggestion.Id);

        builder.Property(suggestion => suggestion.Url).HasMaxLength(2000).IsRequired();
        builder.Property(suggestion => suggestion.UrlHash).HasMaxLength(64).IsRequired();
        builder.Property(suggestion => suggestion.Title).HasMaxLength(300);
        builder.Property(suggestion => suggestion.Notes).HasMaxLength(1000);
        builder.Property(suggestion => suggestion.Status).HasMaxLength(50).IsRequired();
        builder.Property(suggestion => suggestion.SubmittedAt).IsRequired();
        builder.Property(suggestion => suggestion.ReviewerEmail).HasMaxLength(256);
        builder.Property(suggestion => suggestion.ReviewNotes).HasMaxLength(500);

        builder.HasIndex(suggestion => suggestion.UrlHash)
            .IsUnique()
            .HasFilter("[Status] IN ('Pending', 'UnderReview')")
            .HasDatabaseName("IX_NewsSuggestions_UrlHash_Active");

        builder.HasIndex(suggestion => new { suggestion.Status, suggestion.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsSuggestions_Status_SubmittedAt");

        builder.HasIndex(suggestion => new { suggestion.SubmitterMemberId, suggestion.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsSuggestions_Submitter_SubmittedAt");

        builder.HasOne(suggestion => suggestion.Submitter)
            .WithMany()
            .HasForeignKey(suggestion => suggestion.SubmitterMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(suggestion => suggestion.DuplicateCandidate)
            .WithMany()
            .HasForeignKey(suggestion => suggestion.DuplicateCandidateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

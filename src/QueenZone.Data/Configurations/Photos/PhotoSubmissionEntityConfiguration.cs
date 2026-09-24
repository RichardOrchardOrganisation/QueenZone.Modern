using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PhotoSubmissionEntityConfiguration : IEntityTypeConfiguration<PhotoSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<PhotoSubmissionEntity> builder)
    {
        builder.ToTable("PhotoSubmissions");
        builder.HasKey(submission => submission.Id);

        builder.Property(submission => submission.Title).HasMaxLength(200).IsRequired();
        builder.Property(submission => submission.Description).HasMaxLength(1000);
        builder.Property(submission => submission.SuggestedCategory).HasMaxLength(100);
        builder.Property(submission => submission.ApprovedCategory).HasMaxLength(100);
        builder.Property(submission => submission.BlobPath).HasMaxLength(512).IsRequired();
        builder.Property(submission => submission.WebOptimizedBlobPath).HasMaxLength(512).IsRequired();
        builder.Property(submission => submission.ThumbnailBlobPath).HasMaxLength(512).IsRequired();
        builder.Property(submission => submission.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(submission => submission.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
        builder.Property(submission => submission.SubmittedAt).IsRequired();
        builder.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
        builder.Property(submission => submission.ReviewNotes).HasMaxLength(500);
        builder.Property(submission => submission.RejectionReason).HasMaxLength(500);

        builder.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoSubmissions_Status_SubmittedAt");

        builder.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoSubmissions_Submitter_SubmittedAt");

        builder.HasOne(submission => submission.Submitter)
            .WithMany()
            .HasForeignKey(submission => submission.SubmitterMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

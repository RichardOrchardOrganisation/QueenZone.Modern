using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class TriviaFactSubmissionEntityConfiguration : IEntityTypeConfiguration<TriviaFactSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<TriviaFactSubmissionEntity> builder)
    {
        builder.ToTable("TriviaFactSubmissions");
        builder.HasKey(submission => submission.Id);

        builder.Property(submission => submission.Text)
            .HasMaxLength(TriviaValidation.MaxTextLength)
            .IsRequired();
        builder.Property(submission => submission.Category).HasMaxLength(TriviaValidation.MaxCategoryLength);
        builder.Property(submission => submission.Difficulty).HasMaxLength(TriviaValidation.MaxDifficultyLength);
        builder.Property(submission => submission.SourceNote).HasMaxLength(TriviaValidation.MaxSourceNoteLength);
        builder.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
        builder.Property(submission => submission.SubmittedAt).IsRequired();
        builder.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
        builder.Property(submission => submission.ReviewNotes).HasMaxLength(500);
        builder.Property(submission => submission.RejectionReason).HasMaxLength(500);

        builder.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TriviaFactSubmissions_Status_SubmittedAt");

        builder.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TriviaFactSubmissions_Submitter_SubmittedAt");

        builder.HasOne(submission => submission.Submitter)
            .WithMany()
            .HasForeignKey(submission => submission.SubmitterMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

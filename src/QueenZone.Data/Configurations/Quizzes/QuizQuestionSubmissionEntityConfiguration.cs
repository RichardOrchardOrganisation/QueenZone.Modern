using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizQuestionSubmissionEntityConfiguration : IEntityTypeConfiguration<QuizQuestionSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<QuizQuestionSubmissionEntity> builder)
    {
        builder.ToTable("QuizQuestionSubmissions");
        builder.HasKey(submission => submission.Id);

        builder.Property(submission => submission.QuestionText)
            .HasMaxLength(QuizValidation.QuestionMaxLength)
            .IsRequired();
        builder.Property(submission => submission.SourceNote)
            .HasMaxLength(QuizQuestionSubmissionValidation.MaxSourceNoteLength);
        builder.Property(submission => submission.Status).HasMaxLength(50).IsRequired();
        builder.Property(submission => submission.SubmittedAt).IsRequired();
        builder.Property(submission => submission.ReviewerEmail).HasMaxLength(256);
        builder.Property(submission => submission.ReviewNotes).HasMaxLength(500);
        builder.Property(submission => submission.RejectionReason).HasMaxLength(500);

        builder.HasIndex(submission => new { submission.Status, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_QuizQuestionSubmissions_Status_SubmittedAt");

        builder.HasIndex(submission => new { submission.SubmitterMemberId, submission.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_QuizQuestionSubmissions_Submitter_SubmittedAt");

        builder.HasOne(submission => submission.Submitter)
            .WithMany()
            .HasForeignKey(submission => submission.SubmitterMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(submission => submission.Options)
            .WithOne(option => option.Submission)
            .HasForeignKey(option => option.QuizQuestionSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

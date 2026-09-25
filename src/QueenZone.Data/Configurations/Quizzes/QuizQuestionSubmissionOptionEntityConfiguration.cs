using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizQuestionSubmissionOptionEntityConfiguration : IEntityTypeConfiguration<QuizQuestionSubmissionOptionEntity>
{
    public void Configure(EntityTypeBuilder<QuizQuestionSubmissionOptionEntity> builder)
    {
        builder.ToTable("QuizQuestionSubmissionOptions");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.OptionText).HasMaxLength(QuizValidation.OptionMaxLength).IsRequired();
        builder.HasIndex(option => new { option.QuizQuestionSubmissionId, option.DisplayOrder })
            .HasDatabaseName("IX_QuizQuestionSubmissionOptions_Submission_DisplayOrder");
    }
}

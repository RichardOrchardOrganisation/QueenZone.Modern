using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizQuestionEntityConfiguration : IEntityTypeConfiguration<QuizQuestionEntity>
{
    public void Configure(EntityTypeBuilder<QuizQuestionEntity> builder)
    {
        builder.ToTable("QuizQuestions");
        builder.HasKey(question => question.Id);
        builder.Property(question => question.QuestionText).HasMaxLength(QuizValidation.QuestionMaxLength).IsRequired();
        builder.Property(question => question.Category).HasMaxLength(QuizValidation.CategoryMaxLength);
        builder.Property(question => question.Difficulty).HasMaxLength(QuizValidation.DifficultyMaxLength);
        builder.HasIndex(question => new { question.QuizId, question.DisplayOrder })
            .HasDatabaseName("IX_QuizQuestions_QuizId_DisplayOrder");
        builder.HasMany(question => question.Options)
            .WithOne(option => option.Question)
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

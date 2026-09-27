using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizEntityConfiguration : IEntityTypeConfiguration<QuizEntity>
{
    public void Configure(EntityTypeBuilder<QuizEntity> builder)
    {
        builder.ToTable("Quizzes");
        builder.HasKey(quiz => quiz.Id);
        builder.Property(quiz => quiz.Title).HasMaxLength(QuizValidation.TitleMaxLength).IsRequired();
        builder.Property(quiz => quiz.Description).HasMaxLength(QuizValidation.DescriptionMaxLength);
        builder.Property(quiz => quiz.CreatedAt).IsRequired();
        builder.HasIndex(quiz => quiz.IsPublished)
            .HasDatabaseName("IX_Quizzes_IsPublished");
        builder.HasMany(quiz => quiz.Questions)
            .WithOne(question => question.Quiz)
            .HasForeignKey(question => question.QuizId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(quiz => quiz.Attempts)
            .WithOne(attempt => attempt.Quiz)
            .HasForeignKey(attempt => attempt.QuizId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

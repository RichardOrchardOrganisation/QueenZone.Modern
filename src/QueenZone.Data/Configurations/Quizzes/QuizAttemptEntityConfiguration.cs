using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizAttemptEntityConfiguration : IEntityTypeConfiguration<QuizAttemptEntity>
{
    public void Configure(EntityTypeBuilder<QuizAttemptEntity> builder)
    {
        builder.ToTable("QuizAttempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.CompletedAt).IsRequired();
        builder.HasIndex(attempt => new { attempt.QuizId, attempt.CompletedAt })
            .HasDatabaseName("IX_QuizAttempts_QuizId_CompletedAt");
        builder.HasIndex(attempt => new { attempt.MemberAccountId, attempt.CompletedAt })
            .HasDatabaseName("IX_QuizAttempts_MemberAccountId_CompletedAt");
    }
}

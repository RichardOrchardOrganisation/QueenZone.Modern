using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizSprintRunEntityConfiguration : IEntityTypeConfiguration<QuizSprintRunEntity>
{
    public void Configure(EntityTypeBuilder<QuizSprintRunEntity> builder)
    {
        builder.ToTable("QuizSprintRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.CompletedAt).IsRequired();
        builder.HasIndex(run => run.CompletedAt)
            .HasDatabaseName("IX_QuizSprintRuns_CompletedAt");
        builder.HasIndex(run => new { run.MemberAccountId, run.CompletedAt })
            .HasDatabaseName("IX_QuizSprintRuns_MemberAccountId_CompletedAt");
    }
}

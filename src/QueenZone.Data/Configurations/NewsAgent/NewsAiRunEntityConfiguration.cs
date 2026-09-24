using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAiRunEntityConfiguration : IEntityTypeConfiguration<NewsAiRunEntity>
{
    public void Configure(EntityTypeBuilder<NewsAiRunEntity> builder)
    {
        builder.ToTable("NewsAiRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(run => run.ModelProvider).HasMaxLength(100).IsRequired();
        builder.Property(run => run.ModelId).HasMaxLength(200).IsRequired();
        builder.Property(run => run.PromptVersion).HasMaxLength(100).IsRequired();
        builder.Property(run => run.GuidanceRevisionId);
        builder.Property(run => run.GuidanceRevisionNumber);
        builder.Property(run => run.GuidanceContentHash).HasMaxLength(64);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(run => run.EstimatedCostUsd).HasPrecision(10, 6);
        builder.Property(run => run.StructuredResultJson).HasMaxLength(8000);
        builder.Property(run => run.ErrorMessage).HasMaxLength(2000);
        builder.Property(run => run.StartedAt).IsRequired();
        builder.Property(run => run.CreatedAt).IsRequired();

        builder.HasIndex(run => new { run.CandidateId, run.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsAiRuns_CandidateId_StartedAt");

        builder.HasOne(run => run.Candidate)
            .WithMany(candidate => candidate.AiRuns)
            .HasForeignKey(run => run.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

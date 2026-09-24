using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PhotoSubmissionAuditLogEntityConfiguration : IEntityTypeConfiguration<PhotoSubmissionAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<PhotoSubmissionAuditLogEntity> builder)
    {
        builder.ToTable("PhotoSubmissionAuditLog");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);

        builder.HasIndex(log => new { log.PhotoSubmissionId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoSubmissionAuditLog_Submission_OccurredAt");

        builder.HasOne(log => log.Submission)
            .WithMany(submission => submission.AuditLogs)
            .HasForeignKey(log => log.PhotoSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPostReportAuditLogEntityConfiguration : IEntityTypeConfiguration<ForumPostReportAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<ForumPostReportAuditLogEntity> builder)
    {
        builder.ToTable("ForumPostReportAuditLog");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);
        builder.HasIndex(log => new { log.ReportId, log.OccurredAt })
            .IsDescending(false, true).HasDatabaseName("IX_ForumPostReportAuditLog_ReportId_OccurredAt");
    }
}

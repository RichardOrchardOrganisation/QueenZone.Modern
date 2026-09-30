using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PrivateMessageReportAuditLogEntityConfiguration : IEntityTypeConfiguration<PrivateMessageReportAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<PrivateMessageReportAuditLogEntity> builder)
    {
        builder.ToTable("PrivateMessageReportAuditLog");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);

        // No navigation/FK constraint to PrivateMessageReportEntity: this log must outlive
        // the report's retention-window purge (ADR 0015), so the relationship is
        // application-enforced only.
        builder.HasIndex(log => new { log.ReportId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PrivateMessageReportAuditLog_ReportId_OccurredAt");
    }
}

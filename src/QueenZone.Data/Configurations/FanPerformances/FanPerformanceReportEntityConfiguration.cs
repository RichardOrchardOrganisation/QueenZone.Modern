using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class FanPerformanceReportEntityConfiguration : IEntityTypeConfiguration<FanPerformanceReportEntity>
{
    public void Configure(EntityTypeBuilder<FanPerformanceReportEntity> builder)
    {
        builder.ToTable("FanPerformanceReports");
        builder.HasKey(report => report.Id);

        builder.Property(report => report.Reason)
            .HasMaxLength(FanPerformanceReportLimits.MaxReasonLength)
            .IsRequired();
        builder.Property(report => report.Status)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(report => report.CreatedAt).IsRequired();
        builder.Property(report => report.TitleSnapshot)
            .HasMaxLength(FanPerformanceReportLimits.MaxSnapshotLength);
        builder.Property(report => report.PerformedBySnapshot)
            .HasMaxLength(FanPerformanceReportLimits.MaxSnapshotLength);
        builder.Property(report => report.ReviewedBy).HasMaxLength(256);

        builder.HasIndex(report => new { report.ReporterMemberId, report.StageId })
            .IsUnique()
            .HasFilter("[Status] = 'Open'")
            .HasDatabaseName("UX_FanPerformanceReports_Reporter_Stage_Open");

        builder.HasIndex(report => new { report.Status, report.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_FanPerformanceReports_Status_CreatedAt");

        builder.HasOne(report => report.Reporter)
            .WithMany()
            .HasForeignKey(report => report.ReporterMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

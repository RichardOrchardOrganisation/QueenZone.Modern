using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPostReportEntityConfiguration : IEntityTypeConfiguration<ForumPostReportEntity>
{
    public void Configure(EntityTypeBuilder<ForumPostReportEntity> builder)
    {
        builder.ToTable("ForumPostReports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Category).HasMaxLength(100).IsRequired();
        builder.Property(report => report.Details).HasMaxLength(ForumPostReportLimits.MaxDetailsLength);
        builder.Property(report => report.Status).HasMaxLength(50).IsRequired();
        builder.Property(report => report.PostBodySnapshot).IsRequired();
        builder.Property(report => report.AuthorDisplayNameSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(report => report.ThreadTitleSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(report => report.CreatedAt).IsRequired();
        builder.Property(report => report.PostCreatedAtSnapshot).IsRequired();

        builder.HasIndex(report => new { report.ReporterMemberId, report.PostId })
            .IsUnique().HasDatabaseName("IX_ForumPostReports_Reporter_Post");
        builder.HasIndex(report => new { report.Status, report.CreatedAt })
            .IsDescending(false, true).HasDatabaseName("IX_ForumPostReports_Status_CreatedAt");
        builder.HasIndex(report => report.ReportedMemberId)
            .HasDatabaseName("IX_ForumPostReports_ReportedMember");

        builder.HasOne(report => report.Reporter).WithMany()
            .HasForeignKey(report => report.ReporterMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(report => report.Reported).WithMany()
            .HasForeignKey(report => report.ReportedMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

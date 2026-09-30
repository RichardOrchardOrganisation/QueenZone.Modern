using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAuditLogEntityConfiguration : IEntityTypeConfiguration<NewsAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<NewsAuditLogEntity> builder)
    {
        builder.ToTable("NewsAuditLog");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.NewsId).IsRequired();
        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);
        builder.Property(log => log.OccurredAt)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .IsRequired();

        builder.HasIndex(log => new { log.NewsId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NewsAuditLog_NewsId_OccurredAt");
    }
}

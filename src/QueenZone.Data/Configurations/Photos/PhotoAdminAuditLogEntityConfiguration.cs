using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PhotoAdminAuditLogEntityConfiguration : IEntityTypeConfiguration<PhotoAdminAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<PhotoAdminAuditLogEntity> builder)
    {
        builder.ToTable("PhotoAdminAuditLog");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2000);

        builder.HasIndex(log => new { log.PicId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PhotoAdminAuditLog_PicId_OccurredAt");
    }
}

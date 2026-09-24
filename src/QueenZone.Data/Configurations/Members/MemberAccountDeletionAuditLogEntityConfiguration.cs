using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberAccountDeletionAuditLogEntityConfiguration : IEntityTypeConfiguration<MemberAccountDeletionAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<MemberAccountDeletionAuditLogEntity> builder)
    {
        builder.ToTable("MemberAccountDeletionAuditLog");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Action).HasMaxLength(50).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.HasIndex(log => new { log.MemberAccountId, log.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_MemberAccountDeletionAuditLog_MemberAccountId_OccurredAt");
    }
}

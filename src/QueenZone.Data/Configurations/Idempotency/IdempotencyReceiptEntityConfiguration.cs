using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class IdempotencyReceiptEntityConfiguration : IEntityTypeConfiguration<IdempotencyReceiptEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyReceiptEntity> builder)
    {
        builder.ToTable("IdempotencyReceipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.OperationKind)
            .HasMaxLength(IdempotencyLimits.OperationKindMaxLength)
            .IsRequired();
        builder.Property(row => row.PayloadHash)
            .HasMaxLength(IdempotencyLimits.PayloadHashLength)
            .IsRequired();
        builder.Property(row => row.Location).HasMaxLength(IdempotencyLimits.LocationMaxLength);
        builder.Property(row => row.ResponseBodyJson).IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.ExpiresAt).IsRequired();
        builder.HasIndex(row => new { row.MemberId, row.OperationKind, row.OperationId })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyReceipts_Member_Kind_Operation");
        builder.HasIndex(row => row.ExpiresAt)
            .HasDatabaseName("IX_IdempotencyReceipts_ExpiresAt");
    }
}

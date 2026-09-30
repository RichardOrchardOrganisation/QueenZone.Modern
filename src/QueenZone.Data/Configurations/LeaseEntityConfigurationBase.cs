using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

/// <summary>Shared lease-table shape. Each lease keeps its own table; only the mapping helper is shared.</summary>
public abstract class LeaseEntityConfigurationBase<TEntity>(string tableName) : IEntityTypeConfiguration<TEntity>
    where TEntity : class, ILeaseEntity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(tableName);
        builder.HasKey(nameof(ILeaseEntity.LeaseName));

        builder.Property(nameof(ILeaseEntity.LeaseName)).HasMaxLength(100).IsRequired();
        builder.Property(nameof(ILeaseEntity.HolderId)).HasMaxLength(64).IsRequired();
        builder.Property(nameof(ILeaseEntity.AcquiredAtUtc)).IsRequired();
        builder.Property(nameof(ILeaseEntity.ExpiresAtUtc)).IsRequired();
    }
}

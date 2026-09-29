using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

/// <summary>Shared run-request table shape. Each queue keeps its own table, indexes and migration history.</summary>
public abstract class RunRequestEntityConfigurationBase<TEntity>(string tableName) : IEntityTypeConfiguration<TEntity>
    where TEntity : class, IRunRequestEntity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(tableName);
        builder.HasKey(nameof(IRunRequestEntity.Id));

        builder.Property(nameof(IRunRequestEntity.RequestedBy)).HasMaxLength(256).IsRequired();
        builder.Property(nameof(IRunRequestEntity.RequestedAtUtc)).IsRequired();
        builder.Property(nameof(IRunRequestEntity.RunnerId)).HasMaxLength(100);
        builder.Property(nameof(IRunRequestEntity.Summary)).HasMaxLength(2000);
        builder.Property(nameof(IRunRequestEntity.ErrorMessage)).HasMaxLength(2000);
        builder.Property(nameof(IRunRequestEntity.ActiveKey)).HasMaxLength(20);
        builder.Property(nameof(IRunRequestEntity.UpdatedAtUtc)).IsRequired();

        builder.HasIndex(nameof(IRunRequestEntity.ActiveKey))
            .IsUnique()
            .HasFilter("[ActiveKey] IS NOT NULL")
            .HasDatabaseName($"UX_{tableName}_ActiveKey");
        builder.HasIndex("Status", nameof(IRunRequestEntity.RequestedAtUtc))
            .HasDatabaseName($"IX_{tableName}_Status_RequestedAtUtc");

        ConfigureStatus(builder);
        ConfigureQueueSpecific(builder);
    }

    protected abstract void ConfigureStatus(EntityTypeBuilder<TEntity> builder);

    protected virtual void ConfigureQueueSpecific(EntityTypeBuilder<TEntity> builder)
    {
    }
}

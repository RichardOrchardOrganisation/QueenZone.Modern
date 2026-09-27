using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class SearchReindexLeaseEntityConfiguration : IEntityTypeConfiguration<SearchReindexLeaseEntity>
{
    public void Configure(EntityTypeBuilder<SearchReindexLeaseEntity> builder)
    {
        builder.ToTable("SearchReindexLeases");
        builder.HasKey(lease => lease.LeaseName);

        builder.Property(lease => lease.LeaseName).HasMaxLength(100).IsRequired();
        builder.Property(lease => lease.HolderId).HasMaxLength(64).IsRequired();
        builder.Property(lease => lease.AcquiredAtUtc).IsRequired();
        builder.Property(lease => lease.ExpiresAtUtc).IsRequired();
    }
}

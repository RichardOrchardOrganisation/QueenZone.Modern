using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentRunLeaseEntityConfiguration : IEntityTypeConfiguration<NewsAgentRunLeaseEntity>
{
    public void Configure(EntityTypeBuilder<NewsAgentRunLeaseEntity> builder)
    {
        builder.ToTable("NewsAgentRunLeases");
        builder.HasKey(lease => lease.LeaseName);

        builder.Property(lease => lease.LeaseName).HasMaxLength(100).IsRequired();
        builder.Property(lease => lease.HolderId).HasMaxLength(64).IsRequired();
        builder.Property(lease => lease.AcquiredAtUtc).IsRequired();
        builder.Property(lease => lease.ExpiresAtUtc).IsRequired();
    }
}

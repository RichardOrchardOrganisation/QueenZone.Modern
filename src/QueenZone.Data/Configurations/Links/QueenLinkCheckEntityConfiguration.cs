using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QueenLinkCheckEntityConfiguration : IEntityTypeConfiguration<QueenLinkCheckEntity>
{
    public void Configure(EntityTypeBuilder<QueenLinkCheckEntity> builder)
    {
        builder.ToTable("QueenLinkChecks");
        builder.HasKey(check => check.QueenFeaturedSiteId);
        builder.Property(check => check.QueenFeaturedSiteId).ValueGeneratedNever();
        builder.Property(check => check.Url).HasMaxLength(500).IsRequired();
        builder.Property(check => check.LastCheckedAtUtc).IsRequired();
        builder.Property(check => check.IsAvailable).IsRequired();
        builder.Property(check => check.IsConfirmedDead).IsRequired();
        builder.Property(check => check.ConsecutiveFailureCount).IsRequired();
        builder.Property(check => check.LastError).HasMaxLength(500);
        builder.HasIndex(check => check.IsConfirmedDead);
        builder.HasIndex(check => check.LastCheckedAtUtc);
    }
}

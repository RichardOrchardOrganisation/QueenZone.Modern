using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsDiscoverySourceEntityConfiguration : IEntityTypeConfiguration<NewsDiscoverySourceEntity>
{
    public void Configure(EntityTypeBuilder<NewsDiscoverySourceEntity> builder)
    {
        builder.ToTable("NewsDiscoverySources");
        builder.HasKey(source => source.Id);

        builder.Property(source => source.Key).HasMaxLength(100).IsRequired();
        builder.Property(source => source.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(source => source.HomepageUrl).HasMaxLength(2000).IsRequired();
        builder.Property(source => source.FeedOrSiteUrl).HasMaxLength(2000);
        builder.Property(source => source.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(source => source.TrustTier).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(source => source.RelevanceKeywords).HasMaxLength(1000);
        builder.Property(source => source.CreatedAt).IsRequired();
        builder.Property(source => source.UpdatedAt).IsRequired();

        builder.HasIndex(source => source.Key)
            .IsUnique()
            .HasDatabaseName("IX_NewsDiscoverySources_Key");
    }
}

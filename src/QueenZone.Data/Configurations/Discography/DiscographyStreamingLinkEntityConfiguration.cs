using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class DiscographyStreamingLinkEntityConfiguration : IEntityTypeConfiguration<DiscographyStreamingLinkEntity>
{
    public void Configure(EntityTypeBuilder<DiscographyStreamingLinkEntity> builder)
    {
        builder.ToTable("DiscographyStreamingLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Provider)
            .HasConversion(provider => provider.Key(), key => StreamingProviders.FromKey(key))
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(link => link.ExternalId).HasMaxLength(StreamingLinkUrl.MaxExternalIdLength).IsUnicode(false).IsRequired();
        builder.Property(link => link.Url).HasMaxLength(StreamingLinkUrl.MaxUrlLength).IsUnicode(false).IsRequired();
        builder.Property(link => link.Source)
            .HasConversion(source => source.Key(), key => StreamingProviders.SourceFromKey(key))
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(link => link.UpdatedAtUtc).IsRequired();
        builder.Property(link => link.UpdatedBy).HasMaxLength(100);

        // One link per provider for each album (album-level rows) and for each track. Track ids are
        // unique across albums, so the track index does not need AlbumId.
        builder.HasIndex(link => new { link.AlbumId, link.Provider })
            .IsUnique()
            .HasFilter("[AlbumSongId] IS NULL")
            .HasDatabaseName("IX_DiscographyStreamingLinks_Album_Provider");
        builder.HasIndex(link => new { link.AlbumSongId, link.Provider })
            .IsUnique()
            .HasFilter("[AlbumSongId] IS NOT NULL")
            .HasDatabaseName("IX_DiscographyStreamingLinks_Track_Provider");

        // Public album reads load album and track links together by AlbumId.
        builder.HasIndex(link => link.AlbumId);
    }
}

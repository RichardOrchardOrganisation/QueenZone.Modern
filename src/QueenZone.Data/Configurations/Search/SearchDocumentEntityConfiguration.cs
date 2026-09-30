using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class SearchDocumentEntityConfiguration : IEntityTypeConfiguration<SearchDocumentEntity>
{
    public void Configure(EntityTypeBuilder<SearchDocumentEntity> builder)
    {
        builder.ToTable("SearchDocument");
        builder.HasKey(document => document.Id);

        builder.Property(document => document.SourceKey).HasMaxLength(200).IsRequired();
        builder.Property(document => document.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(300).IsRequired();
        builder.Property(document => document.Body).IsRequired();
        builder.Property(document => document.Summary).HasMaxLength(500);
        builder.Property(document => document.Url).HasMaxLength(500).IsRequired();
        builder.Property(document => document.ImageUrl).HasMaxLength(512);
        builder.Property(document => document.Category).HasMaxLength(200);
        builder.Property(document => document.AuthorDisplayName).HasMaxLength(256);
        builder.Property(document => document.IndexedAt).IsRequired();

        builder.HasIndex(document => document.SourceKey)
            .IsUnique()
            .HasDatabaseName("UQ_SearchDocument_SourceKey");

        builder.HasIndex(document => new { document.ContentType, document.PublishedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SearchDocument_ContentType_PublishedAt");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsTableRowConfiguration : IEntityTypeConfiguration<NewsTableRow>
{
    public void Configure(EntityTypeBuilder<NewsTableRow> builder)
    {
        builder.ToTable("NEWS_T", table => table.ExcludeFromMigrations());
        builder.HasKey(row => row.NewsId);

        builder.Property(row => row.NewsId)
            .HasColumnName("NEWS_ID")
            .ValueGeneratedOnAdd();
        builder.Property(row => row.Title).HasColumnName("TITLE").HasMaxLength(150);
        builder.Property(row => row.Excerpt).HasColumnName("EXCERPT").HasMaxLength(NewsValidation.MaxExcerptLength);
        builder.Property(row => row.Body).HasColumnName("ARTICLE");
        builder.Property(row => row.PublishedAt).HasColumnName("DATE");
        builder.Property(row => row.SourceUrl).HasColumnName("SOURCE_URL").HasMaxLength(NewsValidation.MaxSourceUrlLength);
        builder.Property(row => row.Slug).HasColumnName("SLUG").HasMaxLength(200);
        builder.Property(row => row.CreatedAt).HasColumnName("CREATED_AT");
        builder.Property(row => row.UpdatedAt).HasColumnName("UPDATED_AT");
        builder.Property(row => row.EditorEmail).HasColumnName("EDITOR_EMAIL").HasMaxLength(256);
        builder.Property(row => row.UserId).HasColumnName("USER_ID");
        builder.Property(row => row.Type).HasColumnName("TYPE");
        builder.Property(row => row.QueenOnline).HasColumnName("QUEEN_ONLINE");
        builder.Property(row => row.ImageBlobKey)
            .HasColumnName("IMAGE_BLOB_KEY")
            .HasMaxLength(NewsValidation.MaxImageBlobKeyLength);
        builder.Property(row => row.ImageGalleryPicId).HasColumnName("IMAGE_GALLERY_PIC_ID");
        builder.Property(row => row.ForumTopicId).HasColumnName("FORUM_TOPIC_ID");
        builder.Property(row => row.IsPublished)
            .HasColumnName("DISPLAY")
            .HasConversion(
                value => value ? 1 : 0,
                value => value == 1);
    }
}

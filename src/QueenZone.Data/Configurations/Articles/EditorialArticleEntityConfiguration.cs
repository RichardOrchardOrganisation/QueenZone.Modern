using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class EditorialArticleEntityConfiguration : IEntityTypeConfiguration<EditorialArticleEntity>
{
    public void Configure(EntityTypeBuilder<EditorialArticleEntity> builder)
    {
        builder.ToTable("EditorialArticles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Excerpt).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Tags).HasMaxLength(500);
        builder.Property(x => x.Source).HasMaxLength(2000);
        builder.Property(x => x.ImageBlobKey).HasMaxLength(1000);
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(320).IsRequired();
        builder.Property(x => x.LiveTitle).HasMaxLength(300);
        builder.Property(x => x.LiveSlug).HasMaxLength(300);
        builder.Property(x => x.LiveExcerpt).HasMaxLength(500);
        builder.Property(x => x.LiveWordCount).IsRequired();
        builder.Property(x => x.LiveAuthorName).HasMaxLength(200);
        builder.Property(x => x.LiveCategory).HasMaxLength(100);
        builder.Property(x => x.LiveTags).HasMaxLength(500);
        builder.Property(x => x.LiveSource).HasMaxLength(2000);
        builder.Property(x => x.LiveImageBlobKey).HasMaxLength(1000);
        builder.HasIndex(x => x.LegacyArticleId).IsUnique().HasFilter("[LegacyArticleId] IS NOT NULL");
        builder.HasIndex(x => x.SourceSubmissionId).IsUnique().HasFilter("[SourceSubmissionId] IS NOT NULL");
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.LiveSlug).IsUnique().HasFilter("[LiveSlug] IS NOT NULL");
    }
}

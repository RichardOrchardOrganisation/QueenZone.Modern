using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPostAttachmentEntityConfiguration : IEntityTypeConfiguration<ForumPostAttachmentEntity>
{
    public void Configure(EntityTypeBuilder<ForumPostAttachmentEntity> builder)
    {
        builder.ToTable("ForumPostAttachments");
        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(attachment => attachment.BlobPath).HasMaxLength(512).IsRequired();
        builder.Property(attachment => attachment.ContainerName).HasMaxLength(64).IsRequired();
        builder.Property(attachment => attachment.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(attachment => attachment.UploadedAt).IsRequired();
        builder.Property(attachment => attachment.DownloadCount).HasDefaultValue(0);

        builder.HasIndex(attachment => attachment.LegacyPostId)
            .HasDatabaseName("IX_ForumPostAttachments_LegacyPostId");
        builder.HasIndex(attachment => attachment.PostId)
            .HasDatabaseName("IX_ForumPostAttachments_PostId");

        // ModernForumPost is excluded from EF migrations; SQL Server migration adds the FK in SQL.
        builder.HasOne(attachment => attachment.Post)
            .WithMany()
            .HasForeignKey(attachment => attachment.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

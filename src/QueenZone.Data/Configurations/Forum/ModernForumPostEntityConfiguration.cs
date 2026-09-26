using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ModernForumPostEntityConfiguration : IEntityTypeConfiguration<ModernForumPostEntity>
{
    public void Configure(EntityTypeBuilder<ModernForumPostEntity> builder)
    {
        builder.ToTable("ModernForumPost", table =>
        {
            table.ExcludeFromMigrations();
            table.HasTrigger("TR_ModernForumPost_RefreshArchiveAuthorSummary");
        });
        builder.HasKey(post => post.Id);
        builder.Property(post => post.AuthorDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(post => post.BodyHtml).IsUnicode().HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(post => post.SignatureHtml).IsUnicode().HasColumnType("nvarchar(max)");
        builder.Property(post => post.Attachment).HasMaxLength(120).IsUnicode(false);
        builder.Property(post => post.FileSize).HasMaxLength(12).IsUnicode(false);
        builder.Property(post => post.EditCount).HasDefaultValue(0);
        builder.Property(post => post.IsHidden).IsRequired().HasDefaultValue(false);
        builder.Property(post => post.UpdatedAt).IsConcurrencyToken();
        builder.HasIndex(post => post.LegacyPostId)
            .IsUnique()
            .HasDatabaseName("UQ_ModernForumPost_LegacyPostId");
        builder.HasIndex(post => new { post.AuthorMemberId, post.PostedAt })
            .HasDatabaseName("IX_ModernForumPost_AuthorMemberId_PostedAt");
        builder.HasIndex(post => new { post.AuthorLegacyUserId, post.PostedAt, post.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_ModernForumPost_AuthorLegacyUserId_PostedAt");
        builder.HasIndex(post => post.PostedAt)
            .HasDatabaseName("IX_ModernForumPost_PostedAt_Visible")
            .HasFilter("[IsHidden] = 0");
        builder.HasOne(post => post.Thread)
            .WithMany()
            .HasForeignKey(post => post.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

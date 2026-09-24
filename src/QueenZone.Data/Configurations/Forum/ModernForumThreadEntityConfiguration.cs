using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ModernForumThreadEntityConfiguration : IEntityTypeConfiguration<ModernForumThreadEntity>
{
    public void Configure(EntityTypeBuilder<ModernForumThreadEntity> builder)
    {
        builder.ToTable("ModernForumThread", table => table.ExcludeFromMigrations());
        builder.HasKey(thread => thread.Id);
        builder.Property(thread => thread.Title).HasMaxLength(200).IsRequired();
        builder.Property(thread => thread.StartedByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(thread => thread.IsHidden).IsRequired().HasDefaultValue(false);
        builder.Property(thread => thread.StarterAttachment).HasMaxLength(120);
        builder.Property(thread => thread.StarterFileSize).HasMaxLength(12);
        builder.HasIndex(thread => thread.LegacyTopicId)
            .IsUnique()
            .HasDatabaseName("UQ_ModernForumThread_LegacyTopicId");
        builder.HasOne(thread => thread.Category)
            .WithMany()
            .HasForeignKey(thread => thread.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

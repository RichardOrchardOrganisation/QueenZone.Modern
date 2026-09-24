using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ModernForumCategoryEntityConfiguration : IEntityTypeConfiguration<ModernForumCategoryEntity>
{
    public void Configure(EntityTypeBuilder<ModernForumCategoryEntity> builder)
    {
        builder.ToTable("ModernForumCategory", table => table.ExcludeFromMigrations());
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Name).HasMaxLength(100).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(400);
        builder.HasIndex(category => category.LegacyForumId)
            .IsUnique()
            .HasDatabaseName("UQ_ModernForumCategory_LegacyForumId");
    }
}

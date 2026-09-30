using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NotificationPreferenceEntityConfiguration : IEntityTypeConfiguration<NotificationPreferenceEntity>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceEntity> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(row => new { row.MemberAccountId, row.Category });

        builder.Property(row => row.Category).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(row => row.IsEnabled).IsRequired();
        builder.Property(row => row.UpdatedAt).IsRequired();

        builder.HasIndex(row => new { row.Category, row.IsEnabled })
            .HasDatabaseName("IX_NotificationPreferences_Category_IsEnabled");

        builder.HasOne(row => row.MemberAccount)
            .WithMany()
            .HasForeignKey(row => row.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

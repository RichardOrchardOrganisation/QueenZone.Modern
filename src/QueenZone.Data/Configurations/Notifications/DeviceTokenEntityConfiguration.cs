using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class DeviceTokenEntityConfiguration : IEntityTypeConfiguration<DeviceTokenEntity>
{
    public void Configure(EntityTypeBuilder<DeviceTokenEntity> builder)
    {
        builder.ToTable("DeviceTokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.DeviceId).HasMaxLength(200).IsRequired();
        builder.Property(token => token.Platform).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(token => token.Token).HasMaxLength(4000).IsRequired();
        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.UpdatedAt).IsRequired();

        builder.HasIndex(token => token.DeviceId)
            .IsUnique()
            .HasDatabaseName("IX_DeviceTokens_DeviceId");

        builder.HasIndex(token => token.MemberAccountId)
            .HasDatabaseName("IX_DeviceTokens_MemberAccountId");

        builder.HasOne(token => token.MemberAccount)
            .WithMany()
            .HasForeignKey(token => token.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

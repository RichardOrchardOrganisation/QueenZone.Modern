using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberSocialLinkEntityConfiguration : IEntityTypeConfiguration<MemberSocialLinkEntity>
{
    public void Configure(EntityTypeBuilder<MemberSocialLinkEntity> builder)
    {
        builder.ToTable("MemberSocialLinks");
        builder.HasKey(row => new { row.MemberId, row.Channel });

        builder.Property(row => row.Channel).HasMaxLength(20).IsRequired();
        builder.Property(row => row.Url).HasMaxLength(MemberSocialLinkUrl.MaxUrlLength).IsRequired();

        builder.HasOne(row => row.Member)
            .WithMany()
            .HasForeignKey(row => row.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

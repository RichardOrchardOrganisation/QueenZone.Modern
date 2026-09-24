using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberFollowEntityConfiguration : IEntityTypeConfiguration<MemberFollowEntity>
{
    public void Configure(EntityTypeBuilder<MemberFollowEntity> builder)
    {
        builder.ToTable("MemberFollows");
        builder.HasKey(follow => follow.Id);

        builder.Property(follow => follow.CreatedAt).IsRequired();

        builder.HasIndex(follow => new { follow.FollowerMemberId, follow.FollowedMemberId })
            .IsUnique()
            .HasDatabaseName("IX_MemberFollows_Follower_Followed");

        builder.HasIndex(follow => follow.FollowedMemberId)
            .HasDatabaseName("IX_MemberFollows_Followed");

        builder.HasOne(follow => follow.Follower)
            .WithMany()
            .HasForeignKey(follow => follow.FollowerMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(follow => follow.Followed)
            .WithMany()
            .HasForeignKey(follow => follow.FollowedMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

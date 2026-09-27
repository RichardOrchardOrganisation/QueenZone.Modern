using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberMessageBlockEntityConfiguration : IEntityTypeConfiguration<MemberMessageBlockEntity>
{
    public void Configure(EntityTypeBuilder<MemberMessageBlockEntity> builder)
    {
        builder.ToTable("MemberMessageBlocks");
        builder.HasKey(block => block.Id);

        builder.Property(block => block.CreatedAt).IsRequired();

        builder.HasIndex(block => new { block.BlockerMemberId, block.BlockedMemberId })
            .IsUnique()
            .HasDatabaseName("IX_MemberMessageBlocks_Blocker_Blocked");

        builder.HasIndex(block => block.BlockedMemberId)
            .HasDatabaseName("IX_MemberMessageBlocks_Blocked");

        builder.HasOne(block => block.Blocker)
            .WithMany()
            .HasForeignKey(block => block.BlockerMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(block => block.Blocked)
            .WithMany()
            .HasForeignKey(block => block.BlockedMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

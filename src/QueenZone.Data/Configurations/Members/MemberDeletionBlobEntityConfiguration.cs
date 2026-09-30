using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberDeletionBlobEntityConfiguration : IEntityTypeConfiguration<MemberDeletionBlobEntity>
{
    public void Configure(EntityTypeBuilder<MemberDeletionBlobEntity> builder)
    {
        builder.ToTable("MemberDeletionBlobs");
        builder.HasKey(blob => blob.Id);
        builder.Property(blob => blob.MemberAccountId).IsRequired();
        builder.Property(blob => blob.Container).HasMaxLength(100).IsRequired();
        builder.Property(blob => blob.BlobPath).HasMaxLength(512).IsRequired();
        builder.Property(blob => blob.CreatedAt).IsRequired();
        builder.HasIndex(blob => blob.CreatedAt);
        builder.HasIndex(blob => blob.MemberAccountId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MobileAuthRefreshTokenEntityConfiguration : IEntityTypeConfiguration<MobileAuthRefreshTokenEntity>
{
    public void Configure(EntityTypeBuilder<MobileAuthRefreshTokenEntity> builder)
    {
        builder.ToTable("MobileAuthRefreshTokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.ClientId).HasMaxLength(100).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();
        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_MobileAuthRefreshTokens_TokenHash");

        builder.HasIndex(token => new { token.MemberAccountId, token.RevokedAt })
            .HasDatabaseName("IX_MobileAuthRefreshTokens_Member_Revoked");

        builder.HasOne(token => token.MemberAccount)
            .WithMany()
            .HasForeignKey(token => token.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

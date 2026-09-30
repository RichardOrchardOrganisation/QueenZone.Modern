using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MobileAuthAuthorizationCodeEntityConfiguration : IEntityTypeConfiguration<MobileAuthAuthorizationCodeEntity>
{
    public void Configure(EntityTypeBuilder<MobileAuthAuthorizationCodeEntity> builder)
    {
        builder.ToTable("MobileAuthAuthorizationCodes");
        builder.HasKey(code => code.Id);

        builder.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(code => code.ClientId).HasMaxLength(100).IsRequired();
        builder.Property(code => code.RedirectUri).HasMaxLength(500).IsRequired();
        builder.Property(code => code.CodeChallenge).HasMaxLength(128).IsRequired();
        builder.Property(code => code.ExpiresAt).IsRequired();
        builder.Property(code => code.CreatedAt).IsRequired();

        builder.HasIndex(code => code.CodeHash)
            .IsUnique()
            .HasDatabaseName("IX_MobileAuthAuthorizationCodes_CodeHash");

        builder.HasOne(code => code.MemberAccount)
            .WithMany()
            .HasForeignKey(code => code.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberExternalLoginConfiguration : IEntityTypeConfiguration<MemberExternalLogin>
{
    public void Configure(EntityTypeBuilder<MemberExternalLogin> builder)
    {
        builder.ToTable("MemberExternalLogins");
        builder.HasKey(login => login.Id);

        builder.Property(login => login.Provider).HasMaxLength(50).IsRequired();
        builder.Property(login => login.ProviderKey).HasMaxLength(256).IsRequired();
        builder.Property(login => login.Email).HasMaxLength(256).IsRequired();
        builder.Property(login => login.LinkedAt).IsRequired();
        builder.Property(login => login.AppleRefreshTokenProtected);

        builder.HasIndex(login => new { login.Provider, login.ProviderKey })
            .IsUnique()
            .HasDatabaseName("IX_MemberExternalLogins_Provider_ProviderKey");

        builder.HasOne<MemberAccount>()
            .WithMany()
            .HasForeignKey(login => login.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

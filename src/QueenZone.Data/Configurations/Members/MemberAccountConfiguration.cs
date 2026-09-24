using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberAccountConfiguration : IEntityTypeConfiguration<MemberAccount>
{
    public void Configure(EntityTypeBuilder<MemberAccount> builder)
    {
        builder.ToTable("MemberAccounts");
        builder.HasKey(account => account.Id);

        builder.Property(account => account.Email).HasMaxLength(256).IsRequired();
        builder.Property(account => account.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(account => account.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(account => account.AvatarUrl).HasMaxLength(512);
        builder.Property(account => account.PasswordHash).HasMaxLength(512);
        builder.Property(account => account.PasswordFailureCount).IsRequired().HasDefaultValue(0);
        builder.Property(account => account.PasswordFailureWindowStartedAt);
        builder.Property(account => account.CreatedAt).IsRequired();
        builder.Property(account => account.LastLoginAt);
        builder.Property(account => account.MessagePrivacy)
            .HasConversion<byte>()
            .IsRequired()
            .HasDefaultValue(MemberMessagePrivacy.Members);
        builder.Property(account => account.IsSuspended).IsRequired().HasDefaultValue(false);
        builder.Property(account => account.SuspendedAt);
        builder.Property(account => account.SuspendedReason).HasMaxLength(1000);
        builder.Property(account => account.SuspendedByAdminEmail).HasMaxLength(256);
        builder.Property(account => account.DeletionRequestedAt);
        builder.Property(account => account.DeletionRecoveryDisplayName).HasMaxLength(100);
        builder.Property(account => account.DeletionRecoveryAvatarUrl).HasMaxLength(512);
        builder.Property(account => account.PersonalDataPurgedAt);

        builder.HasIndex(account => account.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_MemberAccounts_NormalizedEmail");

        builder.HasIndex(account => account.IsSuspended)
            .HasDatabaseName("IX_MemberAccounts_IsSuspended");

        builder.HasIndex(account => new { account.DeletionRequestedAt, account.PersonalDataPurgedAt })
            .HasDatabaseName("IX_MemberAccounts_DeletionRequestedAt_PersonalDataPurgedAt");
    }
}

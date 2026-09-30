using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class HelpRequestEntityConfiguration : IEntityTypeConfiguration<HelpRequestEntity>
{
    public void Configure(EntityTypeBuilder<HelpRequestEntity> builder)
    {
        builder.ToTable("HelpRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Topic).HasMaxLength(50).IsRequired();
        builder.Property(request => request.Subject).HasMaxLength(200).IsRequired();
        builder.Property(request => request.Message).HasMaxLength(4000).IsRequired();
        builder.Property(request => request.Name).HasMaxLength(100).IsRequired();
        builder.Property(request => request.Email).HasMaxLength(256).IsRequired();
        builder.Property(request => request.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(request => request.Status).HasMaxLength(50).IsRequired();
        builder.Property(request => request.SubmittedAt).IsRequired();
        builder.Property(request => request.ReviewerEmail).HasMaxLength(256);
        builder.Property(request => request.ReviewNotes).HasMaxLength(500);

        builder.HasIndex(request => new { request.Status, request.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_HelpRequests_Status_SubmittedAt");

        builder.HasIndex(request => new { request.NormalizedEmail, request.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_HelpRequests_NormalizedEmail_SubmittedAt");

        builder.HasIndex(request => new { request.MemberId, request.SubmittedAt })
            .IsDescending(false, true)
            .HasFilter("[MemberId] IS NOT NULL")
            .HasDatabaseName("IX_HelpRequests_Member_SubmittedAt");

        builder.HasOne(request => request.Member)
            .WithMany()
            .HasForeignKey(request => request.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

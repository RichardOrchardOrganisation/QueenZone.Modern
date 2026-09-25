using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PrivateMessageReportEntityConfiguration : IEntityTypeConfiguration<PrivateMessageReportEntity>
{
    public void Configure(EntityTypeBuilder<PrivateMessageReportEntity> builder)
    {
        builder.ToTable("PrivateMessageReports");
        builder.HasKey(report => report.Id);

        builder.Property(report => report.Reason)
            .HasMaxLength(PrivateMessageLimits.MaxReportReasonLength);
        builder.Property(report => report.Status)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(report => report.MessageBodySnapshot)
            .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
            .IsRequired();
        builder.Property(report => report.SenderDisplayNameSnapshot)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(report => report.CreatedAt).IsRequired();
        builder.Property(report => report.MessageCreatedAtSnapshot).IsRequired();
        builder.Property(report => report.MessageSortKeySnapshot).IsRequired();

        builder.HasIndex(report => new { report.ReporterMemberId, report.MessageId })
            .IsUnique()
            .HasDatabaseName("IX_PrivateMessageReports_Reporter_Message");

        builder.HasIndex(report => new { report.Status, report.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_PrivateMessageReports_Status_CreatedAt");

        builder.HasIndex(report => report.ConversationId)
            .HasDatabaseName("IX_PrivateMessageReports_Conversation");

        builder.HasOne(report => report.Message)
            .WithMany()
            .HasForeignKey(report => report.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(report => report.Conversation)
            .WithMany()
            .HasForeignKey(report => report.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(report => report.Reporter)
            .WithMany()
            .HasForeignKey(report => report.ReporterMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(report => report.Reported)
            .WithMany()
            .HasForeignKey(report => report.ReportedMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

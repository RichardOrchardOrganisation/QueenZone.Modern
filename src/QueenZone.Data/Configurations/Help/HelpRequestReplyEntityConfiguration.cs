using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class HelpRequestReplyEntityConfiguration : IEntityTypeConfiguration<HelpRequestReplyEntity>
{
    public void Configure(EntityTypeBuilder<HelpRequestReplyEntity> builder)
    {
        builder.ToTable("HelpRequestReplies");
        builder.HasKey(reply => reply.Id);

        builder.Property(reply => reply.ToEmail).HasMaxLength(256).IsRequired();
        builder.Property(reply => reply.Subject).HasMaxLength(200).IsRequired();
        builder.Property(reply => reply.Body).HasMaxLength(HelpRequestReply.MaxBodyLength).IsRequired();
        builder.Property(reply => reply.SentByEmail).HasMaxLength(256).IsRequired();
        builder.Property(reply => reply.SentAt).IsRequired();

        builder.HasIndex(reply => new { reply.HelpRequestId, reply.SentAt })
            .HasDatabaseName("IX_HelpRequestReplies_HelpRequest_SentAt");

        builder.HasOne(reply => reply.HelpRequest)
            .WithMany()
            .HasForeignKey(reply => reply.HelpRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PrivateMessageEntityConfiguration : IEntityTypeConfiguration<PrivateMessageEntity>
{
    public void Configure(EntityTypeBuilder<PrivateMessageEntity> builder)
    {
        builder.ToTable("PrivateMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Body)
            .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
            .IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.SortKey)
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt })
            .HasDatabaseName("IX_PrivateMessages_Conversation_CreatedAt");
        builder.HasIndex(message => new { message.ConversationId, message.SortKey })
            .HasDatabaseName("IX_PrivateMessages_Conversation_SortKey");
        builder.HasIndex(message => new { message.SenderMemberId, message.CreatedAt })
            .HasDatabaseName("IX_PrivateMessages_Sender_CreatedAt");

        builder.HasOne(message => message.Conversation)
            .WithMany(conversation => conversation.Messages)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(message => message.Sender)
            .WithMany()
            .HasForeignKey(message => message.SenderMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

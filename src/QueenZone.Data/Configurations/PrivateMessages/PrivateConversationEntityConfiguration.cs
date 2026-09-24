using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PrivateConversationEntityConfiguration : IEntityTypeConfiguration<PrivateConversationEntity>
{
    public void Configure(EntityTypeBuilder<PrivateConversationEntity> builder)
    {
        builder.ToTable("PrivateConversations");
        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.LastMessagePreview)
            .HasMaxLength(PrivateMessageLimits.PreviewLength)
            .IsRequired();
        builder.Property(conversation => conversation.CreatedAt).IsRequired();
        builder.Property(conversation => conversation.LastMessageAt).IsRequired();
        builder.Property(conversation => conversation.LastMessageSortKey).IsRequired();

        builder.HasIndex(conversation => new { conversation.MemberLowId, conversation.MemberHighId })
            .IsUnique()
            .HasDatabaseName("IX_PrivateConversations_MemberPair");

        builder.HasIndex(conversation => conversation.LastMessageSortKey)
            .IsDescending()
            .HasDatabaseName("IX_PrivateConversations_LastMessageSortKey");

        builder.HasOne(conversation => conversation.MemberLow)
            .WithMany()
            .HasForeignKey(conversation => conversation.MemberLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(conversation => conversation.MemberHigh)
            .WithMany()
            .HasForeignKey(conversation => conversation.MemberHighId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

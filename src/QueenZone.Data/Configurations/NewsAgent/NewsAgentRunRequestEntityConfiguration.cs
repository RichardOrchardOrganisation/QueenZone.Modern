using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentRunRequestEntityConfiguration()
    : RunRequestEntityConfigurationBase<NewsAgentRunRequestEntity>("NewsAgentRunRequests")
{
    protected override void ConfigureStatus(EntityTypeBuilder<NewsAgentRunRequestEntity> builder) =>
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

    protected override void ConfigureQueueSpecific(EntityTypeBuilder<NewsAgentRunRequestEntity> builder)
    {
        builder.Property(request => request.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(request => request.ArticleUrl).HasMaxLength(2000);
        builder.Property(request => request.GenerateDraft).IsRequired();
    }
}

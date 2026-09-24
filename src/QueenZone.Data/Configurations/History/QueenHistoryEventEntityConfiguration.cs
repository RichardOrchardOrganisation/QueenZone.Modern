using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QueenHistoryEventEntityConfiguration : IEntityTypeConfiguration<QueenHistoryEventEntity>
{
    private readonly bool sqlServer;

    public QueenHistoryEventEntityConfiguration(bool sqlServer)
    {
        this.sqlServer = sqlServer;
    }

    public void Configure(EntityTypeBuilder<QueenHistoryEventEntity> builder)
    {
        builder.ToTable("QueenHistoryEvents");
        builder.HasKey(historyEvent => historyEvent.Id);

        builder.Property(historyEvent => historyEvent.Title).HasMaxLength(200).IsRequired();
        builder.Property(historyEvent => historyEvent.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(historyEvent => historyEvent.EventDate).IsRequired();
        builder.Property(historyEvent => historyEvent.EventMonthDay)
            .HasComputedColumnSql(sqlServer
                ? "MONTH([EventDate]) * 100 + DAY([EventDate])"
                : "CAST(strftime('%m', EventDate) AS INTEGER) * 100 + CAST(strftime('%d', EventDate) AS INTEGER)", stored: true);
        builder.Property(historyEvent => historyEvent.DatePrecision).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(historyEvent => historyEvent.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(historyEvent => historyEvent.Importance).IsRequired();
        builder.Property(historyEvent => historyEvent.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(historyEvent => historyEvent.SourceKey).HasMaxLength(200).IsRequired();
        builder.Property(historyEvent => historyEvent.SourceUrl).HasMaxLength(2000);
        builder.Property(historyEvent => historyEvent.IsPublished).IsRequired();
        builder.Property(historyEvent => historyEvent.CreatedAt).IsRequired();
        builder.Property(historyEvent => historyEvent.UpdatedAt).IsRequired();
        if (sqlServer)
        {
            builder.Property(historyEvent => historyEvent.RowVersion).IsRowVersion();
        }
        else
        {
            builder.Property(historyEvent => historyEvent.RowVersion)
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedNever();
        }

        builder.HasIndex(historyEvent => new { historyEvent.IsPublished, historyEvent.DatePrecision, historyEvent.EventDate })
            .HasDatabaseName("IX_QueenHistoryEvents_Published_Date");

        builder.HasIndex(historyEvent => new { historyEvent.IsPublished, historyEvent.DatePrecision, historyEvent.EventMonthDay })
            .HasDatabaseName("IX_QueenHistoryEvents_Published_MonthDay");

        builder.HasIndex(historyEvent => new { historyEvent.SourceType, historyEvent.SourceKey })
            .IsUnique()
            .HasDatabaseName("IX_QueenHistoryEvents_Source");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuoteEntityConfiguration : IEntityTypeConfiguration<QuoteEntity>
{
    public void Configure(EntityTypeBuilder<QuoteEntity> builder)
    {
        builder.ToTable("QUEEN_QUOTE_T", table => table.ExcludeFromMigrations());
        builder.HasKey(quote => quote.QuoteId);

        builder.Property(quote => quote.QuoteId)
            .HasColumnName("QUEEN_QUOTE_ID")
            .ValueGeneratedOnAdd();
        builder.Property(quote => quote.Text).HasColumnName("QUEEN_QUOTE").HasMaxLength(QuoteValidation.MaxTextLength);
        builder.Property(quote => quote.WhoSaid).HasColumnName("WHO_SAID").HasMaxLength(QuoteValidation.MaxWhoSaidLength);
        builder.Property(quote => quote.Context).HasColumnName("CONTEXT").HasMaxLength(QuoteValidation.MaxContextLength).HasColumnType($"varchar({QuoteValidation.MaxContextLength})");
        builder.Property(quote => quote.SourceType).HasColumnName("SOURCE_TYPE").HasConversion<string>().HasMaxLength(50).HasColumnType("varchar(50)");
        builder.Property(quote => quote.SourceKey).HasColumnName("SOURCE_KEY").HasMaxLength(QuoteValidation.MaxSourceKeyLength).HasColumnType($"varchar({QuoteValidation.MaxSourceKeyLength})");
        builder.Property(quote => quote.CreatedAt).HasColumnName("CREATE_DATE");
        builder.Property(quote => quote.IsPublished)
            .HasColumnName("DISPLAY")
            .HasConversion(
                value => value ? (byte)1 : (byte)0,
                value => value == 1);

        builder.HasIndex(quote => new { quote.SourceType, quote.SourceKey })
            .IsUnique()
            .HasFilter("[SOURCE_TYPE] IS NOT NULL AND [SOURCE_KEY] IS NOT NULL")
            .HasDatabaseName("IX_QUEEN_QUOTE_T_Source");
    }
}

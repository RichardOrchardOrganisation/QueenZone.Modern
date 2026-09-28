using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class QuoteCsvImporter(QueenZoneDbContext dbContext)
{
    private static readonly string[] ExpectedHeaders =
    [
        "Text",
        "WhoSaid",
        "Context",
        "SourceType",
        "SourceKey",
    ];

    public async Task<QuoteCsvImportResult> ImportAsync(
        string csvPath,
        DateTime importedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = ReadRows(csvPath);
        var sourceKeys = rows.Select(row => row.SourceKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await dbContext.Quotes
            .Where(item => item.SourceType != null
                && item.SourceKey != null
                && sourceKeys.Contains(item.SourceKey))
            .ToListAsync(cancellationToken);
        var existingBySource = existing.ToDictionary(
            item => CsvImportRowParsing.BuildSourceKey(item.SourceType!.Value, item.SourceKey!),
            StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var row in rows)
        {
            if (existingBySource.TryGetValue(CsvImportRowParsing.BuildSourceKey(row.SourceType, row.SourceKey), out var entity))
            {
                if (Apply(entity, row, isNew: false))
                {
                    updated++;
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            entity = new QuoteEntity
            {
                CreatedAt = importedAtUtc,
            };
            Apply(entity, row, isNew: true);
            dbContext.Quotes.Add(entity);
            existingBySource[CsvImportRowParsing.BuildSourceKey(row.SourceType, row.SourceKey)] = entity;
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new QuoteCsvImportResult(rows.Count, created, updated, unchanged);
    }

    public static IReadOnlyList<QuoteCsvImportRow> ReadRows(string csvPath)
    {
        var rows = new List<QuoteCsvImportRow>();
        var rowNumbersBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fields, rowNumber) in CsvImportRowParsing.ReadRows(csvPath, ExpectedHeaders))
        {
            var row = ParseRow(fields, rowNumber);
            var sourceKey = CsvImportRowParsing.BuildSourceKey(row.SourceType, row.SourceKey);
            if (rowNumbersBySource.TryGetValue(sourceKey, out var firstRowNumber))
            {
                throw new InvalidOperationException(
                    $"Row {rowNumber} has the same SourceType/SourceKey ({row.SourceType}:{row.SourceKey}) as row {firstRowNumber}.");
            }

            rowNumbersBySource.Add(sourceKey, rowNumber);
            rows.Add(row);
        }

        return rows;
    }

    private static QuoteCsvImportRow ParseRow(string[] fields, int rowNumber)
    {
        var text = CsvImportRowParsing.Required(fields[0], rowNumber, "Text");
        var whoSaid = CsvImportRowParsing.Required(fields[1], rowNumber, "WhoSaid");
        var context = string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim();
        var sourceType = CsvImportRowParsing.ParseEnum<QuoteSourceType>(fields[3], rowNumber, "SourceType");
        var sourceKey = CsvImportRowParsing.Required(fields[4], rowNumber, "SourceKey");

        if (text.Length > QuoteValidation.MaxTextLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Text must be {QuoteValidation.MaxTextLength} characters or fewer.");
        }

        if (whoSaid.Length > QuoteValidation.MaxWhoSaidLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} WhoSaid must be {QuoteValidation.MaxWhoSaidLength} characters or fewer.");
        }

        if (context is not null && context.Length > QuoteValidation.MaxContextLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Context must be {QuoteValidation.MaxContextLength} characters or fewer.");
        }

        if (sourceKey.Length > QuoteValidation.MaxSourceKeyLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} SourceKey must be {QuoteValidation.MaxSourceKeyLength} characters or fewer.");
        }

        return new QuoteCsvImportRow(text, whoSaid, context, sourceType, sourceKey);
    }

    private static bool Apply(
        QuoteEntity entity,
        QuoteCsvImportRow row,
        bool isNew)
    {
        var changed = isNew
            || entity.Text != row.Text
            || entity.WhoSaid != row.WhoSaid
            || entity.Context != row.Context
            || entity.SourceType != row.SourceType
            || entity.SourceKey != row.SourceKey;

        if (!changed)
        {
            return false;
        }

        entity.Text = row.Text;
        entity.WhoSaid = row.WhoSaid;
        entity.Context = row.Context;
        entity.SourceType = row.SourceType;
        entity.SourceKey = row.SourceKey;

        // Only force-publish brand-new rows. An existing row's publish state is
        // admin-owned (e.g. someone unpublished it in the admin UI) and must not
        // be silently reverted by a routine re-import of the same source CSV.
        if (isNew)
        {
            entity.IsPublished = true;
        }

        return true;
    }
}

using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class TriviaFactCsvImporter(QueenZoneDbContext dbContext)
{
    private static readonly string[] ExpectedHeaders =
    [
        "Text",
        "Category",
        "Difficulty",
        "Source",
    ];

    public async Task<TriviaFactCsvImportResult> ImportAsync(
        string csvPath,
        DateTime importedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = ReadRows(csvPath);
        var texts = rows.Select(row => row.Text).Distinct(StringComparer.Ordinal).ToList();
        var existing = await dbContext.TriviaFacts
            .Where(item => texts.Contains(item.Text))
            .ToListAsync(cancellationToken);
        var existingByText = existing.ToDictionary(item => item.Text, StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var row in rows)
        {
            if (existingByText.TryGetValue(row.Text, out var entity))
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

            entity = new TriviaFactEntity
            {
                CreatedAt = importedAtUtc,
            };
            Apply(entity, row, isNew: true);
            dbContext.TriviaFacts.Add(entity);
            existingByText[row.Text] = entity;
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new TriviaFactCsvImportResult(rows.Count, created, updated, unchanged);
    }

    public static IReadOnlyList<TriviaFactCsvImportRow> ReadRows(string csvPath)
    {
        var rows = new List<TriviaFactCsvImportRow>();
        var rowNumbersByText = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (fields, rowNumber) in CsvImportRowParsing.ReadRows(csvPath, ExpectedHeaders))
        {
            var row = ParseRow(fields, rowNumber);
            if (rowNumbersByText.TryGetValue(row.Text, out var firstRowNumber))
            {
                throw new InvalidOperationException(
                    $"Row {rowNumber} has the same Text as row {firstRowNumber}.");
            }

            rowNumbersByText.Add(row.Text, rowNumber);
            rows.Add(row);
        }

        return rows;
    }

    private static TriviaFactCsvImportRow ParseRow(string[] fields, int rowNumber)
    {
        var text = CsvImportRowParsing.Required(fields[0], rowNumber, "Text");
        var category = string.IsNullOrWhiteSpace(fields[1]) ? null : fields[1].Trim();
        var difficulty = string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim();
        var source = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim();

        if (text.Length > TriviaValidation.MaxTextLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Text must be {TriviaValidation.MaxTextLength} characters or fewer.");
        }

        if (category is not null && category.Length > TriviaValidation.MaxCategoryLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Category must be {TriviaValidation.MaxCategoryLength} characters or fewer.");
        }

        if (difficulty is not null && !TriviaValidation.AllowedDifficulties.Contains(difficulty, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Row {rowNumber} Difficulty must be easy, medium, or hard.");
        }

        if (source is not null && source.Length > TriviaValidation.MaxSourceLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Source must be {TriviaValidation.MaxSourceLength} characters or fewer.");
        }

        return new TriviaFactCsvImportRow(text, category, difficulty, source);
    }

    private static bool Apply(
        TriviaFactEntity entity,
        TriviaFactCsvImportRow row,
        bool isNew)
    {
        var changed = isNew
            || entity.Category != row.Category
            || entity.Difficulty != row.Difficulty
            || entity.Source != row.Source;

        if (!changed)
        {
            return false;
        }

        entity.Text = row.Text;
        entity.Category = row.Category;
        entity.Difficulty = row.Difficulty;
        entity.Source = row.Source;

        // Only force-publish brand-new rows. An existing row's publish state is
        // admin-owned and must not be silently reverted by a routine re-import.
        if (isNew)
        {
            entity.IsPublished = true;
        }

        return true;
    }
}

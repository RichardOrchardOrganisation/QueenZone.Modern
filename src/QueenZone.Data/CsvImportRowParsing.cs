using Microsoft.VisualBasic.FileIO;

namespace QueenZone.Data;

/// <summary>
/// Small parsing helpers shared by the CSV importers (<see cref="QueenHistoryCsvImporter"/>,
/// <see cref="QuoteCsvImporter"/>, <see cref="TriviaFactCsvImporter"/>).
/// </summary>
internal static class CsvImportRowParsing
{
    public static IEnumerable<(string[] Fields, int RowNumber)> ReadRows(
        string csvPath,
        string[] expectedHeaders)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("CSV path is required.", nameof(csvPath));
        }

        return EnumerateRows(csvPath, expectedHeaders);
    }

    private static IEnumerable<(string[] Fields, int RowNumber)> EnumerateRows(
        string csvPath,
        string[] expectedHeaders)
    {
        using var parser = new TextFieldParser(csvPath);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        var headers = parser.ReadFields()
            ?? throw new InvalidOperationException("CSV file is empty.");
        if (!headers.SequenceEqual(expectedHeaders, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"CSV header must be: {string.Join(",", expectedHeaders)}");
        }

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (fields.Length != expectedHeaders.Length)
            {
                throw new InvalidOperationException($"Row {rowNumber} has {fields.Length} columns; expected {expectedHeaders.Length}.");
            }

            yield return (fields, rowNumber);
        }
    }

    public static string Required(string? value, int rowNumber, string column)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Row {rowNumber} {column} is required.");
        }

        return value.Trim();
    }

    public static TEnum ParseEnum<TEnum>(string value, int rowNumber, string column)
        where TEnum : struct =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Row {rowNumber} {column} has unsupported value '{value}'.");

    public static string BuildSourceKey<TEnum>(TEnum sourceType, string sourceKey)
        where TEnum : struct, Enum =>
        $"{sourceType}:{sourceKey}";
}

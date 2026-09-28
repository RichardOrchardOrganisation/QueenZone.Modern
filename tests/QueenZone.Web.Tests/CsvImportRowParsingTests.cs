using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class CsvImportRowParsingTests
{
    private static readonly string[] Headers = ["First", "Second"];

    [Fact]
    public void ReadRows_keeps_quoted_fields_and_physical_row_numbers_after_empty_rows()
    {
        var path = WriteCsv("First,Second\nfirst,\"with, comma\"\n,  \nlast,value\n");

        var rows = CsvImportRowParsing.ReadRows(path, Headers).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(("with, comma", 2), (rows[0].Fields[1], rows[0].RowNumber));
        Assert.Equal(("last", 4), (rows[1].Fields[0], rows[1].RowNumber));
    }

    [Fact]
    public void ReadRows_rejects_wrong_column_count_at_the_original_row()
    {
        var path = WriteCsv("First,Second\nvalid,row\nmissing\n");

        var error = Assert.Throws<InvalidOperationException>(
            () => CsvImportRowParsing.ReadRows(path, Headers).ToList());

        Assert.Equal("Row 3 has 1 columns; expected 2.", error.Message);
    }

    [Fact]
    public void ReadRows_preserves_empty_file_and_header_errors()
    {
        var emptyPath = WriteCsv(string.Empty);
        var wrongHeaderPath = WriteCsv("Other,Second\nvalue,value\n");

        Assert.Equal("CSV file is empty.", Assert.Throws<InvalidOperationException>(
            () => CsvImportRowParsing.ReadRows(emptyPath, Headers).ToList()).Message);
        Assert.Equal("CSV header must be: First,Second", Assert.Throws<InvalidOperationException>(
            () => CsvImportRowParsing.ReadRows(wrongHeaderPath, Headers).ToList()).Message);
    }

    [Fact]
    public void ReadRows_requires_a_path()
    {
        var error = Assert.Throws<ArgumentException>(
            () => CsvImportRowParsing.ReadRows(" ", Headers).ToList());

        Assert.Equal("csvPath", error.ParamName);
    }

    private static string WriteCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}

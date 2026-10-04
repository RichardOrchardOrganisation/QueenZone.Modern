using System.Text;
using System.Text.Json.Nodes;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class CrosswordSeedJsonTests
{
    private static string SeedsDirectory => Path.Combine(AppContext.BaseDirectory, "data", "crosswords");

    [Fact]
    public void All_ten_seed_grids_validate_and_round_trip_without_losing_clues()
    {
        var files = Directory.GetFiles(SeedsDirectory, "*.json");
        Assert.Equal(10, files.Length);
        var slugs = new HashSet<string>();
        foreach (var file in files)
        {
            var parsed = CrosswordSeedJson.Parse(File.ReadAllBytes(file));
            Assert.True(parsed.IsValid, Path.GetFileName(file) + ": " + string.Join("; ", parsed.Errors.Select(error => error.Message)));
            var seed = Assert.IsType<CrosswordSeed>(parsed.Seed);
            Assert.True(slugs.Add(seed.Slug));
            Assert.InRange(seed.Grid.Clues.Count, 6, 50);
            var exported = CrosswordSeedJson.Export(seed);
            var roundTrip = CrosswordSeedJson.Parse(Encoding.UTF8.GetBytes(exported));
            Assert.True(roundTrip.IsValid);
            Assert.Equal(exported, CrosswordSeedJson.Export(roundTrip.Seed!));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public void Rejects_malformed_or_non_object_documents(string json)
    {
        var result = CrosswordSeedJson.Parse(Encoding.UTF8.GetBytes(json));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "file");
    }

    [Fact]
    public void Rejects_oversized_uploads_before_parsing()
    {
        var result = CrosswordSeedJson.Parse(new byte[CrosswordSeedJson.MaxBytes + 1]);
        Assert.Contains(result.Errors, error => error.Message.Contains("256 KB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("slug", "UPPER")]
    [InlineData("slug", "two--hyphens")]
    [InlineData("title", "")]
    [InlineData("difficulty", "expert")]
    [InlineData("style", "barred")]
    public void Reports_metadata_errors_by_field(string field, string value)
    {
        var document = Fixture();
        document[field] = value;
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
    }

    [Theory]
    [InlineData("title", 201)]
    [InlineData("description", 1001)]
    [InlineData("slug", 101)]
    public void Rejects_metadata_over_field_limits(string field, int length)
    {
        var document = Fixture();
        document[field] = new string('a', length);
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    public void Dimensions_require_integers(string field)
    {
        var document = Fixture();
        document[field] = 5.5;
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
        document[field] = "5";
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
        document[field] = 2147483648L;
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
    }

    [Theory]
    [InlineData("slug")]
    [InlineData("title")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("grid")]
    [InlineData("entries")]
    public void Missing_and_null_required_fields_are_rejected(string field)
    {
        var document = Fixture();
        document.Remove(field);
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
        document[field] = null;
        Assert.Contains(Parse(document).Errors, error => error.Code == field);
    }

    [Fact]
    public void Optional_description_and_explanation_can_be_missing_or_null()
    {
        var document = Fixture();
        document.Remove("description");
        document["entries"]![0]!["explanation"] = null;
        Assert.True(Parse(document).IsValid);
        document["description"] = null;
        Assert.True(Parse(document).IsValid);
    }

    [Fact]
    public void Rejects_wrong_JSON_types_with_field_locations()
    {
        var document = Fixture();
        document["title"] = 42;
        document["grid"]![0] = null;
        document["entries"]![0] = "invalid";
        var result = Parse(document);
        Assert.Contains(result.Errors, error => error.Code == "title");
        Assert.Contains(result.Errors, error => error.Code == "grid[0]");
        Assert.Contains(result.Errors, error => error.Code == "entries[0]");
    }

    [Theory]
    [InlineData("direction", "Across")]
    [InlineData("answer", "")]
    [InlineData("clue", "")]
    [InlineData("enumeration", "5")]
    [InlineData("enumeration", "(0,5)")]
    [InlineData("enumeration", "(4)")]
    [InlineData("enumeration", "(99999999999999999)")]
    public void Rejects_invalid_entries_with_field_locations(string field, string value)
    {
        var document = Fixture();
        document["entries"]![0]![field] = value;
        Assert.Contains(Parse(document).Errors, error => error.Code == "entries[0]." + field);
    }

    [Fact]
    public void Entry_number_is_required_and_entry_text_has_limits()
    {
        var document = Fixture();
        document["entries"]![0]!["number"] = null;
        document["entries"]![0]!["clue"] = new string('a', 501);
        document["entries"]![0]!["explanation"] = new string('a', 301);
        var result = Parse(document);
        Assert.Contains(result.Errors, error => error.Code == "entries[0].number");
        Assert.Contains(result.Errors, error => error.Code == "entries[0].clue");
        Assert.Contains(result.Errors, error => error.Code == "entries[0].explanation");
    }

    [Theory]
    [InlineData("(5)")]
    [InlineData("(2,3)")]
    [InlineData("(2-3)")]
    public void Preserves_word_breaks_and_hyphens_in_enumeration(string enumeration)
    {
        var document = Fixture();
        document["entries"]![0]!["enumeration"] = enumeration;
        var result = Parse(document);
        Assert.True(result.IsValid);
        Assert.Equal(enumeration, result.Seed!.Grid.Clues[0].Enumeration);
    }

    [Fact]
    public void Shared_validator_rejects_corrupt_solutions_and_returns_warnings_for_valid_grids()
    {
        var document = Fixture();
        Assert.NotEmpty(Parse(document).Warnings);
        document["entries"]![0]!["answer"] = "WRONG";
        Assert.Contains(Parse(document).Errors, error => error.Code == "answer-mismatch");
    }

    private static JsonObject Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SeedsDirectory, "meet-the-band.json")))!.AsObject();

    private static CrosswordSeedParseResult Parse(JsonObject document) =>
        CrosswordSeedJson.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
}

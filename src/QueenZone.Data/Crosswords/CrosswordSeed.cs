using System.Text.Json;
using System.Text.RegularExpressions;

namespace QueenZone.Data;

public sealed record CrosswordSeed(
    string Slug,
    string Title,
    string Description,
    string Difficulty,
    string Style,
    CrosswordGrid Grid);

public sealed record CrosswordSeedParseResult(
    CrosswordSeed? Seed,
    IReadOnlyList<CrosswordGridIssue> Errors,
    IReadOnlyList<CrosswordGridIssue> Warnings)
{
    public bool IsValid => Seed is not null && Errors.Count == 0;
}

/// <summary>Appendix B interchange format, shared by the CLI and admin import/export.</summary>
public static partial class CrosswordSeedJson
{
    public const int MaxBytes = 256 * 1024;

    public static CrosswordSeedParseResult Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaxBytes)
        {
            return new(null, [new("file", "Crossword JSON must not exceed 256 KB.")], []);
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseDocument(document.RootElement);
        }
        catch (JsonException)
        {
            return new(null, [new("file", "Supply a valid JSON object.")], []);
        }
    }

    public static string Export(CrosswordSeed seed) => JsonSerializer.Serialize(new
    {
        slug = seed.Slug,
        title = seed.Title,
        description = seed.Description,
        difficulty = seed.Difficulty,
        width = seed.Grid.Width,
        height = seed.Grid.Height,
        style = seed.Style,
        grid = seed.Grid.Rows,
        entries = seed.Grid.Clues.Select(clue => new
        {
            number = clue.Number,
            direction = clue.Direction == CrosswordDirection.Across ? "across" : "down",
            answer = clue.Answer,
            clue = clue.Clue,
            enumeration = clue.Enumeration,
            explanation = clue.Explanation
        })
    }, new JsonSerializerOptions { WriteIndented = true });

    private static CrosswordSeedParseResult ParseDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(null, [new("file", "Supply a JSON object.")], []);
        }
        var errors = new List<CrosswordGridIssue>();
        var slug = ReadString(root, "slug", errors, 100);
        var title = ReadString(root, "title", errors, 200);
        var description = ReadString(root, "description", errors, 1000, optional: true);
        var difficulty = ReadString(root, "difficulty", errors, 20);
        var style = ReadString(root, "style", errors, 20);
        if (!SlugPattern().IsMatch(slug))
        {
            errors.Add(new("slug", "Slug must use lowercase letters, digits and single separating hyphens."));
        }
        if (difficulty is not ("easy" or "medium" or "hard"))
        {
            errors.Add(new("difficulty", "Difficulty must be easy, medium or hard."));
        }
        if (style is not ("american" or "british"))
        {
            errors.Add(new("style", "Style must be american or british."));
        }
        var width = ReadInteger(root, "width", errors);
        var height = ReadInteger(root, "height", errors);
        var rows = ReadRows(root, errors);
        var entries = ReadEntries(root, errors);
        if (errors.Count > 0)
        {
            return new(null, errors, []);
        }
        var grid = new CrosswordGrid(width, height, rows, entries);
        var validation = CrosswordGridValidator.Validate(grid);
        if (!validation.IsValid)
        {
            return new(null, validation.Errors, validation.Warnings);
        }
        return new(new(slug, title, description, difficulty, style, grid), [], validation.Warnings);
    }

    private static string ReadString(JsonElement element, string field,
        List<CrosswordGridIssue> errors, int maxLength, string prefix = "", bool optional = false)
    {
        if (!element.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (!optional)
            {
                errors.Add(new(prefix + field, "This field is required."));
            }
            return "";
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new(prefix + field, "Supply a string."));
            return "";
        }
        var text = value.GetString()!;
        if ((!optional && string.IsNullOrWhiteSpace(text)) || text.Length > maxLength)
        {
            errors.Add(new(prefix + field, $"Supply {(optional ? "up to" : "1 to")} {maxLength} characters."));
        }
        return text;
    }

    private static int ReadInteger(JsonElement element, string field, List<CrosswordGridIssue> errors, string prefix = "")
    {
        if (element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }
        errors.Add(new(prefix + field, "Supply an integer."));
        return 0;
    }

    private static List<string> ReadRows(JsonElement root, List<CrosswordGridIssue> errors)
    {
        var rows = new List<string>();
        if (!root.TryGetProperty("grid", out var grid) || grid.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new("grid", "Supply an array of solution rows."));
            return rows;
        }
        foreach (var row in grid.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.String)
            {
                errors.Add(new($"grid[{rows.Count}]", "Supply a string row."));
            }
            rows.Add(row.ValueKind == JsonValueKind.String ? row.GetString()! : "");
        }
        return rows;
    }

    private static List<CrosswordClue> ReadEntries(JsonElement root, List<CrosswordGridIssue> errors)
    {
        var clues = new List<CrosswordClue>();
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new("entries", "Supply an array of clues."));
            return clues;
        }
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var prefix = $"entries[{index++}].";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(prefix.TrimEnd('.'), "Supply a clue object."));
                continue;
            }
            clues.Add(ReadClue(entry, prefix, errors));
        }
        return clues;
    }

    private static CrosswordClue ReadClue(JsonElement entry, string prefix, List<CrosswordGridIssue> errors)
    {
        var number = ReadInteger(entry, "number", errors, prefix);
        var direction = ReadString(entry, "direction", errors, 6, prefix);
        var answer = ReadString(entry, "answer", errors, 15, prefix);
        var clue = ReadString(entry, "clue", errors, 500, prefix);
        var enumeration = ReadString(entry, "enumeration", errors, 50, prefix);
        var explanation = ReadString(entry, "explanation", errors, 300, prefix, optional: true);
        if (direction is not ("across" or "down"))
        {
            errors.Add(new(prefix + "direction", "Direction must be across or down."));
        }
        if (!EnumerationPattern().IsMatch(enumeration)
            || !EnumerationMatches(enumeration, answer.Length))
        {
            errors.Add(new(prefix + "enumeration", "Enumeration must show word lengths matching the answer, such as (3,5) or (3-5)."));
        }
        return new(number, direction == "across" ? CrosswordDirection.Across : CrosswordDirection.Down,
            answer, clue, enumeration, explanation.Length == 0 ? null : explanation);
    }

    private static bool EnumerationMatches(string enumeration, int length)
    {
        var total = 0;
        foreach (var part in enumeration[1..^1].Split([',', '-']))
        {
            if (!int.TryParse(part, out var count) || count <= 0 || count > 15)
            {
                return false;
            }
            total += count;
        }
        return total == length;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^\(\d+(?:[,-]\d+)*\)$")]
    private static partial Regex EnumerationPattern();
}

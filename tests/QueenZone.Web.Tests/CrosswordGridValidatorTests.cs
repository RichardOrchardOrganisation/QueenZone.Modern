using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class CrosswordGridValidatorTests
{
    private static readonly string[] Square = ["SATOR", "AREPO", "TENET", "OPERA", "ROTAS"];

    [Fact]
    public void Derives_numbering_and_coordinates_in_reading_order()
    {
        var result = CrosswordGridValidator.Validate(WithClues(Square));

        Assert.True(result.IsValid);
        Assert.Equal([1, 1, 2, 3, 4, 5, 6, 7, 8, 9], result.Runs.Select(run => run.Number));
        Assert.Equal(new CrosswordRun(1, CrosswordDirection.Across, 0, 0, "SATOR"), result.Runs[0]);
        Assert.Equal(new CrosswordRun(5, CrosswordDirection.Down, 0, 4, "ROTAS"), result.Runs[5]);
        Assert.Equal(new CrosswordRun(9, CrosswordDirection.Across, 4, 0, "ROTAS"), result.Runs[^1]);
        Assert.DoesNotContain(result.Warnings, warning => warning.Code is "unchecked" or "symmetry");
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(5, 16)]
    [InlineData(-1, 5)]
    public void Rejects_dimensions_before_accessing_cells(int width, int height)
    {
        var result = CrosswordGridValidator.Validate(new(width, height, Square, []));
        Assert.Contains(result.Errors, error => error.Code == "dimensions");
        Assert.Empty(result.Runs);
    }

    [Theory]
    [InlineData("AAAA")]
    [InlineData("AAAAAA")]
    public void Rejects_ragged_rows(string row)
    {
        var result = CrosswordGridValidator.Validate(new(5, 5, [row, .. Square.Skip(1)], []));
        Assert.Contains(result.Errors, error => error.Code == "shape");
    }

    [Fact]
    public void Rejects_wrong_row_count()
    {
        Assert.Contains(CrosswordGridValidator.Validate(new(5, 5, Square.Take(4).ToArray(), [])).Errors,
            error => error.Code == "shape");
    }

    [Theory]
    [InlineData('a')]
    [InlineData('1')]
    [InlineData(' ')]
    [InlineData('É')]
    public void Rejects_non_solution_letters_with_cell_location(char invalid)
    {
        var grid = WithClues([invalid + "ATOR", .. Square.Skip(1)]);
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors,
            error => error.Code == "letter" && error.Row == 0 && error.Column == 0);
    }

    [Fact]
    public void Rejects_missing_clue_for_every_white_run()
    {
        var result = CrosswordGridValidator.Validate(new(5, 5, Square, []));
        Assert.Equal(10, result.Errors.Count(error => error.Code == "missing-entry"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_missing_clue_text(string clue)
    {
        var grid = ReplaceFirst(first => first with { Clue = clue });
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "missing-clue");
    }

    [Theory]
    [InlineData("SATAR")]
    [InlineData("SATORA")]
    [InlineData("SATO")]
    public void Rejects_crossing_disagreement_and_wrong_entry_length(string answer)
    {
        var grid = ReplaceFirst(first => first with { Answer = answer });
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors,
            error => error.Code == "answer-mismatch" && error.Number == 1 && error.Direction == CrosswordDirection.Across);
    }

    [Theory]
    [InlineData("sator")]
    [InlineData("SAT OR")]
    [InlineData("")]
    public void Rejects_invalid_answer_letters(string answer)
    {
        var grid = ReplaceFirst(first => first with { Answer = answer });
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "answer-letters");
    }

    [Fact]
    public void Rejects_answer_through_a_block()
    {
        var grid = WithClues(["SAT##", "AREPO", "TENET", "OPERA", "ROTAS"]);
        grid = grid with { Clues = [grid.Clues[0] with { Answer = "SATOR" }, .. grid.Clues.Skip(1)] };
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "answer-mismatch");
    }

    [Fact]
    public void Rejects_entries_of_two_letters()
    {
        var grid = WithClues(["SA###", "AREPO", "TENET", "OPERA", "ROTAS"]);
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "short-entry");
    }

    [Fact]
    public void Rejects_nonstandard_or_duplicate_numbering()
    {
        var wrong = ReplaceFirst(first => first with { Number = 50 });
        var result = CrosswordGridValidator.Validate(wrong);
        Assert.Contains(result.Errors, error => error.Code == "numbering");
        Assert.Contains(result.Errors, error => error.Code == "missing-entry");
        var grid = WithClues(Square);
        Assert.Contains(CrosswordGridValidator.Validate(grid with { Clues = [.. grid.Clues, grid.Clues[0]] }).Errors,
            error => error.Code == "duplicate-entry");
    }

    [Fact]
    public void Rejects_unknown_direction()
    {
        var grid = ReplaceFirst(first => first with { Direction = (CrosswordDirection)42 });
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "numbering");
    }

    [Fact]
    public void Rejects_disconnected_white_cells()
    {
        var grid = WithClues(["SATOR", "#####", "#####", "#####", "ROTAS"]);
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "connectivity");
    }

    [Fact]
    public void Rejects_empty_grid_and_orphan_cells()
    {
        var empty = new CrosswordGrid(5, 5, ["#####", "#####", "#####", "#####", "#####"], []);
        Assert.Contains(CrosswordGridValidator.Validate(empty).Errors, error => error.Code == "connectivity");
        var orphan = empty with { Rows = ["A####", .. empty.Rows.Skip(1)] };
        Assert.Contains(CrosswordGridValidator.Validate(orphan).Errors,
            error => error.Code == "orphan-cell" && error.Row == 0 && error.Column == 0);
    }

    [Fact]
    public void Warnings_do_not_block_British_style_publishing()
    {
        var grid = WithClues(["SATOR", "#####", "#####", "#####", "#####"]);
        var result = CrosswordGridValidator.Validate(grid);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Code == "unchecked");
        Assert.Contains(result.Warnings, warning => warning.Code == "symmetry");
    }

    [Fact]
    public void Warns_about_duplicate_answers_and_answer_words_in_clues()
    {
        var grid = ReplaceFirst(first => first with { Clue = "The word sator, in a square." });
        var result = CrosswordGridValidator.Validate(grid);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Code == "duplicate-answer");
        Assert.Contains(result.Warnings, warning => warning.Code == "answer-in-clue");
        var substring = ReplaceFirst(first => first with { Clue = "A satorial word." });
        Assert.DoesNotContain(CrosswordGridValidator.Validate(substring).Warnings, warning => warning.Code == "answer-in-clue");
    }

    [Fact]
    public void Rejects_overlong_explanation()
    {
        var grid = ReplaceFirst(first => first with { Explanation = new string('x', 301) });
        Assert.Contains(CrosswordGridValidator.Validate(grid).Errors, error => error.Code == "explanation-length");
        Assert.True(CrosswordGridValidator.Validate(ReplaceFirst(first => first with { Explanation = new string('x', 300) })).IsValid);
    }

    [Fact]
    public void Random_valid_grids_have_stable_derived_numbering_after_clue_order_changes()
    {
        var random = new Random(2050);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var width = random.Next(5, 16);
            var height = random.Next(5, 16);
            var rows = Enumerable.Range(0, height)
                .Select(_ => new string(Enumerable.Range(0, width).Select(_ => (char)random.Next('A', 'Z' + 1)).ToArray()))
                .ToArray();
            var grid = WithClues(rows);
            var first = CrosswordGridValidator.Validate(grid);
            var reordered = CrosswordGridValidator.Validate(grid with { Clues = grid.Clues.Reverse().ToArray() });
            Assert.True(first.IsValid);
            Assert.True(reordered.IsValid);
            Assert.Equal(first.Runs, reordered.Runs);
            Assert.Equal(width + height - 1, first.Runs.Max(run => run.Number));
        }
    }

    private static CrosswordGrid ReplaceFirst(Func<CrosswordClue, CrosswordClue> replace)
    {
        var grid = WithClues(Square);
        return grid with { Clues = [replace(grid.Clues[0]), .. grid.Clues.Skip(1)] };
    }

    private static CrosswordGrid WithClues(string[] rows)
    {
        var grid = new CrosswordGrid(rows[0].Length, rows.Length, rows, []);
        var runs = CrosswordGridValidator.Validate(grid).Runs;
        return grid with
        {
            Clues = runs.Select(run => new CrosswordClue(run.Number, run.Direction, run.Answer,
                "A word in this square.", $"({run.Answer.Length})")).ToArray()
        };
    }
}

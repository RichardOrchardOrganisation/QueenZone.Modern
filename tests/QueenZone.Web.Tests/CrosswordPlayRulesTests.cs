using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class CrosswordPlayRulesTests
{
    private static CrosswordGrid Grid => CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band").Grid;

    [Fact]
    public void Check_reports_empty_correct_and_incorrect_without_disclosing_letters()
    {
        var grid = Grid;
        var letters = CrosswordPlayRules.EmptyLetters(grid).ToCharArray();
        var cells = CrosswordPlayRules.SelectCells(grid, new("grid"));
        letters[cells[0]] = char.ToLowerInvariant(string.Concat(grid.Rows)[cells[0]]);
        letters[cells[1]] = string.Concat(grid.Rows)[cells[1]] == 'Z' ? 'X' : 'Z';
        var checks = CrosswordPlayRules.Check(grid, new string(letters), new("grid"));
        Assert.Equal("correct", checks[0].Status);
        Assert.Equal("incorrect", checks[1].Status);
        Assert.All(checks.Skip(2), check => Assert.Equal("empty", check.Status));
        Assert.Equal(cells, checks.Select(check => check.Index));
        Assert.False(CrosswordPlayRules.IsComplete(grid, new string(letters)));
        Assert.True(CrosswordPlayRules.IsComplete(grid, string.Concat(grid.Rows).ToLowerInvariant()));
    }

    [Fact]
    public void Entry_and_cell_reveal_use_derived_coordinates_and_skip_blocks()
    {
        var grid = Grid;
        foreach (var run in CrosswordGridValidator.Validate(grid).Runs)
        {
            var reveal = CrosswordPlayRules.Reveal(grid, new("entry", Number: run.Number, Direction: run.Direction));
            Assert.Equal(run.Answer, string.Concat(reveal.Select(cell => cell.Letter)));
            Assert.Equal(run.Row * grid.Width + run.Column, reveal[0].Index);
            Assert.Equal(reveal[0], Assert.Single(CrosswordPlayRules.Reveal(grid, new("cell", reveal[0].Index))));
        }
        Assert.Equal(string.Concat(grid.Rows).Count(cell => cell != '#'), CrosswordPlayRules.Reveal(grid, new("grid")).Count);
    }

    [Fact]
    public void Short_checks_expand_only_the_selected_entry_or_cell()
    {
        var grid = Grid;
        var run = CrosswordGridValidator.Validate(grid).Runs[0];
        var selection = new CrosswordSelection("entry", Number: run.Number, Direction: run.Direction);
        var letters = CrosswordPlayRules.ExpandLetters(grid, run.Answer.ToLowerInvariant(), selection);
        Assert.All(CrosswordPlayRules.Check(grid, letters, selection), cell => Assert.Equal("correct", cell.Status));
        var first = run.Row * grid.Width + run.Column;
        var cellSelection = new CrosswordSelection("cell", first);
        Assert.Equal(run.Answer[0], CrosswordPlayRules.ExpandLetters(grid, run.Answer[..1], cellSelection)[first]);
        Assert.Equal(letters, CrosswordPlayRules.ExpandLetters(grid, letters, selection));
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.ExpandLetters(grid, "AB", cellSelection));
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.ExpandLetters(grid, run.Answer, new("grid")));
        Assert.Throws<ArgumentNullException>(() => CrosswordPlayRules.ExpandLetters(grid, null!, selection));
    }

    [Theory]
    [InlineData("unknown", null, null, null)]
    [InlineData("cell", null, null, null)]
    [InlineData("cell", -1, null, null)]
    [InlineData("cell", 49, null, null)]
    [InlineData("entry", null, 999, CrosswordDirection.Across)]
    [InlineData("entry", null, 1, null)]
    public void Invalid_selection_is_rejected(string scope, int? cell, int? number, CrosswordDirection? direction) =>
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.SelectCells(Grid, new(scope, cell, number, direction)));

    [Fact]
    public void Blocks_invalid_characters_and_wrong_length_are_rejected_or_normalized()
    {
        var grid = Grid;
        var block = string.Concat(grid.Rows).IndexOf('#');
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.SelectCells(grid, new("cell", block)));
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.NormalizeLetters(grid, "ABC"));
        Assert.Throws<ArgumentNullException>(() => CrosswordPlayRules.NormalizeLetters(grid, null!));
        Assert.Throws<ArgumentNullException>(() => CrosswordPlayRules.SelectCells(grid, null!));
        var letters = CrosswordPlayRules.EmptyLetters(grid).ToCharArray();
        var cells = CrosswordPlayRules.SelectCells(grid, new("grid"));
        letters[cells[0]] = '?';
        Assert.Throws<ArgumentException>(() => CrosswordPlayRules.NormalizeLetters(grid, new string(letters)));
        letters[cells[0]] = ' ';
        letters[cells[1]] = '_';
        letters[block] = '?';
        Assert.Equal(CrosswordPlayRules.EmptyLetters(grid), CrosswordPlayRules.NormalizeLetters(grid, new string(letters)));
    }

    [Fact]
    public void Explanation_requires_completed_or_fully_revealed_entry()
    {
        var original = Grid;
        var grid = original with { Clues = original.Clues.Select(clue => clue with { Explanation = $"Fact {clue.Number} {clue.Direction}" }).ToArray() };
        var blank = CrosswordPlayRules.EmptyLetters(grid);
        Assert.Empty(CrosswordPlayRules.GetExplanations(grid, blank, []));
        var run = CrosswordGridValidator.Validate(grid).Runs[0];
        var cells = CrosswordPlayRules.SelectCells(grid, new("entry", Number: run.Number, Direction: run.Direction));
        Assert.Empty(CrosswordPlayRules.GetExplanations(grid, blank, [cells[0]]));
        Assert.Contains(CrosswordPlayRules.GetExplanations(grid, blank, cells), item => item.Number == run.Number && item.Direction == run.Direction);
        Assert.Equal(grid.Clues.Count, CrosswordPlayRules.GetExplanations(grid, string.Concat(grid.Rows), []).Count);
        Assert.Empty(CrosswordPlayRules.GetExplanations(original, string.Concat(original.Rows), []));
    }

    [Fact]
    public void Fingerprint_ignores_clue_edits_but_detects_shape_and_answer_changes()
    {
        var grid = Grid;
        var fingerprint = CrosswordPlayRules.GridFingerprint(grid);
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, CrosswordPlayRules.GridFingerprint(grid with { Clues = [] }));
        Assert.NotEqual(fingerprint, CrosswordPlayRules.GridFingerprint(grid with { Width = 8 }));
        Assert.NotEqual(fingerprint, CrosswordPlayRules.GridFingerprint(grid with { Rows = grid.Rows.Select(row => row.Replace('A', 'Z')).ToArray() }));
    }
}

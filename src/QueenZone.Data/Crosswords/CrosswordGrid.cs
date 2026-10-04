namespace QueenZone.Data;

public enum CrosswordDirection { Across, Down }

/// <summary>Solution rows use # for blocks and uppercase A-Z for letters.</summary>
public sealed record CrosswordGrid(
    int Width,
    int Height,
    IReadOnlyList<string> Rows,
    IReadOnlyList<CrosswordClue> Clues);

public sealed record CrosswordClue(
    int Number,
    CrosswordDirection Direction,
    string Answer,
    string Clue,
    string Enumeration,
    string? Explanation = null);

/// <summary>Position, numbering and solution are derived from the grid, never input coordinates.</summary>
public sealed record CrosswordRun(
    int Number,
    CrosswordDirection Direction,
    int Row,
    int Column,
    string Answer);

public sealed record CrosswordGridIssue(
    string Code,
    string Message,
    int? Row = null,
    int? Column = null,
    int? Number = null,
    CrosswordDirection? Direction = null);

public sealed record CrosswordGridValidation(
    IReadOnlyList<CrosswordRun> Runs,
    IReadOnlyList<CrosswordGridIssue> Errors,
    IReadOnlyList<CrosswordGridIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

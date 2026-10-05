using System.Security.Cryptography;
using System.Text;

namespace QueenZone.Data;

public sealed record CrosswordSelection(string Scope, int? Cell = null, int? Number = null,
    CrosswordDirection? Direction = null);
public sealed record CrosswordCellCheck(int Index, string Status);
public sealed record CrosswordRevealedCell(int Index, string Letter);
public sealed record CrosswordEntryExplanation(int Number, CrosswordDirection Direction, string Explanation);

/// <summary>Server-only solution operations. Public puzzle DTOs must never contain the grid passed here.</summary>
public static class CrosswordPlayRules
{
    public static void EnsureVersion(CrosswordCatalogItem puzzle, Guid expected)
    {
        if (expected == Guid.Empty)
        {
            throw new ArgumentException("A play version from the current puzzle is required.", nameof(expected));
        }
        if (expected != puzzle.PlayVersion)
        {
            throw new OptimisticConcurrencyException("This crossword has changed. Reload before continuing.");
        }
    }

    public static string NormalizeLetters(CrosswordGrid grid, string letters)
    {
        ArgumentNullException.ThrowIfNull(letters);
        if (letters.Length != grid.Width * grid.Height)
        {
            throw new ArgumentException("Letters must contain one character per grid cell.", nameof(letters));
        }
        var result = letters.ToUpperInvariant().ToCharArray();
        var solution = string.Concat(grid.Rows);
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = NormalizeCell(solution[index], result[index]);
        }
        return new string(result);
    }

    public static string EmptyLetters(CrosswordGrid grid) =>
        new(string.Concat(grid.Rows).Select(cell => cell == '#' ? '#' : '.').ToArray());

    public static string ExpandLetters(CrosswordGrid grid, string letters, CrosswordSelection selection)
    {
        ArgumentNullException.ThrowIfNull(letters);
        if (letters.Length == grid.Width * grid.Height)
        {
            return NormalizeLetters(grid, letters);
        }
        var cells = SelectCells(grid, selection);
        if (selection.Scope == "grid" || letters.Length != cells.Count)
        {
            throw new ArgumentException("Supply the whole grid or exactly the selected letters.", nameof(letters));
        }
        var expanded = EmptyLetters(grid).ToCharArray();
        for (var offset = 0; offset < cells.Count; offset++)
        {
            expanded[cells[offset]] = letters[offset];
        }
        return NormalizeLetters(grid, new string(expanded));
    }

    public static IReadOnlyList<int> SelectCells(CrosswordGrid grid, CrosswordSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Scope switch
        {
            "grid" => Enumerable.Range(0, grid.Width * grid.Height)
                .Where(index => grid.Rows[index / grid.Width][index % grid.Width] != '#').ToArray(),
            "cell" => SelectCell(grid, selection.Cell),
            "entry" => SelectEntry(grid, selection.Number, selection.Direction),
            _ => throw new ArgumentException("Select a cell, entry or grid.", nameof(selection))
        };
    }

    public static IReadOnlyList<CrosswordCellCheck> Check(CrosswordGrid grid, string letters,
        CrosswordSelection selection)
    {
        letters = NormalizeLetters(grid, letters);
        var solution = string.Concat(grid.Rows);
        return SelectCells(grid, selection).Select(index =>
            new CrosswordCellCheck(index, CellStatus(letters[index], solution[index]))).ToArray();
    }

    public static IReadOnlyList<CrosswordRevealedCell> Reveal(CrosswordGrid grid, CrosswordSelection selection)
    {
        var solution = string.Concat(grid.Rows);
        return SelectCells(grid, selection).Select(index => new CrosswordRevealedCell(index, solution[index].ToString())).ToArray();
    }

    public static bool IsComplete(CrosswordGrid grid, string letters) =>
        NormalizeLetters(grid, letters) == string.Concat(grid.Rows);

    public static IReadOnlyList<CrosswordEntryExplanation> GetExplanations(CrosswordGrid grid, string letters,
        IReadOnlyCollection<int> revealedCells)
    {
        letters = NormalizeLetters(grid, letters);
        var revealed = revealedCells.ToHashSet();
        return CrosswordGridValidator.Validate(grid).Runs
            .Where(run => CanExplain(grid, run, letters, revealed))
            .Select(run => grid.Clues.Single(clue => clue.Number == run.Number && clue.Direction == run.Direction))
            .Where(clue => !string.IsNullOrWhiteSpace(clue.Explanation))
            .Select(clue => new CrosswordEntryExplanation(clue.Number, clue.Direction, clue.Explanation!)).ToArray();
    }

    /// <summary>Clue corrections do not change the identity of a saved grid.</summary>
    public static string GridFingerprint(CrosswordGrid grid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{grid.Width}x{grid.Height}:{string.Concat(grid.Rows)}")));

    private static char NormalizeCell(char solution, char letter)
    {
        if (solution == '#')
        {
            return '#';
        }
        if (letter is '.' or ' ' or '_')
        {
            return '.';
        }
        if (letter is >= 'A' and <= 'Z')
        {
            return letter;
        }
        throw new ArgumentException("White cells accept only A-Z or a blank.");
    }

    private static IReadOnlyList<int> SelectCell(CrosswordGrid grid, int? cell)
    {
        if (cell is null || cell < 0 || cell >= grid.Width * grid.Height)
        {
            throw new ArgumentException("Select a cell within the grid.", nameof(cell));
        }
        if (grid.Rows[cell.Value / grid.Width][cell.Value % grid.Width] == '#')
        {
            throw new ArgumentException("A block cannot be selected.", nameof(cell));
        }
        return [cell.Value];
    }

    private static IReadOnlyList<int> SelectEntry(CrosswordGrid grid, int? number, CrosswordDirection? direction)
    {
        var run = CrosswordGridValidator.Validate(grid).Runs.SingleOrDefault(run => run.Number == number && run.Direction == direction);
        if (run is null)
        {
            throw new ArgumentException("Select an existing entry.", nameof(number));
        }
        return RunCells(grid, run).ToArray();
    }

    private static IEnumerable<int> RunCells(CrosswordGrid grid, CrosswordRun run) =>
        Enumerable.Range(0, run.Answer.Length).Select(offset => run.Row * grid.Width + run.Column +
            offset * (run.Direction == CrosswordDirection.Across ? 1 : grid.Width));

    private static bool CanExplain(CrosswordGrid grid, CrosswordRun run, string letters, HashSet<int> revealed)
    {
        var cells = RunCells(grid, run).ToArray();
        var solution = string.Concat(grid.Rows);
        return cells.All(index => letters[index] == solution[index]) || cells.All(revealed.Contains);
    }

    private static string CellStatus(char letter, char solution) =>
        letter switch
        {
            '.' => "empty",
            _ when letter == solution => "correct",
            _ => "incorrect",
        };
}

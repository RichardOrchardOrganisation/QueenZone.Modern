namespace QueenZone.Data;

/// <summary>Shared, deterministic validation for drafts, publishing and imports. Performs no I/O.</summary>
public static class CrosswordGridValidator
{
    public static CrosswordGridValidation Validate(CrosswordGrid grid)
    {
        var errors = new List<CrosswordGridIssue>();
        var warnings = new List<CrosswordGridIssue>();
        if (grid.Width is < 5 or > 15 || grid.Height is < 5 or > 15)
        {
            errors.Add(new("dimensions", "Width and height must be between 5 and 15."));
        }
        if (grid.Rows.Count != grid.Height || grid.Rows.Any(row => row.Length != grid.Width))
        {
            errors.Add(new("shape", "Supply exactly height rows, each containing width cells."));
        }
        if (errors.Count > 0)
        {
            return new([], errors, warnings);
        }

        ValidateLetters(grid, errors);
        var runs = DeriveRuns(grid);
        ValidateClues(grid, runs, errors, warnings);
        ValidateCells(grid, runs, errors, warnings);
        return new(runs, errors, warnings);
    }

    private static void ValidateLetters(CrosswordGrid grid, List<CrosswordGridIssue> errors)
    {
        for (var row = 0; row < grid.Height; row++)
        {
            for (var column = 0; column < grid.Width; column++)
            {
                var letter = grid.Rows[row][column];
                if (letter != '#' && letter is not (>= 'A' and <= 'Z'))
                {
                    errors.Add(new("letter", "Solution cells must be uppercase A-Z or #.", row, column));
                }
            }
        }
    }

    private static List<CrosswordRun> DeriveRuns(CrosswordGrid grid)
    {
        var runs = new List<CrosswordRun>();
        var number = 0;
        for (var row = 0; row < grid.Height; row++)
        {
            for (var column = 0; column < grid.Width; column++)
            {
                AddCellRuns(grid, row, column, runs, ref number);
            }
        }
        return runs;
    }

    private static void AddCellRuns(CrosswordGrid grid, int row, int column, List<CrosswordRun> runs, ref int number)
    {
        if (grid.Rows[row][column] == '#')
        {
            return;
        }
        var across = IsStart(grid, row, column, CrosswordDirection.Across);
        var down = IsStart(grid, row, column, CrosswordDirection.Down);
        if (across || down)
        {
            number++;
        }
        if (across)
        {
            runs.Add(ReadRun(grid, number, row, column, CrosswordDirection.Across));
        }
        if (down)
        {
            runs.Add(ReadRun(grid, number, row, column, CrosswordDirection.Down));
        }
    }

    private static bool IsStart(CrosswordGrid grid, int row, int column, CrosswordDirection direction)
    {
        if (direction == CrosswordDirection.Across)
        {
            return (column == 0 || grid.Rows[row][column - 1] == '#')
                && column + 1 < grid.Width && grid.Rows[row][column + 1] != '#';
        }
        return (row == 0 || grid.Rows[row - 1][column] == '#')
            && row + 1 < grid.Height && grid.Rows[row + 1][column] != '#';
    }

    private static CrosswordRun ReadRun(CrosswordGrid grid, int number, int row, int column, CrosswordDirection direction)
    {
        var letters = new System.Text.StringBuilder();
        var r = row;
        var c = column;
        while (r < grid.Height && c < grid.Width && grid.Rows[r][c] != '#')
        {
            letters.Append(grid.Rows[r][c]);
            r += direction == CrosswordDirection.Down ? 1 : 0;
            c += direction == CrosswordDirection.Across ? 1 : 0;
        }
        return new(number, direction, row, column, letters.ToString());
    }

    private static void ValidateClues(CrosswordGrid grid, List<CrosswordRun> runs,
        List<CrosswordGridIssue> errors, List<CrosswordGridIssue> warnings)
    {
        var clues = grid.Clues.GroupBy(clue => (clue.Number, clue.Direction))
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var run in runs)
        {
            if (run.Answer.Length < 3)
            {
                errors.Add(Issue("short-entry", "Entries must have at least three letters.", run));
            }
            if (!clues.TryGetValue((run.Number, run.Direction), out var matching))
            {
                errors.Add(Issue("missing-entry", "Every white run needs a matching numbered clue.", run));
                continue;
            }
            if (matching.Length != 1)
            {
                errors.Add(Issue("duplicate-entry", "Number and direction must identify exactly one clue.", run));
            }
            foreach (var clue in matching)
            {
                ValidateClue(clue, run, errors, warnings);
            }
        }
        foreach (var clue in grid.Clues)
        {
            if (!runs.Any(run => run.Number == clue.Number && run.Direction == clue.Direction))
            {
                errors.Add(new("numbering", "Clue number and direction do not match a grid entry.",
                    Number: clue.Number, Direction: clue.Direction));
            }
        }
        foreach (var duplicate in runs.GroupBy(run => run.Answer).Where(group => group.Count() > 1))
        {
            warnings.Add(Issue("duplicate-answer", "An answer occurs more than once in this puzzle.", duplicate.First()));
        }
    }

    private static void ValidateClue(CrosswordClue clue, CrosswordRun run,
        List<CrosswordGridIssue> errors, List<CrosswordGridIssue> warnings)
    {
        if (clue.Answer.Length == 0 || clue.Answer.Any(letter => letter is not (>= 'A' and <= 'Z')))
        {
            errors.Add(Issue("answer-letters", "Answers must contain only uppercase A-Z.", run));
        }
        if (!string.Equals(clue.Answer, run.Answer, StringComparison.Ordinal))
        {
            errors.Add(Issue("answer-mismatch", "Answer disagrees with the grid, crossing, block or boundary.", run));
        }
        if (string.IsNullOrWhiteSpace(clue.Clue))
        {
            errors.Add(Issue("missing-clue", "Clue text is required.", run));
        }
        if (clue.Explanation?.Length > 300)
        {
            errors.Add(Issue("explanation-length", "Explanation must not exceed 300 characters.", run));
        }
        var words = clue.Clue.Split([' ', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '-'],
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains(run.Answer, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add(Issue("answer-in-clue", "Clue contains the answer word.", run));
        }
    }

    private static CrosswordGridIssue Issue(string code, string message, CrosswordRun run) =>
        new(code, message, run.Row, run.Column, run.Number, run.Direction);

    private static void ValidateCells(CrosswordGrid grid, List<CrosswordRun> runs,
        List<CrosswordGridIssue> errors, List<CrosswordGridIssue> warnings)
    {
        var white = new HashSet<(int Row, int Column)>();
        var coverage = CountCellCoverage(runs);
        var asymmetric = false;
        for (var row = 0; row < grid.Height; row++)
        {
            for (var column = 0; column < grid.Width; column++)
            {
                var block = grid.Rows[row][column] == '#';
                asymmetric |= block != (grid.Rows[grid.Height - row - 1][grid.Width - column - 1] == '#');
                if (!block)
                {
                    white.Add((row, column));
                    if (!coverage.ContainsKey((row, column)))
                    {
                        errors.Add(new("orphan-cell", "White cell belongs to no entry.", row, column));
                    }
                }
            }
        }
        if (white.Count == 0 || !IsConnected(white))
        {
            errors.Add(new("connectivity", "All white cells must form one connected grid."));
        }
        if (asymmetric)
        {
            warnings.Add(new("symmetry", "Blocks are not rotationally symmetric."));
        }
        var uncheckedCell = white.Where(cell => coverage.GetValueOrDefault(cell) < 2).Cast<(int Row, int Column)?>().FirstOrDefault();
        if (uncheckedCell is { } cellWithWarning)
        {
            warnings.Add(new("unchecked", "Some white cells belong to fewer than two entries (British style).",
                cellWithWarning.Row, cellWithWarning.Column));
        }
    }

    private static Dictionary<(int Row, int Column), int> CountCellCoverage(List<CrosswordRun> runs)
    {
        var coverage = new Dictionary<(int Row, int Column), int>();
        foreach (var run in runs)
        {
            for (var index = 0; index < run.Answer.Length; index++)
            {
                var cell = (run.Row + (run.Direction == CrosswordDirection.Down ? index : 0),
                    run.Column + (run.Direction == CrosswordDirection.Across ? index : 0));
                coverage[cell] = coverage.GetValueOrDefault(cell) + 1;
            }
        }
        return coverage;
    }

    private static bool IsConnected(HashSet<(int Row, int Column)> white)
    {
        var seen = new HashSet<(int Row, int Column)>();
        var queue = new Queue<(int Row, int Column)>();
        queue.Enqueue(white.First());
        while (queue.TryDequeue(out var cell))
        {
            if (!white.Contains(cell) || !seen.Add(cell))
            {
                continue;
            }
            queue.Enqueue((cell.Row - 1, cell.Column));
            queue.Enqueue((cell.Row + 1, cell.Column));
            queue.Enqueue((cell.Row, cell.Column - 1));
            queue.Enqueue((cell.Row, cell.Column + 1));
        }
        return seen.Count == white.Count;
    }
}

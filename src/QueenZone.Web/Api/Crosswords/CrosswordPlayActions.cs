using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>Shared play rules for bearer API routes and antiforgery-protected website handlers.</summary>
internal static class CrosswordPlayActions
{
    public static async Task<CrosswordCheckResultDto> CheckAsync(CrosswordCatalogItem puzzle, CrosswordCheckRequestDto request,
        Guid? member, ICrosswordProgressRepository progress, CancellationToken cancellationToken)
    {
        CrosswordPlayRules.EnsureVersion(puzzle, request.PlayVersion);
        ArgumentNullException.ThrowIfNull(request.Selection);
        var selection = request.Selection.ToSelection();
        var letters = CrosswordPlayRules.ExpandLetters(puzzle.Seed.Grid, request.Letters, selection);
        var cells = CrosswordPlayRules.Check(puzzle.Seed.Grid, letters, selection);
        if (request.AutoCheck && member is { } id)
            await progress.MarkAssistanceAsync(puzzle.Id, id, [], true, request.PlayVersion, cancellationToken);
        return new(cells, Explanations(puzzle.Seed.Grid, letters, []), CrosswordPlayRules.IsComplete(puzzle.Seed.Grid, letters), puzzle.PlayVersion);
    }

    public static async Task<CrosswordRevealResultDto> RevealAsync(CrosswordCatalogItem puzzle, CrosswordRevealRequestDto request,
        Guid? member, ICrosswordProgressRepository progress, CancellationToken cancellationToken)
    {
        CrosswordPlayRules.EnsureVersion(puzzle, request.PlayVersion);
        ArgumentNullException.ThrowIfNull(request.Selection);
        var cells = CrosswordPlayRules.Reveal(puzzle.Seed.Grid, request.Selection.ToSelection());
        var indices = cells.Select(cell => cell.Index).ToArray();
        if (member is { } id)
            await progress.MarkAssistanceAsync(puzzle.Id, id, indices, false, request.PlayVersion, cancellationToken);
        return new(cells, Explanations(puzzle.Seed.Grid, CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), indices), false, puzzle.PlayVersion);
    }

    public static async Task<CrosswordCompletionResultDto> CompleteAsync(CrosswordCatalogItem puzzle, CrosswordProgressRequestDto request,
        Guid member, ICrosswordProgressRepository progress, CancellationToken cancellationToken)
    {
        var result = await progress.CompleteAsync(puzzle.Id, member, request.ToWrite(), cancellationToken);
        var completion = result.Completion is { } completed ? new CrosswordCompletionDto(completed.ElapsedSeconds,
            completed.Clean, completed.RankingEligible, completed.CompletedAt) : null;
        IReadOnlyList<CrosswordAnswerReviewDto> review = result.Correct ? puzzle.Seed.Grid.Clues.Select(clue =>
            new CrosswordAnswerReviewDto(clue.Number, Direction(clue.Direction), clue.Answer, clue.Explanation)).ToArray() : [];
        return new(result.Correct, completion, review, puzzle.PlayVersion);
    }

    public static CrosswordProgressDto Progress(CrosswordProgress saved) => new(saved.Letters, saved.ElapsedSeconds,
        saved.RevealedCells, saved.AutoCheckUsed, saved.UpdatedAt, saved.StartedAt, saved.PlayVersion);

    private static IReadOnlyList<CrosswordExplanationDto> Explanations(CrosswordGrid grid, string letters, IReadOnlyList<int> revealed) =>
        CrosswordPlayRules.GetExplanations(grid, letters, revealed).Select(item =>
            new CrosswordExplanationDto(item.Number, Direction(item.Direction), item.Explanation)).ToArray();

    private static string Direction(CrosswordDirection direction) => direction == CrosswordDirection.Across ? "across" : "down";
}

using QueenZone.Data;

namespace QueenZone.Web;

public sealed record CrosswordSelectionDto(string Scope, int? Cell = null, int? Number = null, string? Direction = null)
{
    internal CrosswordSelection ToSelection() => new(Scope, Cell, Number, Direction switch
    {
        null => null,
        "across" => CrosswordDirection.Across,
        "down" => CrosswordDirection.Down,
        _ => throw new ArgumentException("Direction must be across or down.")
    });
}

public sealed record CrosswordCheckRequestDto(string Letters, CrosswordSelectionDto Selection, Guid PlayVersion, bool AutoCheck = false);
public sealed record CrosswordRevealRequestDto(CrosswordSelectionDto Selection, Guid PlayVersion);
public sealed record CrosswordExplanationDto(int Number, string Direction, string Explanation);
public sealed record CrosswordCheckResultDto(IReadOnlyList<CrosswordCellCheck> Cells,
    IReadOnlyList<CrosswordExplanationDto> Explanations, bool Complete, Guid PlayVersion);
public sealed record CrosswordRevealResultDto(IReadOnlyList<CrosswordRevealedCell> Cells,
    IReadOnlyList<CrosswordExplanationDto> Explanations, bool Clean, Guid PlayVersion);
public sealed record CrosswordProgressDto(string Letters, int ElapsedSeconds, IReadOnlyList<int> RevealedCells,
    bool AutoCheckUsed, DateTimeOffset UpdatedAt, DateTimeOffset StartedAt, Guid PlayVersion);
public sealed record CrosswordProgressRequestDto(string Letters, int ElapsedSeconds, IReadOnlyList<int> RevealedCells,
    bool AutoCheckUsed, DateTimeOffset UpdatedAt, Guid PlayVersion)
{
    internal CrosswordProgressWrite ToWrite() => new(Letters, ElapsedSeconds, RevealedCells, AutoCheckUsed, UpdatedAt, PlayVersion);
}
public sealed record CrosswordAnswerReviewDto(int Number, string Direction, string Answer, string? Explanation);
public sealed record CrosswordCompletionDto(int ElapsedSeconds, bool Clean, bool RankingEligible, DateTimeOffset CompletedAt);
public sealed record CrosswordCompletionResultDto(bool Correct, CrosswordCompletionDto? Completion,
    IReadOnlyList<CrosswordAnswerReviewDto> Review, Guid PlayVersion);

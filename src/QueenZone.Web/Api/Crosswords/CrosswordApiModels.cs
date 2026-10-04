namespace QueenZone.Web;

public sealed record CrosswordListItemDto(Guid Id, string Slug, string Title, string Difficulty,
    int Width, int Height, DateTimeOffset? PublishedAt, string? Progress = null,
    int? ProgressPercent = null, int? ElapsedSeconds = null);

public sealed record CrosswordPlayClueDto(int Number, string Direction, int Row, int Column,
    int Length, string Clue, string Enumeration);

/// <summary>Public play contract excludes answers, solution rows and explanations.</summary>
public sealed record CrosswordDetailDto(Guid Id, string Slug, string Title, string Description,
    string Difficulty, string Style, int Width, int Height, bool Archived,
    IReadOnlyList<bool> Blocks, IReadOnlyList<int> Numbering, IReadOnlyList<CrosswordPlayClueDto> Clues,
    Guid PlayVersion);

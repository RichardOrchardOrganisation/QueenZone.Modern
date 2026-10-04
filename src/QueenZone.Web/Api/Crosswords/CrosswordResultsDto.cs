namespace QueenZone.Web;

public sealed record CrosswordLeaderboardEntryDto(int Rank, string DisplayName, int ElapsedSeconds, DateTimeOffset CompletedAt);
public sealed record CrosswordLeaderboardDto(IReadOnlyList<CrosswordLeaderboardEntryDto> Top, CrosswordLeaderboardEntryDto? Viewer, int TotalMembers);
public sealed record CrosswordHistoryEntryDto(Guid Id, string? Slug, string Title, int ElapsedSeconds, bool Clean, DateTimeOffset CompletedAt, bool Playable);
public sealed record CrosswordHistoryDto(IReadOnlyList<CrosswordHistoryEntryDto> Items, int TotalCompleted, int WeeklyStreak, string WeekTimeZone);

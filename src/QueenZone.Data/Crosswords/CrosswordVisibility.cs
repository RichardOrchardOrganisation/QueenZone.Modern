using QueenZone.Data.Entities;

namespace QueenZone.Data;

public static class CrosswordVisibility
{
    public static bool IsListed(CrosswordCatalogItem puzzle, DateTimeOffset now) =>
        puzzle.Status is CrosswordStatus.Published or CrosswordStatus.Scheduled
        && puzzle.PublishAt is not null && puzzle.PublishAt <= now;

    public static bool IsPlayable(CrosswordCatalogItem puzzle, DateTimeOffset now) =>
        IsListed(puzzle, now)
        || (puzzle.Status == CrosswordStatus.Archived && puzzle.PublishedAt is not null && puzzle.PublishedAt <= now);
}

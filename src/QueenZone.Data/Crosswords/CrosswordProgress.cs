using System.Text.Json;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed record CrosswordProgressWrite(string Letters, int ElapsedSeconds, IReadOnlyList<int> RevealedCells,
    bool AutoCheckUsed, DateTimeOffset UpdatedAt, Guid PlayVersion);
public sealed record CrosswordProgress(Guid CrosswordId, string Letters, int ElapsedSeconds,
    IReadOnlyList<int> RevealedCells, bool AutoCheckUsed, DateTimeOffset UpdatedAt, DateTimeOffset StartedAt, Guid PlayVersion);
public sealed record CrosswordCompletion(Guid CrosswordId, Guid MemberId, int ElapsedSeconds,
    bool Clean, bool RankingEligible, DateTimeOffset CompletedAt);
public sealed record CrosswordCompletionResult(bool Correct, CrosswordCompletion? Completion);

/// <summary>The minimum ranking time scales with the total grid area, including blocks.</summary>
public sealed class CrosswordRankingOptions
{
    public int MinimumSecondsPer25Cells { get; set; } = 10;
}

public interface ICrosswordProgressRepository
{
    Task<CrosswordProgress?> GetAsync(Guid crosswordId, Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrosswordProgress>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrosswordCompletion>> GetCompletionsAsync(Guid? crosswordId, Guid? memberId,
        CancellationToken cancellationToken = default);
    Task<CrosswordProgress> SaveAsync(Guid crosswordId, Guid memberId, CrosswordProgressWrite write,
        CancellationToken cancellationToken = default);
    Task MarkAssistanceAsync(Guid crosswordId, Guid memberId, IReadOnlyList<int> revealedCells, bool autoCheckUsed,
        Guid playVersion, CancellationToken cancellationToken = default);
    Task<CrosswordCompletionResult> CompleteAsync(Guid crosswordId, Guid memberId, CrosswordProgressWrite write,
        CancellationToken cancellationToken = default);
}

internal static class CrosswordProgressMapping
{
    public static CrosswordProgressEntity Create(CrosswordCatalogItem puzzle, Guid memberId, DateTimeOffset now)
    {
        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A member is required.", nameof(memberId));
        }
        return new()
        {
            Id = Guid.NewGuid(),
            CrosswordId = puzzle.Id,
            MemberId = memberId,
            PlayVersion = puzzle.PlayVersion,
            GridFingerprint = CrosswordPlayRules.GridFingerprint(puzzle.Seed.Grid),
            Letters = CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid),
            RevealedCellsJson = "[]",
            StartedAt = now,
            UpdatedAt = DateTimeOffset.MinValue
        };
    }

    public static CrosswordProgress Read(CrosswordProgressEntity entity) => new(entity.CrosswordId, entity.Letters,
        entity.ElapsedSeconds, RevealedCells(entity), entity.AutoCheckUsed, entity.UpdatedAt, entity.StartedAt, entity.PlayVersion);

    public static void EnsureGrid(CrosswordProgressEntity entity, CrosswordCatalogItem puzzle)
    {
        if (entity.PlayVersion != puzzle.PlayVersion || entity.GridFingerprint != CrosswordPlayRules.GridFingerprint(puzzle.Seed.Grid))
        {
            throw new OptimisticConcurrencyException("This crossword has changed. Reload before continuing.");
        }
    }

    public static CrosswordProgressWrite Normalize(CrosswordGrid grid, CrosswordProgressWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.ElapsedSeconds < 0)
        {
            throw new ArgumentException("Elapsed seconds cannot be negative.", nameof(write));
        }
        if (write.UpdatedAt == DateTimeOffset.MinValue)
        {
            throw new ArgumentException("A progress update timestamp is required.", nameof(write));
        }
        var cells = NormalizeReveals(grid, write.RevealedCells);
        return write with
        {
            Letters = CrosswordPlayRules.NormalizeLetters(grid, write.Letters),
            RevealedCells = cells,
            UpdatedAt = write.UpdatedAt.ToUniversalTime()
        };
    }

    public static IReadOnlyList<int> NormalizeReveals(CrosswordGrid grid, IReadOnlyList<int> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count > grid.Width * grid.Height)
        {
            throw new ArgumentException("Too many revealed cells.", nameof(cells));
        }
        foreach (var cell in cells)
        {
            CrosswordPlayRules.SelectCells(grid, new("cell", cell));
        }
        return cells.Distinct().Order().ToArray();
    }

    public static void Apply(CrosswordProgressEntity entity, CrosswordProgressWrite write)
    {
        MarkAssistance(entity, write.RevealedCells, write.AutoCheckUsed);
        if (write.UpdatedAt <= entity.UpdatedAt)
        {
            return;
        }
        entity.Letters = write.Letters;
        entity.ElapsedSeconds = write.ElapsedSeconds;
        entity.UpdatedAt = write.UpdatedAt;
    }

    public static void MarkAssistance(CrosswordProgressEntity entity, IReadOnlyList<int> cells, bool autoCheckUsed)
    {
        entity.RevealedCellsJson = JsonSerializer.Serialize(RevealedCells(entity).Concat(cells).Distinct().Order());
        entity.AutoCheckUsed |= autoCheckUsed;
    }

    public static CrosswordCompletionEntity Complete(CrosswordProgressEntity progress, CrosswordCatalogItem puzzle,
        DateTimeOffset now, CrosswordRankingOptions options)
    {
        var clean = !progress.AutoCheckUsed && RevealedCells(progress).Count == 0;
        var minimum = Math.Ceiling(Math.Max(1, options.MinimumSecondsPer25Cells) *
            (double)puzzle.Seed.Grid.Width * puzzle.Seed.Grid.Height / 25);
        return new()
        {
            Id = Guid.NewGuid(),
            CrosswordId = progress.CrosswordId,
            MemberId = progress.MemberId,
            ElapsedSeconds = progress.ElapsedSeconds,
            Clean = clean,
            RankingEligible = clean && progress.ElapsedSeconds >= minimum,
            CompletedAt = now
        };
    }

    public static CrosswordCompletion Read(CrosswordCompletionEntity entity) => new(entity.CrosswordId,
        entity.MemberId, entity.ElapsedSeconds, entity.Clean, entity.RankingEligible, entity.CompletedAt);

    private static IReadOnlyList<int> RevealedCells(CrosswordProgressEntity entity) =>
        JsonSerializer.Deserialize<int[]>(entity.RevealedCellsJson) ?? [];
}

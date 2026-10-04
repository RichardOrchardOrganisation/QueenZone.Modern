using System.Text;
using System.Text.Json;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

internal static class CrosswordCatalogMapping
{
    public static CrosswordSeed Normalize(CrosswordSeed seed, bool playable)
    {
        if (seed.Grid.Clues.Any(clue => clue.Direction is not (CrosswordDirection.Across or CrosswordDirection.Down)))
        {
            throw new ArgumentException("Clue direction must be Across or Down.", nameof(seed));
        }
        var bytes = Encoding.UTF8.GetBytes(CrosswordSeedJson.Export(seed));
        var result = playable ? CrosswordSeedJson.Parse(bytes) : CrosswordSeedJson.ParseDraft(bytes);
        if (result.Seed is null || (playable && !result.IsValid))
        {
            throw new ArgumentException(string.Join("; ", result.Errors.Select(issue => issue.Code + ": " + issue.Message)), nameof(seed));
        }
        if (JsonSerializer.Serialize(seed.Grid.Rows).Length > 2000 || seed.Grid.Rows.Sum(row => row.Length) > 225)
        {
            throw new ArgumentException("Solution rows exceed the draft storage limit.", nameof(seed));
        }
        if (seed.Grid.Clues.Select(clue => (clue.Number, clue.Direction)).Distinct().Count() != seed.Grid.Clues.Count)
        {
            throw new ArgumentException("Each draft clue must have a unique number and direction.", nameof(seed));
        }
        return result.Seed;
    }

    public static IReadOnlyList<CrosswordSeed> NormalizeBatch(IReadOnlyList<CrosswordSeed> seeds)
    {
        var normalized = seeds.Select(seed => Normalize(seed, playable: true)).ToArray();
        if (seeds.Select(seed => seed.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != seeds.Count)
        {
            throw new ArgumentException("Duplicate slugs in the import batch.", nameof(seeds));
        }
        return normalized;
    }

    public static void ValidateActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 320)
        {
            throw new ArgumentException("Actor is required and must not exceed 320 characters.", nameof(actor));
        }
    }

    public static CrosswordEntity Create(CrosswordSeed seed, Guid creatorId, string actor, DateTimeOffset now, bool publish)
    {
        var entity = new CrosswordEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            CreatedByMemberId = creatorId,
            Status = publish ? CrosswordStatus.Published : CrosswordStatus.Draft,
            PublishedAt = publish ? now : null,
            PublishAt = publish ? now : null
        };
        Apply(entity, seed, actor, now);
        return entity;
    }

    public static void Apply(CrosswordEntity entity, CrosswordSeed seed, string actor, DateTimeOffset now)
    {
        entity.Slug = seed.Slug;
        entity.Title = seed.Title;
        entity.Description = seed.Description;
        entity.Difficulty = seed.Difficulty;
        entity.Style = seed.Style;
        entity.Width = seed.Grid.Width;
        entity.Height = seed.Grid.Height;
        entity.BlockMask = string.Concat(seed.Grid.Rows).ReplaceLettersWithWhiteCells();
        entity.SolutionRowsJson = JsonSerializer.Serialize(seed.Grid.Rows);
        entity.UpdatedAt = now;
        entity.UpdatedByEmail = actor;
        var runs = CrosswordGridValidator.Validate(seed.Grid).Runs;
        entity.Entries = seed.Grid.Clues.Select(clue =>
        {
            var run = runs.FirstOrDefault(run => run.Number == clue.Number && run.Direction == clue.Direction);
            return new CrosswordEntryEntity
            {
                Id = Guid.NewGuid(),
                CrosswordId = entity.Id,
                Number = clue.Number,
                Direction = clue.Direction,
                Row = run?.Row ?? 0,
                Column = run?.Column ?? 0,
                Answer = clue.Answer,
                Clue = clue.Clue,
                Enumeration = clue.Enumeration,
                Explanation = clue.Explanation
            };
        }).ToList();
    }

    public static CrosswordCatalogItem Read(CrosswordEntity entity)
    {
        var rows = JsonSerializer.Deserialize<string[]>(entity.SolutionRowsJson) ?? [];
        var grid = new CrosswordGrid(entity.Width, entity.Height, rows,
            entity.Entries.OrderBy(entry => entry.Number).ThenBy(entry => entry.Direction)
                .Select(entry => new CrosswordClue(entry.Number, entry.Direction, entry.Answer, entry.Clue,
                    entry.Enumeration, entry.Explanation)).ToArray());
        return new(entity.Id, new(entity.Slug, entity.Title, entity.Description, entity.Difficulty, entity.Style, grid),
            entity.Status, entity.PublishAt, entity.PublishedAt, entity.CreatedAt, entity.CreatedByMemberId,
            entity.UpdatedAt, entity.UpdatedByEmail, entity.RowVersion.ToArray());
    }

    private static string ReplaceLettersWithWhiteCells(this string rows) =>
        new(rows.Select(cell => cell == '#' ? '#' : '.').ToArray());
}

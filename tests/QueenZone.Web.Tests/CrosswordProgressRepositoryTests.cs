using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordProgressRepositoryTests
{
    private static readonly Guid Member = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Other = Guid.Parse("11111111-2222-3333-4444-666666666666");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Progress_is_private_defensive_and_last_write_wins(bool ef) => Run(ef, async (repository, catalog, clock, puzzle) =>
    {
        Assert.Null(await repository.GetAsync(puzzle.Id, Member));
        var blank = CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid);
        var first = new CrosswordProgressWrite(blank.ToLowerInvariant(), 12, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        var saved = await repository.SaveAsync(puzzle.Id, Member, first);
        Assert.Equal(blank, saved.Letters);
        Assert.Equal(clock.GetUtcNow(), saved.StartedAt);
        Assert.Null(await repository.GetAsync(puzzle.Id, Other));
        clock.Advance(TimeSpan.FromSeconds(5));
        var newer = first with { Letters = string.Concat(puzzle.Seed.Grid.Rows), ElapsedSeconds = 17, UpdatedAt = clock.GetUtcNow() };
        await repository.SaveAsync(puzzle.Id, Member, newer);
        await repository.SaveAsync(puzzle.Id, Member, first);
        await repository.SaveAsync(puzzle.Id, Member, first with { UpdatedAt = newer.UpdatedAt });
        var loaded = (await repository.GetAsync(puzzle.Id, Member))!;
        Assert.Equal(newer.Letters, loaded.Letters);
        Assert.Equal(17, loaded.ElapsedSeconds);
        Assert.Equal(newer.UpdatedAt, loaded.UpdatedAt);
        Assert.Single(await repository.GetForMemberAsync(Member));
        Assert.Empty(await repository.GetForMemberAsync(Other));
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, puzzle.RowVersion, "editor");
        Assert.Equivalent(loaded, await repository.GetAsync(puzzle.Id, Member));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SaveAsync(puzzle.Id, Member, newer));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Assists_are_monotonic_even_when_a_stale_device_saves_clean_flags(bool ef) => Run(ef, async (repository, _, clock, puzzle) =>
    {
        var grid = puzzle.Seed.Grid;
        var cells = CrosswordPlayRules.SelectCells(grid, new("grid"));
        await repository.MarkAssistanceAsync(puzzle.Id, Member, [cells[0]], false, puzzle.PlayVersion);
        await repository.MarkAssistanceAsync(puzzle.Id, Member, [cells[1], cells[0]], true, puzzle.PlayVersion);
        var write = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(grid), 120, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        await repository.SaveAsync(puzzle.Id, Member, write);
        clock.Advance(TimeSpan.FromSeconds(1));
        var stale = write with { UpdatedAt = write.UpdatedAt.AddSeconds(-1), RevealedCells = [cells[2]] };
        await repository.SaveAsync(puzzle.Id, Member, stale);
        var loaded = (await repository.GetAsync(puzzle.Id, Member))!;
        Assert.Equal(cells.Take(3).Order(), loaded.RevealedCells);
        Assert.True(loaded.AutoCheckUsed);
        Assert.Equal(120, loaded.ElapsedSeconds);
        ((int[])loaded.RevealedCells)[0] = 999;
        Assert.DoesNotContain(999, (await repository.GetAsync(puzzle.Id, Member))!.RevealedCells);
        var result = await repository.CompleteAsync(puzzle.Id, Member,
            write with { Letters = string.Concat(grid.Rows), UpdatedAt = clock.GetUtcNow() });
        Assert.True(result.Correct);
        Assert.False(result.Completion!.Clean);
        Assert.False(result.Completion.RankingEligible);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Server_checks_completion_and_first_success_is_immutable(bool ef) => Run(ef, async (repository, _, clock, puzzle) =>
    {
        var write = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        var wrong = await repository.CompleteAsync(puzzle.Id, Member, write);
        Assert.False(wrong.Correct);
        Assert.Null(wrong.Completion);
        Assert.Empty(await repository.GetCompletionsAsync(puzzle.Id, Member));
        clock.Advance(TimeSpan.FromSeconds(1));
        var correct = write with { Letters = string.Concat(puzzle.Seed.Grid.Rows), UpdatedAt = clock.GetUtcNow() };
        var result = await repository.CompleteAsync(puzzle.Id, Member, correct);
        Assert.True(result.Correct);
        Assert.True(result.Completion!.Clean);
        Assert.True(result.Completion.RankingEligible);
        Assert.Equal(clock.GetUtcNow(), result.Completion.CompletedAt);
        var saved = (await repository.GetAsync(puzzle.Id, Member))!;
        clock.Advance(TimeSpan.FromHours(1));
        var replay = await repository.CompleteAsync(puzzle.Id, Member,
            write with { ElapsedSeconds = 200, UpdatedAt = clock.GetUtcNow(), AutoCheckUsed = true });
        Assert.Equal(result.Completion, replay.Completion);
        await repository.SaveAsync(puzzle.Id, Member, write with { UpdatedAt = clock.GetUtcNow() });
        await repository.MarkAssistanceAsync(puzzle.Id, Member, [], true, puzzle.PlayVersion);
        Assert.Equivalent(saved, await repository.GetAsync(puzzle.Id, Member));
        Assert.Single(await repository.GetCompletionsAsync(null, Member));
        Assert.Single(await repository.GetCompletionsAsync(puzzle.Id, null));
        Assert.Empty(await repository.GetCompletionsAsync(puzzle.Id, Other));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stale_correct_submission_cannot_complete_a_newer_incorrect_grid(bool ef) => Run(ef, async (repository, _, clock, puzzle) =>
    {
        var originalTime = clock.GetUtcNow();
        var latest = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, originalTime.AddSeconds(1), puzzle.PlayVersion);
        await repository.SaveAsync(puzzle.Id, Member, latest);
        var result = await repository.CompleteAsync(puzzle.Id, Member,
            latest with { Letters = string.Concat(puzzle.Seed.Grid.Rows), UpdatedAt = originalTime });
        Assert.False(result.Correct);
        Assert.Empty(await repository.GetCompletionsAsync(null, null));
    });

    [Theory]
    [InlineData(false, 19, false)]
    [InlineData(false, 20, true)]
    [InlineData(true, 19, false)]
    [InlineData(true, 20, true)]
    public Task Implausible_time_completes_but_is_excluded_from_ranking(bool ef, int seconds, bool ranked) => Run(ef, async (repository, _, clock, puzzle) =>
    {
        var result = await repository.CompleteAsync(puzzle.Id, Member,
            new(string.Concat(puzzle.Seed.Grid.Rows), seconds, [], false, clock.GetUtcNow(), puzzle.PlayVersion));
        Assert.True(result.Correct);
        Assert.True(result.Completion!.Clean);
        Assert.Equal(ranked, result.Completion.RankingEligible);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Invalid_input_does_not_create_or_change_progress(bool ef) => Run(ef, async (repository, _, clock, puzzle) =>
    {
        var valid = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 0, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Guid.Empty, valid));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { ElapsedSeconds = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { UpdatedAt = DateTimeOffset.MinValue }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { Letters = "ABC" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { RevealedCells = [999] }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.MarkAssistanceAsync(puzzle.Id, Member, Enumerable.Repeat(0, 226).ToArray(), false, puzzle.PlayVersion));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SaveAsync(puzzle.Id, Member, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { RevealedCells = null! }));
        Assert.Null(await repository.GetAsync(puzzle.Id, Member));
        await repository.SaveAsync(puzzle.Id, Member, valid);
        var before = await repository.GetAsync(puzzle.Id, Member);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(puzzle.Id, Member, valid with { Letters = "ABC" }));
        Assert.Equivalent(before, await repository.GetAsync(puzzle.Id, Member));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Changed_grid_rejects_old_progress_without_erasing_it_or_completed_results(bool ef) => Run(ef, async (repository, catalog, clock, puzzle) =>
    {
        var write = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        await repository.SaveAsync(puzzle.Id, Other, write);
        var result = await repository.CompleteAsync(puzzle.Id, Member, write with { Letters = string.Concat(puzzle.Seed.Grid.Rows) });
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, puzzle.RowVersion, "editor");
        var draft = (await catalog.GetByIdAsync(puzzle.Id))!;
        var changed = draft.Seed with
        {
            Grid = draft.Seed.Grid with
            {
                Rows = draft.Seed.Grid.Rows.Select(row => row.Replace('A', 'Z')).ToArray(),
                Clues = draft.Seed.Grid.Clues.Select(clue => clue with { Answer = clue.Answer.Replace('A', 'Z') }).ToArray()
            }
        };
        await catalog.SaveDraftAsync(puzzle.Id, changed, draft.RowVersion, "editor");
        draft = (await catalog.GetByIdAsync(puzzle.Id))!;
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Published, null, draft.RowVersion, "editor");
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.SaveAsync(puzzle.Id, Other, write));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.MarkAssistanceAsync(puzzle.Id, Other, [], true, puzzle.PlayVersion));
        Assert.Equal(write.Letters, (await repository.GetAsync(puzzle.Id, Other))!.Letters);
        Assert.Equal(result.Completion, Assert.Single(await repository.GetCompletionsAsync(puzzle.Id, Member)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Editorial_text_changes_preserve_version_and_allow_existing_progress(bool ef) => Run(ef, async (repository, catalog, clock, puzzle) =>
    {
        var write = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, clock.GetUtcNow(), puzzle.PlayVersion);
        await repository.SaveAsync(puzzle.Id, Member, write);
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Draft, null, puzzle.RowVersion, "editor");
        var draft = (await catalog.GetByIdAsync(puzzle.Id))!;
        var changed = draft.Seed with
        {
            Title = "Edited title",
            Grid = draft.Seed.Grid with { Clues = draft.Seed.Grid.Clues.Select(clue => clue with { Clue = clue.Clue + " updated", Explanation = "Updated fact" }).ToArray() }
        };
        await catalog.SaveDraftAsync(puzzle.Id, changed, draft.RowVersion, "editor");
        draft = (await catalog.GetByIdAsync(puzzle.Id))!;
        Assert.NotEqual(Guid.Empty, draft.PlayVersion);
        Assert.Equal(puzzle.PlayVersion, draft.PlayVersion);
        await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Published, null, draft.RowVersion, "editor");
        clock.Advance(TimeSpan.FromSeconds(1));
        var saved = await repository.SaveAsync(puzzle.Id, Member, write with { UpdatedAt = clock.GetUtcNow() });
        Assert.Equal(puzzle.PlayVersion, saved.PlayVersion);
    });

    private static async Task Run(bool ef, Func<ICrosswordProgressRepository, ICrosswordCatalogRepository, FakeTimeProvider, CrosswordCatalogItem, Task> scenario)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        if (!ef)
        {
            var catalog = new InMemoryCrosswordCatalogRepository(clock);
            var puzzle = await Publish(catalog);
            await scenario(new InMemoryCrosswordProgressRepository(catalog, clock, new()), catalog, clock, puzzle);
            return;
        }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var efCatalog = new EfCrosswordCatalogRepository(db, clock);
        var efPuzzle = await Publish(efCatalog);
        await scenario(new EfCrosswordProgressRepository(db, clock, new()), efCatalog, clock, efPuzzle);
    }

    private static async Task<CrosswordCatalogItem> Publish(ICrosswordCatalogRepository catalog)
    {
        await catalog.ImportAsync([CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band")], Member, "seed", publish: true);
        return Assert.Single(await catalog.GetAllAsync());
    }
}

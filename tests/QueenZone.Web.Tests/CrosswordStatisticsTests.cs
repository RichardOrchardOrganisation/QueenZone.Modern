using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordStatisticsTests
{
    [Fact]
    public void Member_statistics_match_hand_counted_fixture_and_ignore_old_grid_reveal_coordinates()
    {
        var seed = CrosswordSampleData.Load().Single(seed => seed.Slug == "meet-the-band");
        var id = Guid.NewGuid(); var version = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var puzzle = new CrosswordCatalogItem(id, seed, CrosswordStatus.Published, now, now, now, Guid.NewGuid(), now, "editor", [1], version);
        var first = CrosswordGridValidator.Validate(seed.Grid).Runs[0];
        var cell = first.Row * seed.Grid.Width + first.Column;
        CrosswordProgress Progress(Guid playVersion, int[] reveals) => new(id, CrosswordPlayRules.EmptyLetters(seed.Grid), 10, reveals, false, now, now, playVersion);
        var starts = new[] { Progress(version, [cell]), Progress(version, [cell]), Progress(version, []), Progress(version, []), Progress(Guid.NewGuid(), [cell]) };
        var completed = new[] { 10, 30, 20, 40 }.Select((seconds, index) => new CrosswordCompletion(id, Guid.NewGuid(), seconds, index < 2, true, now)).ToArray();
        var stats = CrosswordStatistics.Calculate(puzzle, starts, completed);
        Assert.Equal(5, stats.Starts); Assert.Equal(4, stats.Completions); Assert.Equal(.8, stats.CompletionRate);
        Assert.Equal(.5, stats.CleanSolveRate); Assert.Equal(25, stats.MedianSeconds);
        Assert.Equal(2, Assert.Single(stats.MostRevealed, entry => entry.Number == first.Number && entry.Direction == first.Direction).Count);
        var odd = CrosswordStatistics.Calculate(puzzle, starts, completed.Take(3).ToArray());
        Assert.Equal(20, odd.MedianSeconds);
        var empty = CrosswordStatistics.Calculate(puzzle, [], []);
        Assert.Equal(0, empty.CompletionRate); Assert.Equal(0, empty.CleanSolveRate); Assert.Null(empty.MedianSeconds); Assert.Empty(empty.MostRevealed);
    }
}

using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>The only play-page projection: no solution letters or private clue explanations.</summary>
internal static class CrosswordPublicProjection
{
    public static CrosswordDetailDto Detail(CrosswordCatalogItem item)
    {
        var grid = item.Seed.Grid;
        var validation = CrosswordGridValidator.Validate(grid);
        var numbering = new int[grid.Width * grid.Height];
        foreach (var run in validation.Runs) numbering[run.Row * grid.Width + run.Column] = run.Number;
        var clues = validation.Runs.Select(run =>
        {
            var clue = grid.Clues.Single(clue => clue.Number == run.Number && clue.Direction == run.Direction);
            return new CrosswordPlayClueDto(run.Number, run.Direction == CrosswordDirection.Across ? "across" : "down",
                run.Row, run.Column, run.Answer.Length, clue.Clue, clue.Enumeration);
        }).ToArray();
        return new(item.Id, item.Seed.Slug, item.Seed.Title, item.Seed.Description, item.Seed.Difficulty, item.Seed.Style,
            grid.Width, grid.Height, item.Status == CrosswordStatus.Archived,
            string.Concat(grid.Rows).Select(cell => cell == '#').ToArray(), numbering, clues, item.PlayVersion);
    }
}

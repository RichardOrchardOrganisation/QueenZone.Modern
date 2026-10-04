using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public static class CrosswordApiEndpoints
{
    public const string RootPath = "/api/v1/crosswords";

    public static void MapCrosswordApiEndpoints(this WebApplication app)
    {
        var group = app.MapApiV1Group(RootPath, "Crosswords");
        group.MapPagedList<CrosswordListItemDto>("", GetListAsync, "GetCrosswords",
            "Published crosswords, including schedules whose publication time has arrived. Archives are omitted.");
        group.MapDetail<CrosswordDetailDto>("/{id:guid}", GetDetailAsync, "GetCrossword",
            "Solution-free crossword for play. Published archives remain playable by direct link with archived=true.");
    }

    internal static async Task<IResult> GetListAsync(HttpContext context, ICrosswordCatalogRepository catalog,
        int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var now = Clock(context).GetUtcNow();
        var items = (await catalog.GetAllAsync(cancellationToken))
            .Where(item => CrosswordVisibility.IsListed(item, now))
            .OrderByDescending(item => item.PublishedAt ?? item.PublishAt).ThenBy(item => item.Seed.Slug, StringComparer.Ordinal)
            .ToArray();
        context.Response.Headers.CacheControl = "no-store";
        return ApiV1EndpointHelpers.OkPagedSlice(items, page, pageSize, source => source.Select(item =>
            new CrosswordListItemDto(item.Id, item.Seed.Slug, item.Seed.Title, item.Seed.Difficulty,
                item.Seed.Grid.Width, item.Seed.Grid.Height, item.PublishedAt ?? item.PublishAt)).ToArray());
    }

    internal static async Task<IResult> GetDetailAsync(HttpContext context, ICrosswordCatalogRepository catalog,
        Guid id, CancellationToken cancellationToken)
    {
        var item = await catalog.GetByIdAsync(id, cancellationToken);
        if (item is null || !CrosswordVisibility.IsPlayable(item, Clock(context).GetUtcNow()))
        {
            return ApiV1EndpointHelpers.NotFound("No playable crossword with that id.");
        }
        var grid = item.Seed.Grid;
        var validation = CrosswordGridValidator.Validate(grid);
        var numbering = new int[grid.Width * grid.Height];
        foreach (var run in validation.Runs)
        {
            numbering[run.Row * grid.Width + run.Column] = run.Number;
        }
        var clues = validation.Runs.Select(run =>
        {
            var clue = grid.Clues.Single(clue => clue.Number == run.Number && clue.Direction == run.Direction);
            return new CrosswordPlayClueDto(run.Number, run.Direction == CrosswordDirection.Across ? "across" : "down",
                run.Row, run.Column, run.Answer.Length, clue.Clue, clue.Enumeration);
        }).ToArray();
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new CrosswordDetailDto(item.Id, item.Seed.Slug, item.Seed.Title, item.Seed.Description,
            item.Seed.Difficulty, item.Seed.Style, grid.Width, grid.Height, item.Status == CrosswordStatus.Archived,
            string.Concat(grid.Rows).Select(cell => cell == '#').ToArray(), numbering, clues));
    }

    private static TimeProvider Clock(HttpContext context) =>
        context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
}

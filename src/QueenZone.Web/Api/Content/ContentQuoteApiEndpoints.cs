using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Random quote, random trivia, and published quote detail routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentQuoteApiEndpoints
{
    internal static void MapContentQuoteApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/quotes/random", GetRandomQuoteAsync)
            .WithName("GetContentRandomQuote")
            .WithSummary("A single random published quote, matching the homepage widget. Intended for the mobile app's homescreen widget.")
            .Produces<QuoteDto?>();

        group.MapGet("/trivia/random", GetRandomTriviaAsync)
            .WithName("GetContentRandomTrivia")
            .WithSummary("A single random published trivia fact, matching the /trivia page. JSON null when none is published.")
            .Produces<TriviaDto?>();

        group.MapGet("/quotes/{id:int}", GetQuoteDetailAsync)
            .WithName("GetContentQuoteDetail")
            .WithSummary("A single published quote by id. Unpublished or missing quotes return 404.")
            .Produces<QuoteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetRandomQuoteAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var quote = await publicQueryCache.GetRandomPublishedQuoteAsync(cancellationToken);

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        QuoteDto? payload = quote is null ? null : ContentApiMapper.ToQuoteDto(quote);
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> GetRandomTriviaAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var fact = await publicQueryCache.GetRandomPublishedTriviaAsync(cancellationToken);

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        TriviaDto? payload = fact is null ? null : ContentApiMapper.ToTriviaDto(fact);
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> GetQuoteDetailAsync(
        IQuoteRepository quoteRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await quoteRepository.GetByIdAsync(id, cancellationToken);
        if (quote is null || !quote.IsPublished)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published quote with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToQuoteDto(quote));
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace QueenZone.Web.Tests;

public sealed class ApiV1EndpointHelperTests
{
    [Fact]
    public void NotFound_matches_problem_details_contract()
    {
        const string detail = "No album with id '4'.";
        var actual = Assert.IsType<ProblemHttpResult>(ApiV1EndpointHelpers.NotFound(detail));
        var expected = Assert.IsType<ProblemHttpResult>(Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: detail));

        Assert.Equal(expected.StatusCode, actual.StatusCode);
        Assert.Equal(expected.ProblemDetails.Title, actual.ProblemDetails.Title);
        Assert.Equal(expected.ProblemDetails.Detail, actual.ProblemDetails.Detail);
        Assert.Equal(expected.ProblemDetails.Type, actual.ProblemDetails.Type);
        Assert.Equal(StatusCodes.Status404NotFound, actual.StatusCode);
    }

    [Fact]
    public void OkPaged_matches_the_standard_list_envelope()
    {
        var actual = Assert.IsType<Ok<ApiPagedResponse<string>>>(
            ApiV1EndpointHelpers.OkPaged(["a", "b"], page: 2, pageSize: 2, totalCount: 5));

        Assert.Equal(["a", "b"], actual.Value!.Items);
        Assert.Equal(2, actual.Value.Page);
        Assert.Equal(2, actual.Value.PageSize);
        Assert.Equal(5, actual.Value.TotalCount);
        Assert.Equal(3, actual.Value.TotalPages);
    }

    [Fact]
    public void OkNoStorePaged_sets_cache_control_and_the_list_envelope()
    {
        var context = new DefaultHttpContext();
        var result = Assert.IsType<Ok<ApiPagedResponse<string>>>(
            ApiV1EndpointHelpers.OkNoStorePaged(context, ["only"], page: 1, pageSize: 20, totalCount: 1));

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(["only"], result.Value!.Items);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
    }

    [Theory]
    [InlineData(null, null, new string[] { "a", "b", "c", "d", "e" }, 1, 20, 5)]
    [InlineData(2, 2, new string[] { "c", "d" }, 2, 2, 5)]
    [InlineData(9, 2, new string[0], 9, 2, 5)]
    [InlineData(0, 1000, new string[] { "a", "b", "c", "d", "e" }, 1, 100, 5)]
    public void OkPagedSlice_clamps_and_maps_only_the_page(
        int? page,
        int? pageSize,
        string[] expectedItems,
        int expectedPage,
        int expectedPageSize,
        int expectedTotal)
    {
        var result = Assert.IsType<Ok<ApiPagedResponse<string>>>(ApiV1EndpointHelpers.OkPagedSlice(
            new[] { "a", "b", "c", "d", "e" },
            page,
            pageSize,
            items => items.Select(item => item).ToList()));

        Assert.Equal(expectedItems, result.Value!.Items);
        Assert.Equal(expectedPage, result.Value.Page);
        Assert.Equal(expectedPageSize, result.Value.PageSize);
        Assert.Equal(expectedTotal, result.Value.TotalCount);
    }

    [Fact]
    public void Map_helpers_keep_group_name_route_names_and_status_metadata()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        var app = builder.Build();
        var group = app.MapApiV1Group("/api/v1/sample", "Sample").DisableAntiforgery();
        group.MapPagedList<string>(
            "/items",
            (int? page, int? pageSize) => Results.Ok(ApiPagedResponse<string>.Create([], 1, 20, 0)),
            "GetSampleItems",
            "Paged sample.");
        group.MapAuthorizedPagedList<string>(
            "/mine",
            (int? page, int? pageSize) => Results.Ok(ApiPagedResponse<string>.Create([], 1, 20, 0)),
            "GetMySampleItems",
            "Paged member sample.");
        group.MapDetail<string>(
            "/items/{id:int}",
            (int id) => Results.Ok(id.ToString()),
            "GetSampleItem",
            "One sample.");

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var list = Assert.Single(endpoints, endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetSampleItems");
        Assert.Equal("/api/v1/sample/items", list.RoutePattern.RawText);
        Assert.Contains("Sample", list.Metadata.GetMetadata<ITagsMetadata>()!.Tags);
        Assert.Equal(ApiV1.OpenApiDocumentName, list.Metadata.GetMetadata<IEndpointGroupNameMetadata>()!.EndpointGroupName);
        Assert.Contains(
            list.Metadata.OfType<IProducesResponseTypeMetadata>(),
            metadata => metadata.Type == typeof(ApiPagedResponse<string>) && metadata.StatusCode == StatusCodes.Status200OK);

        var mine = Assert.Single(endpoints, endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetMySampleItems");
        Assert.Contains(
            mine.Metadata.OfType<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status401Unauthorized);

        var detail = Assert.Single(endpoints, endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetSampleItem");
        Assert.Equal("/api/v1/sample/items/{id:int}", detail.RoutePattern.RawText);
        Assert.Contains(
            detail.Metadata.OfType<IProducesResponseTypeMetadata>(),
            metadata => metadata.Type == typeof(string) && metadata.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(
            detail.Metadata.OfType<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status404NotFound);
    }
}

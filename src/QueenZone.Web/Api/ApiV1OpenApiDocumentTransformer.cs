using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using QueenZone.Data;

namespace QueenZone.Web;

internal sealed class ApiV1OpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "QueenZone API",
            Version = ApiV1.Version,
            Description =
                "Versioned JSON API for the QueenZone website and mobile app. " +
                "Additive changes stay in /api/v1; breaking changes require /api/v2. " +
                "JSON is camelCase with ISO-8601 UTC timestamps. " +
                "Resource errors use RFC 7807 Problem Details (application/problem+json). " +
                "OAuth2 token and authorize errors on /api/v1/auth/* use RFC 6749 " +
                "{ error, error_description } objects. " +
                "Collection endpoints paginate with page and pageSize query parameters " +
                $"(default page {ApiPagination.DefaultPage}, pageSize {ApiPagination.DefaultPageSize}, " +
                $"max {ApiPagination.MaxPageSize}, except where an operation specifies different limits) and return " +
                "{ items, page, pageSize, totalCount, totalPages }. " +
                "Narrow site endpoints such as /api/uploads, RSS, and media streaming are not part of this document.",
        };

        document.Components ??= new OpenApiComponents();
        document.AddComponent("bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Member access token from POST /api/v1/auth/token.",
        });

        DescribePagination(document, "/api/v1/forum/topics/{id}/posts",
            ForumRoutes.PostsPageSize, ForumRoutes.PostsPageSize);
        DescribePagination(document, "/api/v1/content/photos/categories/{slug}/items",
            PhotoRoutes.CategoryPageSize, PhotoRoutes.CategoryPageSize);
        DescribePagination(document, "/api/v1/me/messages",
            PrivateMessageLimits.InboxPageSize, PrivateMessageLimits.MaxInboxPageSize);
        DescribePagination(document, "/api/v1/me/messages/archived",
            PrivateMessageLimits.InboxPageSize, PrivateMessageLimits.MaxInboxPageSize);

        return Task.CompletedTask;
    }

    private static void DescribePagination(OpenApiDocument document, string path, int defaultPageSize, int maxPageSize)
    {
        if (document.Paths is not null
            && document.Paths.TryGetValue(path, out var item)
            && item.Operations is not null
            && item.Operations.TryGetValue(HttpMethod.Get, out var operation))
        {
            operation.Description = $"pageSize defaults to {defaultPageSize} and is clamped to a maximum of {maxPageSize}. " +
                "Values below 1 use the default; page defaults to 1.";
        }
    }
}

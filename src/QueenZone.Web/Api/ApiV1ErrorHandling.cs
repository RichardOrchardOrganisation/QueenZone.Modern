using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace QueenZone.Web;

/// <summary>
/// JSON Problem Details for <c>/api/v1</c> so the mobile client never receives the HTML error pages.
/// OAuth token/authorize responses that already wrote a body are left untouched.
/// </summary>
public static partial class ApiV1ErrorHandling
{
    /// <summary>
    /// Register after the HTML <c>UseExceptionHandler("/error")</c> so this inner
    /// handler writes JSON Problem Details for <c>/api/v1</c>. Do not put the HTML
    /// re-execute handler inside <c>UseWhen</c>: that breaks re-execution and turns
    /// unhandled exceptions into 404s.
    /// </summary>
    public static IApplicationBuilder UseApiV1ExceptionHandler(this IApplicationBuilder app)
    {
        app.UseWhen(
            static context => ApiV1.IsApiPath(context.Request.Path),
            branch => branch.UseExceptionHandler(errorApp => errorApp.Run(WriteUnhandledExceptionAsync)));
        return app;
    }

    /// <summary>
    /// Register immediately after <c>UseStatusCodePagesWithReExecute</c> on the main pipeline.
    /// Do not put the HTML re-execute handler inside <c>UseWhen</c>: that breaks re-execution and
    /// turns antiforgery 400s, 403s, and 429s into 404s.
    /// </summary>
    public static IApplicationBuilder UseApiV1StatusCodePages(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (ApiV1.IsApiPath(context.Request.Path))
            {
                var statusCodePages = context.Features.Get<IStatusCodePagesFeature>();
                if (statusCodePages is not null)
                {
                    statusCodePages.Enabled = false;
                }
            }

            await next();

            if (ApiV1.IsApiPath(context.Request.Path))
            {
                await WriteStatusCodeProblemAsync(context);
            }
        });
        return app;
    }

    internal static async Task WriteUnhandledExceptionAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("QueenZone.Web.Api");
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is not null)
        {
            Log.UnhandledExceptionOnRequest(
                logger,
                error,
                SanitizeHttpMethodForLog(context.Request.Method),
                SanitizeForLog(context.Request.Path.Value));
        }

        await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.")
            .ExecuteAsync(context);
    }

    internal static async Task WriteProblemDetailsForEmptyErrorsAsync(HttpContext http, RequestDelegate next)
    {
        await next(http);
        await WriteStatusCodeProblemAsync(http);
    }

    internal static async Task WriteStatusCodeProblemAsync(HttpContext http)
    {
        if (http.Response.StatusCode is < 400 or > 599
            || http.Response.HasStarted
            || !string.IsNullOrEmpty(http.Response.ContentType))
        {
            return;
        }

        http.Response.ContentLength = null;
        await Results.Problem(
                statusCode: http.Response.StatusCode,
                title: ReasonPhrases.GetReasonPhrase(http.Response.StatusCode))
            .ExecuteAsync(http);
    }

    /// <summary>
    /// Maps the request method to a known HTTP verb so attacker-controlled tokens are never logged.
    /// </summary>
    internal static string SanitizeHttpMethodForLog(string? method)
    {
        if (string.IsNullOrEmpty(method))
        {
            return "OTHER";
        }

        if (HttpMethods.IsGet(method))
        {
            return HttpMethods.Get;
        }

        if (HttpMethods.IsPost(method))
        {
            return HttpMethods.Post;
        }

        if (HttpMethods.IsPut(method))
        {
            return HttpMethods.Put;
        }

        if (HttpMethods.IsDelete(method))
        {
            return HttpMethods.Delete;
        }

        if (HttpMethods.IsPatch(method))
        {
            return HttpMethods.Patch;
        }

        if (HttpMethods.IsHead(method))
        {
            return HttpMethods.Head;
        }

        if (HttpMethods.IsOptions(method))
        {
            return HttpMethods.Options;
        }

        return "OTHER";
    }

    /// <summary>
    /// Strips CR/LF from user-controlled path text before it is written to logs, and caps length.
    /// </summary>
    internal static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        const int maxLength = 256;
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }
}

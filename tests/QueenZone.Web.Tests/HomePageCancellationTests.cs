using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web.Pages;

namespace QueenZone.Web.Tests;

public sealed class HomePageCancellationTests(QueenZoneWebApplicationFactory factory)
    : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Optional_section_propagates_request_cancellation_without_warning(bool sprintBoard)
    {
        using var scope = factory.Services.CreateScope();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        var logger = new RecordingLogger();
        Exception Fail(CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return expected;
        }

        var model = CreateModel(scope.ServiceProvider, cache, logger, sprintBoard, Fail);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.OnGetAsync(cancellation.Token));

        // Do not swallow the original and continue until another section throws cancellation.
        Assert.Same(expected, actual);
        Assert.Empty(logger.Failures);
        Assert.Empty(model.LatestPhotos);
        if (sprintBoard)
        {
            Assert.Equal(default, model.Now);
            Assert.Empty(model.Latest);
        }
        else
        {
            Assert.NotEmpty(model.Latest);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Optional_section_preserves_fallback_for_failures_without_request_cancellation(
        bool sprintBoard,
        bool unrelatedCancellation)
    {
        using var scope = factory.Services.CreateScope();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var request = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        Exception expected = unrelatedCancellation
            ? new OperationCanceledException(unrelated.Token)
            : new InvalidOperationException("Optional section unavailable.");
        var logger = new RecordingLogger();
        var model = CreateModel(scope.ServiceProvider, cache, logger, sprintBoard, token =>
        {
            Assert.Equal(request.Token, token);
            return expected;
        });

        await model.OnGetAsync(request.Token);

        Assert.False(request.IsCancellationRequested);
        var failure = Assert.Single(logger.Failures);
        Assert.Equal(LogLevel.Warning, failure.Level);
        Assert.Same(expected, failure.Exception);
        Assert.Contains(sprintBoard ? "sprint board" : "replies-today", failure.Message);
        Assert.NotEmpty(model.LatestPhotos);
        Assert.NotEmpty(model.FeaturedArticles);
        if (sprintBoard)
        {
            Assert.Empty(model.SprintBoard.Rows);
            Assert.Equal(0, model.SprintBoard.Players);
        }
        else
        {
            Assert.Null(model.ForumRepliesToday);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Html_exception_pipeline_returns_499_without_reexecution_for_cancelled_homepage(
        bool sprintBoard)
    {
        using var scope = factory.Services.CreateScope();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellation = new CancellationTokenSource();
        var logger = new RecordingLogger();
        var model = CreateModel(scope.ServiceProvider, cache, logger, sprintBoard, token =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(token);
        });
        var context = model.HttpContext;
        context.RequestAborted = cancellation.Token;
        var errorPageCalls = 0;
        var app = new ApplicationBuilder(scope.ServiceProvider);
        app.UseExceptionHandler("/error");
        app.Run(async http =>
        {
            if (http.Request.Path == "/error")
            {
                errorPageCalls++;
                return;
            }

            await model.OnGetAsync(http.RequestAborted);
        });

        await app.Build()(context);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Equal(0, errorPageCalls);
        Assert.Empty(logger.Failures);
    }

    [Fact]
    public async Task Html_exception_pipeline_keeps_500_for_cancellation_unrelated_to_request_abort()
    {
        using var scope = factory.Services.CreateScope();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var expected = new OperationCanceledException(unrelated.Token);
        var context = CreatePageContext(scope.ServiceProvider).HttpContext;
        var errorPageCalls = 0;
        var app = new ApplicationBuilder(scope.ServiceProvider);
        app.UseExceptionHandler("/error");
        app.Run(http =>
        {
            if (http.Request.Path != "/error")
            {
                throw expected;
            }

            errorPageCalls++;
            var feature = http.Features.Get<IExceptionHandlerPathFeature>();
            Assert.NotNull(feature);
            Assert.Same(expected, feature.Error);
            Assert.Equal("/", feature.Path);
            var error = new ErrorModel { PageContext = new PageContext { HttpContext = http } };
            error.OnGet();
            return Task.CompletedTask;
        });

        await app.Build()(context);

        Assert.False(context.RequestAborted.IsCancellationRequested);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(1, errorPageCalls);
    }

    private static IndexModel CreateModel(
        IServiceProvider services,
        IMemoryCache cache,
        RecordingLogger logger,
        bool sprintBoard,
        Func<CancellationToken, Exception> failure)
    {
        var store = new PublicQueryCacheStore(cache, Options.Create(new PublicQueryCacheOptions()));
        var forum = sprintBoard
            ? ActivatorUtilities.CreateInstance<PublicForumQueryCache>(services, store)
            : ActivatorUtilities.CreateInstance<PublicForumQueryCache>(services, store, new FailingLiveActivity(failure));
        var queries = new PublicQueryCacheService(
            ActivatorUtilities.CreateInstance<PublicEditorialQueryCache>(services, store),
            forum,
            ActivatorUtilities.CreateInstance<PublicCatalogQueryCache>(services, store),
            ActivatorUtilities.CreateInstance<PublicMediaQueryCache>(services, store));
        var sprint = sprintBoard
            ? ActivatorUtilities.CreateInstance<QuizSprintService>(services, new ThrowingSprintBoardQuizRepository(failure))
            : services.GetRequiredService<QuizSprintService>();
        var model = ActivatorUtilities.CreateInstance<IndexModel>(services, queries, sprint, logger);
        model.PageContext = CreatePageContext(services);
        return model;
    }

    private static PageContext CreatePageContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/";
        return new PageContext
        {
            HttpContext = context,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
        };
    }

    private sealed class FailingLiveActivity(Func<CancellationToken, Exception> failure) : ILiveActivityQueryService
    {
        public Task<int> GetNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
            throw failure(cancellationToken);
    }

    private sealed class RecordingLogger : ILogger<IndexModel>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Failures { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Failures.Add((logLevel, exception, formatter(state, exception)));
    }
}

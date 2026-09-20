using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class HomePageRoutesTests
{
    private static readonly Regex HomepageHeading = new(
        @"<h1\b[^>]*>\s*Twenty-five years of the Queen internet zone\s*</h1>",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [Fact]
    public async Task Home_returns_200_with_a_single_homepage_heading()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        using var home = await client.GetAsync("/");
        var homeHtml = await home.Content.ReadAsStringAsync();
        using var about = await client.GetAsync("/about");
        using var quizzes = await client.GetAsync("/quizzes");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Single(Regex.Matches(homeHtml, @"<h1\b", RegexOptions.IgnoreCase));
        Assert.Matches(HomepageHeading, homeHtml);

        Assert.Equal(HttpStatusCode.OK, about.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, quizzes.StatusCode);
        Assert.Equal("/quizzes/sprint", quizzes.Headers.Location?.OriginalString);
    }

    [Fact]
    public void Home_and_quizzes_index_use_distinct_page_models()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        _ = factory.CreateAnonymousClient();

        var pages = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<CompiledPageActionDescriptor>())
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => descriptor!)
            .ToList();

        var homeModel = Assert.Single(
            pages
                .Where(page => page.RelativePath == "/Pages/Index.cshtml")
                .Select(page => page.ModelTypeInfo!.AsType())
                .Distinct());
        var quizzesModel = Assert.Single(
            pages
                .Where(page => page.RelativePath == "/Pages/Quizzes/Index.cshtml")
                .Select(page => page.ModelTypeInfo!.AsType())
                .Distinct());

        Assert.Equal(typeof(QueenZone.Web.Pages.IndexModel), homeModel);
        Assert.Equal(typeof(QueenZone.Web.Pages.Quizzes.QuizzesIndexModel), quizzesModel);
        Assert.NotEqual(homeModel, quizzesModel);
    }

    [Fact]
    public async Task Home_still_returns_200_when_the_sprint_board_fails()
    {
        using var factory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<IQuizRepository>();
            services.AddSingleton<IQuizRepository, ThrowingSprintBoardQuizRepository>();
        });
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(html, @"<h1\b", RegexOptions.IgnoreCase));
        Assert.Matches(HomepageHeading, html);
        Assert.Contains("The sixty-second Queen quiz", html, StringComparison.Ordinal);
        Assert.Contains("No scores yet today.", html, StringComparison.Ordinal);
    }

    private sealed class ThrowingSprintBoardQuizRepository : IQuizRepository
    {
        public Task<QuizSprintBoardResult> GetSprintBoardAsync(
            QuizSprintBoardScope scope,
            Guid? viewerMemberId,
            int top = 10,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated QuizSprintRuns lookup failure.");

        public Task<IReadOnlyList<QuizAdminItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Unsupported<IReadOnlyList<QuizAdminItem>>();

        public Task<QuizAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Unsupported<QuizAdminDetail?>();

        public Task<Guid> CreateAsync(
            AdminQuizDraft draft,
            Guid createdByMemberId,
            CancellationToken cancellationToken = default) =>
            Unsupported<Guid>();

        public Task UpdateAsync(Guid id, AdminQuizDraft draft, CancellationToken cancellationToken = default) =>
            Unsupported();

        public Task PublishAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

        public Task UnpublishAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

        public Task RecordAttemptAsync(
            Guid quizId,
            Guid memberAccountId,
            int score,
            int correctCount,
            int questionCount,
            CancellationToken cancellationToken = default) =>
            Unsupported();

        public Task<IReadOnlyList<QuizListItem>> GetPublishedAsync(CancellationToken cancellationToken = default) =>
            Unsupported<IReadOnlyList<QuizListItem>>();

        public Task<QuizPlayView?> GetPublishedForPlayAsync(Guid id, CancellationToken cancellationToken = default) =>
            Unsupported<QuizPlayView?>();

        public Task<IReadOnlyList<QuizSprintQuestion>> GetPublishedSprintQuestionsAsync(
            CancellationToken cancellationToken = default) =>
            Unsupported<IReadOnlyList<QuizSprintQuestion>>();

        public Task<QuizSubmissionResult?> SubmitAsync(
            Guid quizId,
            Guid? memberAccountId,
            IReadOnlyList<QuizAnswerSubmission> answers,
            CancellationToken cancellationToken = default) =>
            Unsupported<QuizSubmissionResult?>();

        public Task<QuizLeaderboardResult> GetLeaderboardAsync(
            QuizLeaderboardScope scope,
            Guid? viewerMemberId,
            int top = 10,
            CancellationToken cancellationToken = default) =>
            Unsupported<QuizLeaderboardResult>();

        public Task RecordSprintRunAsync(
            Guid memberAccountId,
            QuizSprintScore score,
            CancellationToken cancellationToken = default) =>
            Unsupported();

        public Task<bool> ClaimSprintRunAsync(
            Guid runId,
            Guid memberAccountId,
            QuizSprintScore score,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
            Unsupported<bool>();

        private static Task Unsupported() => Task.FromException(new NotSupportedException());

        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
    }
}

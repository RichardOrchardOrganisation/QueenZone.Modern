using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class CheckLinksCommandTests
{
    [Fact]
    public async Task ToolsApp_routes_check_links_command_to_usage_error()
    {
        var exitCode = await WithNoLegacyConnectionStringAsync(() =>
            ToolsApp.RunAsync(["check-links", "--settings-file", MissingSettingsPath()]));

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ToolsApp_default_usage_includes_check_links_command()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync([]);

            Assert.Equal(2, exitCode);
            Assert.Contains("check-links", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunAsync_public_entrypoint_returns_usage_error_when_options_are_invalid()
    {
        var exitCode = await WithNoLegacyConnectionStringAsync(() =>
            CheckLinksCommand.RunAsync(["--settings-file", MissingSettingsPath()]));

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Parse_accepts_scheduled_options()
    {
        var options = CheckLinksOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--concurrency", "4",
            "--confirm-after", "3",
            "--timeout-seconds", "5",
            "--limit", "10",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal(4, options.Concurrency);
        Assert.Equal(3, options.ConfirmDeadAfterFailures);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HttpTimeout);
        Assert.Equal(10, options.Limit);
    }

    [Fact]
    public void Parse_rejects_missing_connection_string()
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var options = CheckLinksOptions.Parse(["--settings-file", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")]);

            Assert.False(options.IsValid);
            Assert.Contains("connection-string", options.ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    [Theory]
    [InlineData("queenonline.com", "https://queenonline.com/")]
    [InlineData("http://example.com/path", "http://example.com/path")]
    public void TryNormalizeHttpUrl_accepts_http_urls(string input, string expected)
    {
        var result = HttpQueenLinkChecker.TryNormalizeHttpUrl(input, out var uri);

        Assert.True(result);
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:test@example.com")]
    public void TryNormalizeHttpUrl_rejects_non_http_urls(string input)
    {
        var result = HttpQueenLinkChecker.TryNormalizeHttpUrl(input, out _);

        Assert.False(result);
    }

    [Fact]
    public async Task RunAsync_marks_link_available_and_keeps_it_visible()
    {
        var repository = RepositoryWithLinks();
        var checker = new StubQueenLinkChecker(_ =>
            new QueenLinkHttpCheckResult("https://www.queenonline.com/", true, false, 200, null));

        var exitCode = await CheckLinksCommand.RunAsync(ValidOptions(), repository, checker);
        var categories = await repository.GetCategoriesWithLinksAsync();
        var validationItems = await repository.GetLinksForValidationAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains(categories.Single().Links, link => link.Title == "Queen Online");
        Assert.Equal(0, validationItems.Single(item => item.Link.Id == 1).ConsecutiveFailureCount);
    }

    [Fact]
    public async Task RunAsync_returns_zero_when_no_links_match()
    {
        var repository = new InMemoryLinksRepository([]);

        var exitCode = await CheckLinksCommand.RunAsync(ValidOptions(), repository);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_returns_zero_for_dry_run()
    {
        var repository = RepositoryWithLinks();
        var options = CheckLinksOptions.Parse(["--connection-string", "Server=.;Database=test;", "--dry-run"]);
        Assert.True(options.IsValid);

        var exitCode = await CheckLinksCommand.RunAsync(options, repository);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_hides_link_after_repeated_hard_failures()
    {
        var repository = RepositoryWithLinks();
        await repository.UpsertCheckResultsAsync(
        [
            new QueenLinkCheckUpdate(2, "https://missing.example.test/", DateTime.UtcNow.AddDays(-1), false, false, 1, 404, null),
        ]);
        var checker = new StubQueenLinkChecker(url => url.Contains("missing", StringComparison.Ordinal)
            ? new QueenLinkHttpCheckResult(url, false, true, 404, null)
            : new QueenLinkHttpCheckResult(url, true, false, 200, null));

        var exitCode = await CheckLinksCommand.RunAsync(ValidOptions(), repository, checker);
        var categories = await repository.GetCategoriesWithLinksAsync();
        var validationItems = await repository.GetLinksForValidationAsync();

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(categories.Single().Links, link => link.Title == "Missing Site");
        var missingItem = validationItems.Single(item => item.Link.Id == 2);
        Assert.Equal(2, missingItem.ConsecutiveFailureCount);
        Assert.True(missingItem.IsConfirmedDead);
    }

    [Fact]
    public async Task RunAsync_does_not_confirm_dead_after_soft_timeout()
    {
        var repository = RepositoryWithLinks();
        var checker = new StubQueenLinkChecker(url =>
            new QueenLinkHttpCheckResult(url, false, false, null, "timeout"));

        await CheckLinksCommand.RunAsync(ValidOptions(), repository, checker);
        var categories = await repository.GetCategoriesWithLinksAsync();
        var validationItems = await repository.GetLinksForValidationAsync();

        Assert.Contains(categories.Single().Links, link => link.Title == "Missing Site");
        Assert.False(validationItems.Single(item => item.Link.Id == 2).IsConfirmedDead);
    }

    private static CheckLinksOptions ValidOptions()
    {
        var options = CheckLinksOptions.Parse(["--connection-string", "Server=.;Database=test;"]);
        Assert.True(options.IsValid);
        return options;
    }

    private static string MissingSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"qz-missing-settings-{Guid.NewGuid():N}.json");

    private static async Task<T> WithNoLegacyConnectionStringAsync<T>(Func<Task<T>> action)
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    private static InMemoryLinksRepository RepositoryWithLinks() =>
        new(
        [
            new QueenLinkCategory(
                1,
                "Official",
                [
                    new QueenLink(1, "Queen Online", "https://www.queenonline.com/", null, 1, true),
                    new QueenLink(2, "Missing Site", "https://missing.example.test/", null, 1, false),
                ]),
        ]);

    private sealed class StubQueenLinkChecker(Func<string, QueenLinkHttpCheckResult> responder) : IQueenLinkChecker
    {
        public Task<QueenLinkHttpCheckResult> CheckAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(responder(url));
    }
}

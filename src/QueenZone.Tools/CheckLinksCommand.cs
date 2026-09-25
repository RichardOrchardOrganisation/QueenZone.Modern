using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Tools;

internal static class CheckLinksCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CheckLinksOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        var repository = new EfLinksRepository(dbContext);
        return await RunAsync(options, repository);
    }

    internal static async Task<int> RunAsync(
        CheckLinksOptions options,
        ILinksRepository repository,
        IQueenLinkChecker? checkerOverride = null)
    {
        var items = await repository.GetLinksForValidationAsync(options.CancellationToken);
        if (options.Limit is int limit)
        {
            items = items.Take(limit).ToList();
        }

        if (items.Count == 0)
        {
            Console.WriteLine("No Queen links matched the requested filters.");
            return 0;
        }

        if (options.DryRun)
        {
            Console.WriteLine($"Links to check: {items.Count}");
            Console.WriteLine($"Concurrency: {options.Concurrency}");
            Console.WriteLine($"Confirm dead after: {options.ConfirmDeadAfterFailures} consecutive hard failures");
            Console.WriteLine("Dry run only. No HTTP requests or database writes were made.");
            return 0;
        }

        var checkedAt = DateTime.UtcNow;
        var checker = checkerOverride ?? new HttpQueenLinkChecker(options.HttpTimeout);
        await using var disposableChecker = checker as IAsyncDisposable;
        var results = await CheckAsync(items, checker, options.Concurrency, options.CancellationToken);
        var updates = results
            .Select(result => ToUpdate(result, checkedAt, options.ConfirmDeadAfterFailures))
            .ToList();

        await repository.UpsertCheckResultsAsync(updates, options.CancellationToken);
        PrintSummary(results, updates);

        return 0;
    }

    private static async Task<IReadOnlyList<QueenLinkCheckRunResult>> CheckAsync(
        IReadOnlyList<QueenLinkValidationItem> items,
        IQueenLinkChecker checker,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(Math.Max(concurrency, 1));
        var tasks = items.Select(item => CheckOneAsync(item, checker, semaphore, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    private static async Task<QueenLinkCheckRunResult> CheckOneAsync(
        QueenLinkValidationItem item,
        IQueenLinkChecker checker,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var result = await checker.CheckAsync(item.Link.Url, cancellationToken);
            return new QueenLinkCheckRunResult(item, result);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static QueenLinkCheckUpdate ToUpdate(
        QueenLinkCheckRunResult result,
        DateTime checkedAt,
        int confirmDeadAfterFailures)
    {
        var failureCount = result.Check.IsAvailable
            ? 0
            : result.Item.ConsecutiveFailureCount + 1;
        var isConfirmedDead = result.Check.IsAvailable
            ? false
            : result.Check.IsHardFailure && failureCount >= confirmDeadAfterFailures;

        return new QueenLinkCheckUpdate(
            result.Item.Link.Id,
            result.Check.NormalizedUrl ?? result.Item.Link.Url,
            checkedAt,
            result.Check.IsAvailable,
            isConfirmedDead,
            failureCount,
            result.Check.StatusCode,
            result.Check.Error);
    }

    private static void PrintSummary(
        IReadOnlyList<QueenLinkCheckRunResult> results,
        IReadOnlyList<QueenLinkCheckUpdate> updates)
    {
        var available = updates.Count(update => update.IsAvailable);
        var confirmedDead = updates.Count(update => update.IsConfirmedDead);
        var failing = updates.Count - available;

        Console.WriteLine("Queen links check");
        Console.WriteLine("=================");
        Console.WriteLine($"Links checked: {updates.Count}");
        Console.WriteLine($"Available: {available}");
        Console.WriteLine($"Failing this run: {failing}");
        Console.WriteLine($"Confirmed dead: {confirmedDead}");

        var deadById = updates
            .Where(update => update.IsConfirmedDead)
            .ToDictionary(update => update.QueenFeaturedSiteId);
        if (deadById.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Confirmed dead links hidden from /links:");
        foreach (var result in results.Where(result => deadById.ContainsKey(result.Item.Link.Id)).Take(50))
        {
            var update = deadById[result.Item.Link.Id];
            Console.WriteLine(
                $"  id={result.Item.Link.Id} title=\"{result.Item.Link.Title}\" failures={update.ConsecutiveFailureCount} status={update.LastStatusCode?.ToString() ?? update.LastError ?? "unknown"} url={update.Url}");
        }

        if (deadById.Count > 50)
        {
            Console.WriteLine($"  ... and {deadById.Count - 50} more");
        }
    }

    private static void WriteUsage(string errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- check-links [options]",
            "",
            "Options:",
            "  --connection-string <connection-string>  SQL Server connection string. Defaults to ConnectionStrings__QueenZoneLegacy.",
            "  --settings-file <path>                  Optional appsettings.Local.json to read ConnectionStrings:QueenZoneLegacyLive.",
            "  --concurrency <number>                  Concurrent HTTP checks. Default: 8.",
            "  --confirm-after <number>                Consecutive hard failures before hiding. Default: 2.",
            "  --timeout-seconds <number>              Per-request timeout. Default: 10.",
            "  --limit <number>                        Check only the first N links.",
            "  --dry-run                               Parse and count links without HTTP requests or writes.");
}

internal sealed record QueenLinkCheckRunResult(
    QueenLinkValidationItem Item,
    QueenLinkHttpCheckResult Check);

internal interface IQueenLinkChecker
{
    Task<QueenLinkHttpCheckResult> CheckAsync(string url, CancellationToken cancellationToken = default);
}

internal sealed record QueenLinkHttpCheckResult(
    string? NormalizedUrl,
    bool IsAvailable,
    bool IsHardFailure,
    int? StatusCode,
    string? Error);

internal sealed class HttpQueenLinkChecker(TimeSpan timeout) : IQueenLinkChecker, IAsyncDisposable
{
    private readonly HttpClient httpClient = new()
    {
        Timeout = timeout,
    };

    public async Task<QueenLinkHttpCheckResult> CheckAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHttpUrl(url, out var uri))
        {
            return new QueenLinkHttpCheckResult(null, false, true, null, "invalid-url");
        }

        var (headStatus, headFailure) = await ProbeAsync(HttpMethod.Head, uri, cancellationToken);
        if (headFailure is not null)
        {
            return headFailure;
        }

        if (IsExistingPageStatus(headStatus))
        {
            return new QueenLinkHttpCheckResult(uri.AbsoluteUri, true, false, headStatus, null);
        }

        if (headStatus is not ((int)System.Net.HttpStatusCode.MethodNotAllowed
            or (int)System.Net.HttpStatusCode.NotImplemented))
        {
            return new QueenLinkHttpCheckResult(uri.AbsoluteUri, false, IsHardFailureStatus(headStatus), headStatus, null);
        }

        var (getStatus, getFailure) = await ProbeAsync(HttpMethod.Get, uri, cancellationToken);
        if (getFailure is not null)
        {
            return getFailure;
        }

        return new QueenLinkHttpCheckResult(
            uri.AbsoluteUri,
            IsExistingPageStatus(getStatus),
            IsHardFailureStatus(getStatus),
            getStatus,
            null);
    }

    private async Task<(int Status, QueenLinkHttpCheckResult? Failure)> ProbeAsync(
        HttpMethod method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return ((int)response.StatusCode, null);
        }
        catch (HttpRequestException ex)
        {
            return (0, new QueenLinkHttpCheckResult(uri.AbsoluteUri, false, true, null, ex.HttpRequestError.ToString()));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (0, new QueenLinkHttpCheckResult(uri.AbsoluteUri, false, false, null, "timeout"));
        }
    }

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static bool TryNormalizeHttpUrl(string candidate, out Uri normalizedUri) =>
        QueenLink.TryNormalizeHttpUrl(candidate, out normalizedUri);

    private static bool IsExistingPageStatus(int statusCode) =>
        statusCode is >= 200 and < 400
            or 401
            or 403;

    private static bool IsHardFailureStatus(int statusCode) =>
        statusCode is 400 or 404 or 410
            || statusCode is >= 500 and < 600;
}

internal sealed class CheckLinksOptions
{
    private CheckLinksOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public int Concurrency { get; private init; } = 8;

    public int ConfirmDeadAfterFailures { get; private init; } = 2;

    public TimeSpan HttpTimeout { get; private init; } = TimeSpan.FromSeconds(10);

    public int? Limit { get; private init; }

    public bool DryRun { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public CancellationToken CancellationToken { get; init; }

    public static CheckLinksOptions Parse(string[] args)
    {
        string? connectionString = null;
        string? settingsFile = null;
        var concurrency = 8;
        var confirmAfter = 2;
        var timeoutSeconds = 10;
        int? limit = null;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
                continue;
            }

            if (string.Equals(arg, "--settings-file", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                settingsFile = args[++index];
                continue;
            }

            if (string.Equals(arg, "--concurrency", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length
                && int.TryParse(args[++index], out var parsedConcurrency))
            {
                concurrency = parsedConcurrency;
                continue;
            }

            if (string.Equals(arg, "--confirm-after", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length
                && int.TryParse(args[++index], out var parsedConfirmAfter))
            {
                confirmAfter = parsedConfirmAfter;
                continue;
            }

            if (string.Equals(arg, "--timeout-seconds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length
                && int.TryParse(args[++index], out var parsedTimeout))
            {
                timeoutSeconds = parsedTimeout;
                continue;
            }

            if (string.Equals(arg, "--limit", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length
                && int.TryParse(args[++index], out var parsedLimit))
            {
                limit = parsedLimit;
                continue;
            }

            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settings = ToolsLocalSettings.TryLoad(settingsFile);
            connectionString = settings?.QueenZoneLegacy ?? settings?.QueenZoneLegacyLive;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string, ConnectionStrings__QueenZoneLegacy, or local settings ConnectionStrings:QueenZoneLegacy is required.");
        }

        if (concurrency <= 0)
        {
            return Invalid("--concurrency must be greater than zero.");
        }

        if (confirmAfter <= 0)
        {
            return Invalid("--confirm-after must be greater than zero.");
        }

        if (timeoutSeconds <= 0)
        {
            return Invalid("--timeout-seconds must be greater than zero.");
        }

        if (limit <= 0)
        {
            return Invalid("--limit must be greater than zero.");
        }

        return new CheckLinksOptions
        {
            ConnectionString = connectionString,
            Concurrency = concurrency,
            ConfirmDeadAfterFailures = confirmAfter,
            HttpTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            Limit = limit,
            DryRun = dryRun,
            IsValid = true,
        };
    }

    private static CheckLinksOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}

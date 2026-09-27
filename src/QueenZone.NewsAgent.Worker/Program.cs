using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;

// Regex reads REGEX_DEFAULT_MATCH_TIMEOUT once in its static constructor.
RegexDefaults.ApplyProcessDefault();
var discoverOptions = DiscoverNewsCommandOptions.Parse(args);
var queuedRunOptions = NewsAgentQueuedRunCommandOptions.Parse(args);
if (discoverOptions is null && queuedRunOptions is null)
{
    PrintUsage();
    return 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
// Full discovery/triage/draft pipeline (not registered on the public web host — #336).
services.AddQueenZoneNewsAgentWorker(configuration);

var connectionString = configuration.GetConnectionString("QueenZoneLegacy");
if (string.IsNullOrWhiteSpace(connectionString))
{
    services.AddQueenZoneInMemoryData();
}
else
{
    services.AddQueenZoneLegacyData(connectionString);
}

await using var provider = services.BuildServiceProvider();
if (queuedRunOptions is not null)
{
    var processor = provider.GetRequiredService<NewsAgentQueuedRunProcessor>();
    return await processor.RunOnceAsync(queuedRunOptions.RunnerId);
}

var worker = provider.GetRequiredService<DiscoverNewsWorker>();
return await worker.RunAsync(discoverOptions!);

static void PrintUsage()
{
    Console.WriteLine("QueenZone news discovery worker");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/QueenZone.NewsAgent.Worker -- discover-news [--fetch-only] [--seed-sources] [--scheduled] [--scheduled-with-drafts] [--dry-run] [--force] [--triage] [--triage-only] [--draft] [--draft-only]");
    Console.WriteLine("  dotnet run --project src/QueenZone.NewsAgent.Worker -- process-news-requests [--runner-id <name>]");
}

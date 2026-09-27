using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Search.Shared;

// Regex reads REGEX_DEFAULT_MATCH_TIMEOUT once in its static constructor.
RegexDefaults.ApplyProcessDefault();
var options = SearchReindexCommandOptions.Parse(args);
if (options is null)
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
// Standalone worker, not registered on the public web app (#527, mirrors #336 for NewsAgent).
services.AddQueenZoneSearchReindexWorker(configuration);

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
var worker = provider.GetRequiredService<SearchReindexScheduledWorker>();
return await worker.RunAsync(options);

static void PrintUsage()
{
    Console.WriteLine("QueenZone search reindex worker");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/QueenZone.SearchReindex.Worker -- reindex [--scheduled] [--force]");
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueenZone.Web;

var options = MaintenanceJobCommandOptions.Parse(args);
if (options is null)
{
    Console.WriteLine("QueenZone maintenance worker");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/QueenZone.Maintenance.Worker -- run-jobs <selector>...");
    Console.WriteLine($"  Selectors: {MaintenanceJobs.SixHourlySelector}, {MaintenanceJobs.DailySelector}, {MaintenanceJobs.AllSelector}, {string.Join(", ", MaintenanceJobs.All)}");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSimpleConsole(console =>
{
    console.TimestampFormat = "yyyy-MM-dd HH:mm:ss'Z' ";
    console.UseUtcTimestamp = true;
}));
// Retention and sweep jobs moved off the public web process (#1677).
services.AddQueenZoneMaintenanceWorker(configuration);

await using var provider = services.BuildServiceProvider();
return await provider.GetRequiredService<MaintenanceJobRunner>().RunAsync(options.Jobs);

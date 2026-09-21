using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QueenZoneDataCompositionTests
{
    private const string MirrorConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public void AddQueenZoneData_E2E_with_mirror_connection_string_uses_real_legacy_data()
    {
        // Resolving EfNewsRepository would eagerly probe the legacy schema over a real SQL
        // connection (EfNewsRepository -> LegacyNewsSchema.HasSlugColumn), which isn't available
        // in this unit test. Assert the DI registration instead of building the provider.
        var services = BuildServices(
            environmentName: QueenZoneEnvironments.E2E,
            connectionString: MirrorConnectionString);

        var registration = Assert.Single(services, sd => sd.ServiceType == typeof(INewsRepository));

        Assert.Equal(typeof(EfNewsRepository), registration.ImplementationType);
    }

    [Fact]
    public void AddQueenZoneData_E2E_with_azure_sql_connection_string_throws_and_does_not_register_services()
    {
        var configuration = BuildConfiguration(
            "Server=tcp:queenzone-db.database.windows.net;Database=queenzone_legacy_sync;User ID=user;Password=pw;");
        var services = new ServiceCollection();
        var environment = new FakeHostEnvironment(QueenZoneEnvironments.E2E);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddQueenZoneData(configuration, environment));

        Assert.Contains("refused server", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddQueenZoneData_E2E_with_empty_connection_string_throws()
    {
        var configuration = BuildConfiguration(connectionString: null);
        var services = new ServiceCollection();
        var environment = new FakeHostEnvironment(QueenZoneEnvironments.E2E);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddQueenZoneData(configuration, environment));

        Assert.Contains("queenzone_legacy_sync", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddQueenZoneData_Testing_ignores_shell_connection_string_and_stays_in_memory()
    {
        // Regression: WebApplicationFactory tests must never inherit a developer or CI shell's
        // production connection string, even when ConnectionStrings__QueenZoneLegacy is set.
        var services = BuildServices(
            environmentName: QueenZoneEnvironments.Testing,
            connectionString: "Server=tcp:queenzone-db.database.windows.net;Database=queenzone;User ID=user;Password=pw;");

        var repository = services.BuildServiceProvider().GetRequiredService<INewsRepository>();

        Assert.IsNotType<EfNewsRepository>(repository);
    }

    [Fact]
    public void AddQueenZoneData_registers_mirror_migrator_only_for_E2E()
    {
        var e2e = BuildServices(
            environmentName: QueenZoneEnvironments.E2E,
            connectionString: MirrorConnectionString);
        var testing = BuildServices(
            environmentName: QueenZoneEnvironments.Testing,
            connectionString: MirrorConnectionString);

        Assert.Contains(
            e2e,
            registration => registration.ImplementationType == typeof(E2EMirrorMigrationHostedService));
        Assert.DoesNotContain(
            testing,
            registration => registration.ImplementationType == typeof(E2EMirrorMigrationHostedService));
    }

    private static IServiceCollection BuildServices(string environmentName, string? connectionString)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(connectionString);
        var environment = new FakeHostEnvironment(environmentName);

        services.AddQueenZoneData(configuration, environment);
        return services;
    }

    private static IConfiguration BuildConfiguration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:QueenZoneLegacy"] = connectionString,
            })
            .Build();

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

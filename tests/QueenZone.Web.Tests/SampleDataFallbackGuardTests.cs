using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace QueenZone.Web.Tests;

public sealed class SampleDataFallbackGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Preview")]
    public void EnsureAllowed_throws_in_production_like_hosts_without_opt_in(string environmentName)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SampleDataFallbackGuard.EnsureAllowed(Environment(environmentName), Configuration(null)));
        Assert.Contains("ConnectionStrings:QueenZoneLegacy is required", ex.Message, StringComparison.Ordinal);
        Assert.Contains(environmentName, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    public void EnsureAllowed_throws_when_opt_in_is_not_true(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SampleDataFallbackGuard.EnsureAllowed(Environment("Production"), Configuration(value)));
    }

    [Fact]
    public void EnsureAllowed_permits_production_like_host_that_opts_in()
    {
        var exception = Record.Exception(() =>
            SampleDataFallbackGuard.EnsureAllowed(Environment("Production"), Configuration("true")));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData(QueenZoneEnvironments.Testing)]
    public void EnsureAllowed_permits_local_hosts_without_opt_in(string environmentName)
    {
        var exception = Record.Exception(() =>
            SampleDataFallbackGuard.EnsureAllowed(Environment(environmentName), Configuration(null)));
        Assert.Null(exception);
    }

    [Fact]
    public void Production_host_without_connection_string_or_opt_in_refuses_to_start()
    {
        using var factory = new ProductionWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting(SampleDataFallbackGuard.AllowWithoutDatabaseKey, "false"));

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            Flatten(ex),
            e => e is InvalidOperationException
                && e.Message.Contains("ConnectionStrings:QueenZoneLegacy is required", StringComparison.Ordinal));
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }

    private static IConfiguration Configuration(string? allowWithoutDatabase) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SampleDataFallbackGuard.AllowWithoutDatabaseKey] = allowWithoutDatabase,
            })
            .Build();

    private static IHostEnvironment Environment(string name) => new FakeHostEnvironment(name);
}

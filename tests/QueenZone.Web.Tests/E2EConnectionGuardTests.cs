namespace QueenZone.Web.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class E2EConnectionGuardTests
{
    private const string MachineName = "AGENT-01";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureSafe_throws_on_empty_connection_string(string? connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            E2EConnectionGuard.EnsureSafe(connectionString, MachineName));
        Assert.Contains("queenzone_legacy_sync", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True")]
    [InlineData(@"Data Source=localhost\SQLEXPRESS;Initial Catalog=queenzone_legacy_sync;Integrated Security=True")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True")]
    [InlineData(@"Server=(local)\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True")]
    [InlineData(@"Server=127.0.0.1\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True")]
    [InlineData(@"Server=AGENT-01\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True")]
    public void EnsureSafe_allows_local_sql_express_mirror(string connectionString)
    {
        var exception = Record.Exception(() => E2EConnectionGuard.EnsureSafe(connectionString, MachineName));
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSafe_throws_on_azure_sql_server()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            E2EConnectionGuard.EnsureSafe(
                "Server=tcp:queenzone-db.database.windows.net;Database=queenzone_legacy_sync;User ID=user;Password=pw;",
                MachineName));
        Assert.Contains("refused server", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSafe_throws_on_remote_sql_express_host()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            E2EConnectionGuard.EnsureSafe(
                @"Server=some-other-box\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True",
                MachineName));
        Assert.Contains("refused server", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSafe_throws_on_production_database_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            E2EConnectionGuard.EnsureSafe(
                @"Server=localhost\SQLEXPRESS;Database=queenzone;Integrated Security=True",
                MachineName));
        Assert.Contains("refused database", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSafe_checks_database_before_server()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            E2EConnectionGuard.EnsureSafe(
                "Server=tcp:queenzone-db.database.windows.net;Database=queenzone;User ID=user;Password=pw;",
                MachineName));
        Assert.Contains("refused database", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSafe_falls_back_to_real_machine_name_when_not_overridden()
    {
        var connectionString = $@"Server={Environment.MachineName}\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True";

        var exception = Record.Exception(() => E2EConnectionGuard.EnsureSafe(connectionString));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Server=glory11;Database=queenzone_legacy_sync;User Id=queenzone_probe;Password=pw;TrustServerCertificate=True")]
    [InlineData(@"Server=glory11\SQLEXPRESS;Database=queenzone_legacy_sync;User Id=queenzone_probe;Password=pw;TrustServerCertificate=True")]
    public void EnsureSafe_allows_glory11_lan_login(string connectionString)
    {
        var exception = Record.Exception(() => E2EConnectionGuard.EnsureSafe(connectionString, MachineName));
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSafe_allows_sqlexpress_lan_address_env_var_with_port()
    {
        var previous = Environment.GetEnvironmentVariable("SQLEXPRESS_LAN_ADDRESS");
        Environment.SetEnvironmentVariable("SQLEXPRESS_LAN_ADDRESS", "192.168.1.237,1433");
        try
        {
            var exception = Record.Exception(() => E2EConnectionGuard.EnsureSafe(
                "Server=192.168.1.237,1433;Database=queenzone_legacy_sync;User Id=queenzone_probe;Password=pw;TrustServerCertificate=True",
                MachineName));

            Assert.Null(exception);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQLEXPRESS_LAN_ADDRESS", previous);
        }
    }
}

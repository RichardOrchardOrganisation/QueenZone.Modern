using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class MemberBrowserFixtureTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("E2E", true)]
    [InlineData("Testing", false)]
    public async Task Fixture_is_disabled_without_both_testing_environment_and_explicit_opt_in(string environment, bool enabled)
    {
        var members = System.Reflection.DispatchProxy.Create<IMemberAccountRepository, RejectMembers>();
        using var guarded = new ServiceCollection().AddSingleton(members).BuildServiceProvider();
        await MemberBrowserFixture.SeedAsync(new Host(environment), enabled, guarded);
        Assert.Equal(0, ((RejectMembers)members).Calls);
    }

    [Fact]
    public async Task Fixture_seeds_every_browser_member_into_memory_and_is_idempotent()
    {
        var members = new InMemoryMemberAccountRepository();
        using var services = new ServiceCollection().AddSingleton<IMemberAccountRepository>(members).BuildServiceProvider();

        await MemberBrowserFixture.SeedAsync(new Host("Testing"), true, services);
        await MemberBrowserFixture.SeedAsync(new Host("Testing"), true, services);

        foreach (var member in BrowserFixtureMembers.All)
        {
            var account = await members.FindByIdAsync(member.Id);
            Assert.NotNull(account);
            Assert.Equal(member.DisplayName, account.DisplayName);
            Assert.Equal(MemberMessagePrivacy.Members, account.MessagePrivacy);
            Assert.Equal(member.Id, (await members.FindByEmailAsync(member.Email))?.Id);
        }
    }

    [Fact]
    public async Task Database_or_unknown_member_store_is_rejected_before_any_fixture_write()
    {
        var members = System.Reflection.DispatchProxy.Create<IMemberAccountRepository, RejectMembers>();
        using var services = new ServiceCollection().AddSingleton(members).BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => MemberBrowserFixture.SeedAsync(new Host("Testing"), true, services));
        Assert.Equal(0, ((RejectMembers)members).Calls);
    }

    [Fact]
    public void Production_host_config_does_not_enable_member_browser_fixture()
    {
        var production = new ConfigurationBuilder()
            .AddJsonFile(RepoPaths.Combine("src", "QueenZone.Web", "appsettings.json"))
            .AddInMemoryCollection(ProductionHostSettings.Values)
            .Build();
        Assert.False(production.GetValue<bool>("MemberBrowserFixture:Enabled"));
    }

    private sealed class Host(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Member fixture tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public class RejectMembers : System.Reflection.DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("A non-memory member store must never be called by fixtures.");
        }
    }
}

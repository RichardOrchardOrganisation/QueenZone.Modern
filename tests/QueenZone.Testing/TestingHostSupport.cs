using Microsoft.AspNetCore.Authentication;

namespace QueenZone.Web;

public sealed class TestingHostSupport : ILocalHostSupport
{
    public AuthenticationBuilder AddAuthentication(AuthenticationBuilder builder, bool includeMembers)
    {
        builder.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(LocalHostSupport.AdminScheme, null);
        if (includeMembers)
            builder.AddScheme<AuthenticationSchemeOptions, TestMemberAuthHandler>(LocalHostSupport.MemberScheme, null);
        return builder;
    }

    public void AddContractHost(IServiceCollection services, IHostEnvironment environment)
    {
        if (MobileApiContractHost.IsEnabled(environment))
            services.AddHostedService<MobileApiContractHostedService>();
    }

    public async Task SeedAsync(IHostEnvironment environment, IConfiguration configuration, IServiceProvider services)
    {
        await CrosswordBrowserFixture.SeedAsync(environment,
            configuration.GetValue<bool>("CrosswordBrowserFixture:Enabled"), services);
        await MemberBrowserFixture.SeedAsync(environment,
            configuration.GetValue<bool>("MemberBrowserFixture:Enabled"), services);
    }
}

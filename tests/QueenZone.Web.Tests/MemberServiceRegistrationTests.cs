using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Builds the real web host with <c>ValidateOnBuild</c> and <c>ValidateScopes</c> so a missing
/// or mis-scoped registration for the split member services (#2437) fails here, not at runtime.
/// </summary>
public sealed class MemberServiceRegistrationTests : IClassFixture<MemberServiceRegistrationTests.ValidatingFactory>
{
    private readonly ValidatingFactory factory;

    public MemberServiceRegistrationTests(ValidatingFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(typeof(MemberAccountService))]
    [InlineData(typeof(MemberAccountDeletionService))]
    [InlineData(typeof(MemberAvatarService))]
    [InlineData(typeof(MemberProfileService))]
    [InlineData(typeof(LegacyAccountLinkService))]
    public void MemberServices_ResolveFromValidatedProvider(Type serviceType)
    {
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    public sealed class ValidatingFactory : QueenZoneWebApplicationFactory
    {
        protected override void ConfigureTestServices(IWebHostBuilder builder) =>
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
    }
}

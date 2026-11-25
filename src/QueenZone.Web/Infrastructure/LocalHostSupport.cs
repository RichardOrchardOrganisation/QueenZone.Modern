using Microsoft.AspNetCore.Authentication;

namespace QueenZone.Web;

/// <summary>Explicit composition boundary for the separately distributed local/test host.</summary>
public interface ILocalHostSupport
{
    AuthenticationBuilder AddAuthentication(AuthenticationBuilder builder, bool includeMembers);
    void AddContractHost(IServiceCollection services, IHostEnvironment environment);
    Task SeedAsync(IHostEnvironment environment, IConfiguration configuration, IServiceProvider services);
}

public static class LocalHostSupport
{
    public const string AdminScheme = "Test";
    public const string MemberScheme = "TestMember";
    private static ILocalHostSupport? implementation;

    // Set by the dedicated local host or test assembly, never by configuration or an environment name.
    public static void Register(ILocalHostSupport support)
    {
        ArgumentNullException.ThrowIfNull(support);
        if (Interlocked.CompareExchange(ref implementation, support, null) is not null)
            throw new InvalidOperationException("Local host support is already registered.");
    }

    public static ILocalHostSupport Require() => implementation
        ?? throw new InvalidOperationException(
            "Test authentication is not included in QueenZone.Web. Run tests/QueenZone.TestHost instead, " +
            "or configure Microsoft Entra authentication for this host.");
}

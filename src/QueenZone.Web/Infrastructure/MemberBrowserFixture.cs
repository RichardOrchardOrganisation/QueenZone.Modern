using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Opt-in deterministic browser members (<see cref="BrowserFixtureMembers"/>). Runs only in the
/// Testing environment against the in-memory member store; never writes to a database.
/// </summary>
internal static class MemberBrowserFixture
{
    public static async Task SeedAsync(IHostEnvironment environment, bool enabled, IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        if (!enabled || !environment.IsEnvironment(QueenZoneEnvironments.Testing)) return;
        using var scope = services.CreateScope();
        var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        if (members is not InMemoryMemberAccountRepository)
            throw new InvalidOperationException("Member browser fixtures require an in-memory Testing member store.");
        foreach (var member in BrowserFixtureMembers.All)
        {
            if (await members.FindByIdAsync(member.Id, cancellationToken) is null)
                await members.CreateAsync(member.ToAccount(), cancellationToken);
        }
    }
}

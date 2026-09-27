using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

internal static class MemberBearerAccounts
{
    public static void Ensure(IServiceProvider services, Guid memberId, string email, string displayName)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        if (repository.FindByIdAsync(memberId).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        repository.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
    }
}

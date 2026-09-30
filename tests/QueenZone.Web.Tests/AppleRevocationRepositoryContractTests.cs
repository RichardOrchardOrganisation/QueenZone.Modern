using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class AppleRevocationRepositoryContractTests
{
    protected static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    protected abstract IAppleRevocationRepository Repository { get; }
    protected abstract Task AddMemberAsync(MemberAccount account);
    protected abstract Task AddLoginAsync(MemberExternalLogin login);
    protected abstract Task<MemberExternalLogin?> FindLoginAsync(Guid id);

    [Fact]
    public async Task SaveProtectedToken_RequiresMatchingMemberProviderAndKey()
    {
        var member = await SeedMemberAsync(purged: true);
        var otherMember = await SeedMemberAsync(purged: true);
        var target = await SeedLoginAsync(member, "Apple", "target", "old");
        var otherKey = await SeedLoginAsync(member, "Apple", "other-key", "other");
        var google = await SeedLoginAsync(member, "Google", "target", "google");

        await Repository.SaveAppleRefreshTokenAsync(otherMember.Id, "target", "wrong-member");
        await Repository.SaveAppleRefreshTokenAsync(member.Id, "missing", "wrong-key");
        Assert.Equal("old", (await FindLoginAsync(target.Id))!.AppleRefreshTokenProtected);

        await Repository.SaveAppleRefreshTokenAsync(member.Id, "target", "protected-new");

        Assert.Equal("protected-new", (await FindLoginAsync(target.Id))!.AppleRefreshTokenProtected);
        Assert.Equal("other", (await FindLoginAsync(otherKey.Id))!.AppleRefreshTokenProtected);
        Assert.Equal("google", (await FindLoginAsync(google.Id))!.AppleRefreshTokenProtected);
        Assert.Equal(2, (await Repository.ListPendingAppleRevocationsAsync(10)).Count);
    }

    [Fact]
    public async Task PendingRevocations_RequirePurgeAndToken_AndUseOldestFirstLimit()
    {
        var purged = await SeedMemberAsync(purged: true);
        var coolingOff = await SeedMemberAsync(purged: false);
        var newer = await SeedLoginAsync(purged, "Apple", "newer", "newer-token", Now);
        var older = await SeedLoginAsync(purged, "Apple", "older", "older-token", Now.AddDays(-1));
        await SeedLoginAsync(coolingOff, "Apple", "cooling-off", "retained-token");
        await SeedLoginAsync(purged, "Apple", "no-token", null);
        await SeedLoginAsync(purged, "Google", "google", "not-an-apple-token");

        Assert.Equal(new PendingAppleRevocation(older.Id, "older-token"),
            Assert.Single(await Repository.ListPendingAppleRevocationsAsync(1)));
        Assert.Equal([older.Id, newer.Id],
            (await Repository.ListPendingAppleRevocationsAsync(10)).Select(item => item.ExternalLoginId));
        Assert.Empty(await Repository.ListPendingAppleRevocationsAsync(0));
    }

    [Fact]
    public async Task PendingRevocations_RemainAvailableUntilExplicitCompletion()
    {
        var member = await SeedMemberAsync(purged: true);
        var login = await SeedLoginAsync(member, "Apple", "retry", "retry-token");

        var firstAttempt = Assert.Single(await Repository.ListPendingAppleRevocationsAsync(10));
        Assert.Equal(firstAttempt, Assert.Single(await Repository.ListPendingAppleRevocationsAsync(10)));

        await Repository.CompleteAppleRevocationAsync(login.Id);
        await Repository.CompleteAppleRevocationAsync(login.Id);

        Assert.Empty(await Repository.ListPendingAppleRevocationsAsync(10));
        Assert.Null(await FindLoginAsync(login.Id));
    }

    [Fact]
    public async Task Completion_CannotRemoveUnpurgedOrOtherProviderLogins()
    {
        var purged = await SeedMemberAsync(purged: true);
        var coolingOff = await SeedMemberAsync(purged: false);
        var retained = await SeedLoginAsync(coolingOff, "Apple", "retained", "retained-token");
        var google = await SeedLoginAsync(purged, "Google", "google", null);
        var pending = await SeedLoginAsync(purged, "Apple", "pending", "pending-token");

        await Repository.CompleteAppleRevocationAsync(retained.Id);
        await Repository.CompleteAppleRevocationAsync(google.Id);
        await Repository.CompleteAppleRevocationAsync(Guid.NewGuid());

        Assert.NotNull(await FindLoginAsync(retained.Id));
        Assert.NotNull(await FindLoginAsync(google.Id));
        Assert.Equal(pending.Id, Assert.Single(await Repository.ListPendingAppleRevocationsAsync(10)).ExternalLoginId);
    }

    protected async Task<MemberAccount> SeedMemberAsync(bool purged)
    {
        var id = Guid.NewGuid();
        var account = new MemberAccount
        {
            Id = id,
            Email = $"{id:N}@example.test",
            NormalizedEmail = $"{id:N}@example.test".ToUpperInvariant(),
            DisplayName = "Apple Member",
            CreatedAt = Now.AddDays(-60),
            DeletionRequestedAt = Now.AddDays(-30),
            PersonalDataPurgedAt = purged ? Now : null,
        };
        await AddMemberAsync(account);
        return account;
    }

    protected async Task<MemberExternalLogin> SeedLoginAsync(
        MemberAccount account,
        string provider,
        string providerKey,
        string? protectedToken,
        DateTime? linkedAt = null)
    {
        var login = new MemberExternalLogin
        {
            Id = Guid.NewGuid(),
            MemberAccountId = account.Id,
            Provider = provider,
            ProviderKey = providerKey,
            Email = account.Email,
            LinkedAt = linkedAt ?? Now,
            AppleRefreshTokenProtected = protectedToken,
        };
        await AddLoginAsync(login);
        return login;
    }
}

public sealed class InMemoryAppleRevocationRepositoryContractTests : AppleRevocationRepositoryContractTests
{
    private readonly List<MemberAccount> accounts = [];
    private readonly List<MemberExternalLogin> logins = [];
    private readonly IAppleRevocationRepository repository;

    public InMemoryAppleRevocationRepositoryContractTests()
    {
        repository = new InMemoryAppleRevocationRepository(accounts, logins, new Lock());
    }

    protected override IAppleRevocationRepository Repository => repository;

    protected override Task AddMemberAsync(MemberAccount account)
    {
        accounts.Add(account);
        return Task.CompletedTask;
    }

    protected override Task AddLoginAsync(MemberExternalLogin login)
    {
        logins.Add(login);
        return Task.CompletedTask;
    }

    protected override Task<MemberExternalLogin?> FindLoginAsync(Guid id) =>
        Task.FromResult(logins.SingleOrDefault(login => login.Id == id));
}

public sealed class EfAppleRevocationRepositoryContractTests : AppleRevocationRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly IAppleRevocationRepository repository;

    public EfAppleRevocationRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
        repository = new EfAppleRevocationRepository(dbContext);
    }

    protected override IAppleRevocationRepository Repository => repository;

    protected override async Task AddMemberAsync(MemberAccount account)
    {
        dbContext.MemberAccounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    protected override async Task AddLoginAsync(MemberExternalLogin login)
    {
        dbContext.MemberExternalLogins.Add(login);
        await dbContext.SaveChangesAsync();
    }

    protected override Task<MemberExternalLogin?> FindLoginAsync(Guid id) =>
        dbContext.MemberExternalLogins.AsNoTracking().SingleOrDefaultAsync(login => login.Id == id);

    [Fact]
    public async Task Persistence_UsesCallerTransaction_WithoutCommittingIt()
    {
        var member = await SeedMemberAsync(purged: true);
        var login = await SeedLoginAsync(member, "Apple", "rollback", "original-token");
        var accounts = new EfMemberAccountRepository(dbContext);
        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            await accounts.SaveAppleRefreshTokenAsync(member.Id, login.ProviderKey, "updated-token");
            Assert.Equal("updated-token", Assert.Single(await Repository.ListPendingAppleRevocationsAsync(10)).ProtectedToken);
            await accounts.CompleteAppleRevocationAsync(login.Id);
            Assert.Empty(await Repository.ListPendingAppleRevocationsAsync(10));
            await transaction.RollbackAsync();
        }

        Assert.Equal("original-token", Assert.Single(await accounts.ListPendingAppleRevocationsAsync(10)).ProtectedToken);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}

public sealed class AppleRevocationRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowCapability_SharesAccountRepositoryAndItsLifetime(bool inMemory)
    {
        var services = new ServiceCollection();
        if (inMemory)
        {
            services.AddQueenZoneInMemoryData();
        }
        else
        {
            services.AddQueenZoneLegacyData("Server=(local);Database=QueenZone;Trusted_Connection=True;");
        }

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstAccounts = first.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var firstRevocations = first.ServiceProvider.GetRequiredService<IAppleRevocationRepository>();
        var secondRevocations = second.ServiceProvider.GetRequiredService<IAppleRevocationRepository>();
        Assert.Same(firstAccounts, firstRevocations);
        if (inMemory)
        {
            Assert.Same(firstRevocations, secondRevocations);
        }
        else
        {
            Assert.NotSame(firstRevocations, secondRevocations);
        }
    }
}

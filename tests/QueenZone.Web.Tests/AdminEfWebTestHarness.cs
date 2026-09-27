using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Class fixture for SQLite-backed EF admin HTTP tests. One connection and one host
/// per class; <see cref="ResetAsync"/> deletes rows between serial tests.
/// </summary>
public class AdminEfWebApplicationFactory : QueenZoneWebApplicationFactory
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AdminEfDiscoveryPromoteGate promoteGate = new();

    public AdminEfWebApplicationFactory() => connection.Open();

    internal AdminEfDiscoveryPromoteGate PromoteGate => promoteGate;

    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:QueenZoneLegacy", string.Empty);
        builder.ConfigureServices(services =>
        {
            ConfigureAdminEfCore(services);
            ConfigureAdminEfRepositories(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QueenZoneDbContext>();
        dbContext.Database.EnsureCreated();
        AdminNewsSqliteTestHarness.EnsureNewsTable(dbContext);
        return host;
    }

    private void ConfigureAdminEfCore(IServiceCollection services)
    {
        services.RemoveAll<QueenZoneDbContext>();
        services.RemoveAll<DbContextOptions<QueenZoneDbContext>>();
        services.RemoveAll<IDbContextFactory<QueenZoneDbContext>>();
        services.RemoveAll<IAdminNewsRepository>();
        services.RemoveAll<INewsAuditRepository>();
        services.RemoveAll<INewsDiscoveryRepository>();
        services.RemoveAll<INewsSuggestionRepository>();
        services.RemoveAll<INewsAgentRunLeaseService>();
        services.RemoveAll<IMemberAccountRepository>();
        services.RemoveAll<SharedNewsDiscoveryStore>();
        services.AddSingleton<IMemberAccountRepository, InMemoryMemberAccountRepository>();

        services.RemoveAll<IArticleSubmissionRepository>();
        services.RemoveAll<IArticleRepository>();
        services.AddSingleton<IArticleRepository, EmptyArticleRepository>();

        services.AddSingleton(promoteGate);
        services.AddDbContextFactory<QueenZoneDbContext>(options => options.UseSqlite(connection));
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<QueenZoneDbContext>>().CreateDbContext());
        services.AddScoped<IAdminNewsRepository>(sp =>
        {
            var dbContext = sp.GetRequiredService<QueenZoneDbContext>();
            return new EfAdminNewsRepository(dbContext, AdminNewsSqliteTestHarness.LatestNewsSql);
        });
        services.AddScoped<INewsAuditRepository, EfNewsAuditRepository>();
        services.AddScoped<INewsDiscoveryRepository>(sp =>
        {
            var inner = new EfNewsDiscoveryRepository(sp.GetRequiredService<QueenZoneDbContext>());
            var gate = sp.GetRequiredService<AdminEfDiscoveryPromoteGate>();
            return new ConfigurableNewsDiscoveryRepository(inner)
            {
                TryUpdateCandidateStatusHandler = gate.CreatePromoteHandler(inner),
            };
        });
        services.AddScoped<INewsSuggestionRepository, EfNewsSuggestionRepository>();
    }

    protected virtual void ConfigureAdminEfRepositories(IServiceCollection services)
    {
    }

    public override async Task ResetAsync()
    {
        promoteGate.Reset();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QueenZoneDbContext>();
        await dbContext.NewsAgentDrafts.ExecuteDeleteAsync();
        await dbContext.NewsCandidateEvidence.ExecuteDeleteAsync();
        await dbContext.NewsAiRuns.ExecuteDeleteAsync();
        await dbContext.NewsCandidates.ExecuteDeleteAsync();
        await dbContext.NewsDiscoverySources.ExecuteDeleteAsync();
        await dbContext.NewsAuditLogs.ExecuteDeleteAsync();
        await dbContext.NewsSuggestions.ExecuteDeleteAsync();
        await dbContext.PhotoSubmissionAuditLogs.ExecuteDeleteAsync();
        await dbContext.PhotoSubmissions.ExecuteDeleteAsync();
        await dbContext.ArticleSubmissions.ExecuteDeleteAsync();
        await dbContext.MemberExternalLogins.ExecuteDeleteAsync();
        await dbContext.MemberAccounts.ExecuteDeleteAsync();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NEWS_T");
        await base.ResetAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            connection.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>EF dashboard fixture that also wires submission-queue repositories.</summary>
public sealed class AdminDashboardEfWebApplicationFactory : AdminEfWebApplicationFactory
{
    protected override void ConfigureAdminEfRepositories(IServiceCollection services)
    {
        services.RemoveAll<IMemberAccountRepository>();
        services.RemoveAll<IPhotoSubmissionRepository>();
        services.RemoveAll<INewsSuggestionRepository>();
        services.RemoveAll<IArticleSubmissionRepository>();
        services.RemoveAll<IArticleRepository>();
        services.AddScoped<IMemberAccountRepository, EfMemberAccountRepository>();
        services.AddScoped<IPhotoSubmissionRepository, EfPhotoSubmissionRepository>();
        services.AddScoped<INewsSuggestionRepository, EfNewsSuggestionRepository>();
        services.AddScoped<IArticleSubmissionRepository, EfArticleSubmissionRepository>();
        services.AddScoped<IArticleRepository, EfArticleRepository>();
    }
}

internal sealed class AdminEfDiscoveryPromoteGate
{
    public bool FailPromoteStatusUpdate { get; set; }

    public void Reset() => FailPromoteStatusUpdate = false;

    public Func<int, NewsCandidateStatusUpdate, CancellationToken, Task<bool>>? CreatePromoteHandler(
        INewsDiscoveryRepository inner)
    {
        if (!FailPromoteStatusUpdate)
        {
            return null;
        }

        return (id, update, cancellationToken) =>
            update.Status == NewsCandidateStatus.PromotedToArticle
                ? Task.FromResult(false)
                : inner.TryUpdateCandidateStatusAsync(id, update, cancellationToken);
    }
}

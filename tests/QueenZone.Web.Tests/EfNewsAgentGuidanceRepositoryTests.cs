using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class EfNewsAgentGuidanceRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfNewsAgentGuidanceRepository repository;

    public EfNewsAgentGuidanceRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        repository = new EfNewsAgentGuidanceRepository(dbContext);
    }

    [Fact]
    public async Task Long_guidance_round_trips_without_a_model_length_limit()
    {
        var content = new string('a', 40_001) + "Preserve band member quotes.";
        var draft = await repository.SaveDraftAsync(NewsAgentGuidanceType.Draft, content, "admin@test.local", null);
        await repository.PublishDraftAsync(NewsAgentGuidanceType.Draft, "admin@test.local", draft.RowVersion);

        dbContext.ChangeTracker.Clear();
        Assert.Equal(content, (await repository.GetPublishedAsync(NewsAgentGuidanceType.Draft))!.Content);
        var guidance = dbContext.Model.FindEntityType(typeof(QueenZone.Data.Entities.NewsAgentGuidanceRevisionEntity))!;
        var evidence = dbContext.Model.FindEntityType(typeof(QueenZone.Data.Entities.NewsCandidateEvidenceEntity))!;
        Assert.Null(guidance.FindProperty("Content")!.GetMaxLength());
        Assert.Null(evidence.FindProperty("Excerpt")!.GetMaxLength());
    }

    [Fact]
    public async Task Create_publish_history_and_single_published_revision()
    {
        var draft = await repository.SaveDraftAsync(
            NewsAgentGuidanceType.Triage,
            "prefer member-news",
            "admin@test.local",
            null);
        var published = await repository.PublishDraftAsync(
            NewsAgentGuidanceType.Triage,
            "admin@test.local",
            draft.RowVersion);

        var next = await repository.SaveDraftAsync(
            NewsAgentGuidanceType.Triage,
            "prefer archival stories",
            "editor@test.local",
            null);
        await repository.PublishDraftAsync(
            NewsAgentGuidanceType.Triage,
            "editor@test.local",
            next.RowVersion);

        var current = await repository.GetPublishedAsync(NewsAgentGuidanceType.Triage);
        var history = await repository.ListHistoryAsync(NewsAgentGuidanceType.Triage);
        Assert.NotNull(current);
        Assert.Equal("prefer archival stories", current.Content);
        Assert.Equal(2, history.Count);
        Assert.Single(history, item => item.Status == NewsAgentGuidanceStatus.Published);
        Assert.Equal(NewsAgentGuidanceStatus.Superseded, history.Single(item => item.Id == published.Id).Status);
        Assert.Null(await repository.GetDraftAsync(NewsAgentGuidanceType.Triage));
    }

    [Fact]
    public async Task Rollback_and_restore_default_create_new_published_rows()
    {
        var draft = await repository.SaveDraftAsync(NewsAgentGuidanceType.Draft, "first overlay", "a@test.local", null);
        var first = await repository.PublishDraftAsync(NewsAgentGuidanceType.Draft, "a@test.local", draft.RowVersion);
        var next = await repository.SaveDraftAsync(NewsAgentGuidanceType.Draft, "second overlay", "b@test.local", null);
        await repository.PublishDraftAsync(NewsAgentGuidanceType.Draft, "b@test.local", next.RowVersion);

        var rolledBack = await repository.RollbackAsync(NewsAgentGuidanceType.Draft, first.Id, "c@test.local");
        Assert.Equal("first overlay", rolledBack.Content);
        Assert.NotEqual(first.Id, rolledBack.Id);

        var restored = await repository.RestoreCompiledDefaultAsync(NewsAgentGuidanceType.Draft, "d@test.local");
        Assert.Equal(string.Empty, restored.Content);
        Assert.Equal(4, (await repository.ListHistoryAsync(NewsAgentGuidanceType.Draft)).Count);
    }

    [Fact]
    public async Task SaveDraft_rejects_stale_row_version()
    {
        var draft = await repository.SaveDraftAsync(NewsAgentGuidanceType.Triage, "first", "a@test.local", null);
        await repository.SaveDraftAsync(NewsAgentGuidanceType.Triage, "second", "a@test.local", draft.RowVersion);

        await Assert.ThrowsAsync<NewsAgentGuidanceConcurrencyException>(() =>
            repository.SaveDraftAsync(NewsAgentGuidanceType.Triage, "third", "a@test.local", draft.RowVersion));
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}

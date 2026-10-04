using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordCatalogRepositoryTests
{
    private static readonly Guid Creator = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Bulk_publication_validates_every_token_and_grid_before_changing_any_puzzle(bool ef) => Run(ef, async (repository, clock) =>
    {
        var seeds = CrosswordSampleData.Load();
        await repository.ImportAsync(seeds.Take(2).ToArray(), Creator, "fixture");
        var items = await repository.GetAllAsync();
        var invalid = items[1].Seed with { Grid = items[1].Seed.Grid with { Clues = [] } };
        await repository.SaveDraftAsync(items[1].Id, invalid, items[1].RowVersion, "editor");
        var invalidItem = (await repository.GetByIdAsync(items[1].Id))!;
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PublishSelectedAsync([new(items[0].Id, items[0].RowVersion), new(invalidItem.Id, invalidItem.RowVersion)], "publisher"));
        Assert.All(await repository.GetAllAsync(), item => Assert.Equal(CrosswordStatus.Draft, item.Status));
        Assert.DoesNotContain(await repository.GetAuditAsync(items[0].Id), row => row.Action == "Published");
        await repository.SaveDraftAsync(invalidItem.Id, items[1].Seed, invalidItem.RowVersion, "editor");
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.PublishSelectedAsync([new(items[0].Id, items[0].RowVersion), new(invalidItem.Id, invalidItem.RowVersion)], "publisher"));
        Assert.All(await repository.GetAllAsync(), item => Assert.Equal(CrosswordStatus.Draft, item.Status));
        var current = await repository.GetAllAsync();
        await repository.PublishSelectedAsync(current.Select(row => new CrosswordPublishSelection(row.Id, row.RowVersion)).ToArray(), "publisher");
        Assert.All(await repository.GetAllAsync(), item => Assert.Equal(CrosswordStatus.Published, item.Status));
        foreach (var item in current) Assert.Single(await repository.GetAuditAsync(item.Id), row => row.Action == "Published");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Imports_ten_as_drafts_with_lossless_grids_and_one_audit_each(bool ef) => Run(ef, async (repository, clock) =>
    {
        var seeds = CrosswordSampleData.Load();
        Assert.Equal(10, seeds.Count);
        var result = await repository.ImportAsync(seeds, Creator, "test-import");
        Assert.Equal(10, result.Imported.Count);
        Assert.Empty(result.Skipped);
        var items = await repository.GetAllAsync();
        Assert.Equal(10, items.Count);
        foreach (var item in items)
        {
            Assert.Equal(CrosswordStatus.Draft, item.Status);
            Assert.Null(item.PublishedAt);
            Assert.Null(item.PublishAt);
            Assert.Equal(Creator, item.CreatedByMemberId);
            Assert.Equal(clock.GetUtcNow(), item.CreatedAt);
            Assert.Equal(clock.GetUtcNow(), item.UpdatedAt);
            Assert.Equal("test-import", item.UpdatedByEmail);
            Assert.NotEmpty(item.RowVersion);
            Assert.Equal(CrosswordSeedJson.Export(seeds.Single(seed => seed.Slug == item.Seed.Slug)), CrosswordSeedJson.Export(item.Seed));
            var audit = Assert.Single(await repository.GetAuditAsync(item.Id));
            Assert.NotEqual(Guid.Empty, audit.Id);
            Assert.Equal("Imported", audit.Action);
            Assert.Equal("test-import", audit.Actor);
            Assert.Equal(clock.GetUtcNow(), audit.CreatedAt);
            Assert.Contains("Draft", audit.Summary, StringComparison.Ordinal);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Repeat_import_skips_all_slugs_and_preserves_admin_edits(bool ef) => Run(ef, async (repository, clock) =>
    {
        var seeds = CrosswordSampleData.Load();
        await repository.ImportAsync(seeds, Creator, "test-import");
        var first = (await repository.GetAllAsync())[0];
        clock.Advance(TimeSpan.FromMinutes(1));
        await repository.SaveDraftAsync(first.Id, first.Seed with { Title = "Admin edit" }, first.RowVersion, "editor@test.local");
        var result = await repository.ImportAsync(seeds, Creator, "test-import");
        Assert.Empty(result.Imported);
        Assert.Equal(10, result.Skipped.Count);
        Assert.Equal("Admin edit", (await repository.GetByIdAsync(first.Id))!.Seed.Title);
        var audit = await repository.GetAuditAsync(first.Id);
        Assert.Equal(2, audit.Count);
        Assert.Equal("Edited", audit[0].Action);
        Assert.Equal("editor@test.local", audit[0].Actor);
        Assert.Equal("Imported", audit[1].Action);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Invalid_or_duplicate_batch_writes_nothing(bool ef) => Run(ef, async (repository, _) =>
    {
        var seeds = CrosswordSampleData.Load();
        var seed = seeds[1];
        var corrupt = seed with { Grid = seed.Grid with { Clues = [] } };
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ImportAsync([seeds[0], corrupt], Creator, "test-import"));
        Assert.Empty(await repository.GetAllAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ImportAsync([seed, seed], Creator, "test-import"));
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await repository.GetAuditAsync(Guid.NewGuid()));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Unfinished_draft_rows_and_blank_clues_survive_save_and_reload(bool ef) => Run(ef, async (repository, _) =>
    {
        var seed = CrosswordSampleData.Load()[0] with
        {
            Grid = new(5, 5, ["?????", "?????", "?????", "?????", "?????"], [])
        };
        var id = await repository.CreateDraftAsync(seed, Creator, "editor@test.local");
        var loaded = (await repository.GetByIdAsync(id))!;
        Assert.Equal(seed.Grid.Rows, loaded.Seed.Grid.Rows);
        Assert.Empty(loaded.Seed.Grid.Clues);
        Assert.False(CrosswordGridValidator.Validate(loaded.Seed.Grid).IsValid);
        var valid = CrosswordSampleData.Load()[0];
        var clue = valid.Grid.Clues[0] with { Clue = "", Enumeration = "" };
        var unfinishedClue = valid with { Grid = valid.Grid with { Clues = [clue, .. valid.Grid.Clues.Skip(1)] } };
        await repository.SaveDraftAsync(id, unfinishedClue, loaded.RowVersion, "editor@test.local");
        loaded = (await repository.GetByIdAsync(id))!;
        Assert.Equal(valid.Grid.Rows, loaded.Seed.Grid.Rows);
        Assert.Equal("", loaded.Seed.Grid.Clues[0].Clue);
        Assert.Equal("", loaded.Seed.Grid.Clues[0].Enumeration);
        Assert.Contains(CrosswordGridValidator.Validate(loaded.Seed.Grid).Errors, issue => issue.Code == "missing-clue");
        Assert.Equal(2, (await repository.GetAuditAsync(id)).Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stale_save_is_rejected_without_overwrite_or_extra_audit(bool ef) => Run(ef, async (repository, _) =>
    {
        var seed = CrosswordSampleData.Load()[0];
        var id = await repository.CreateDraftAsync(seed, Creator, "editor@test.local");
        var before = (await repository.GetByIdAsync(id))!;
        await repository.SaveDraftAsync(id, seed with { Title = "First edit" }, before.RowVersion, "editor@test.local");
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.SaveDraftAsync(id,
            seed with { Title = "Stale edit" }, before.RowVersion, "editor@test.local"));
        Assert.Equal("First edit", (await repository.GetByIdAsync(id))!.Seed.Title);
        Assert.Equal(2, (await repository.GetAuditAsync(id)).Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Explicit_published_import_is_validated_and_cannot_use_draft_edit_path(bool ef) => Run(ef, async (repository, clock) =>
    {
        await repository.ImportAsync([CrosswordSampleData.Load()[0]], Creator, "test-import", publish: true);
        var item = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(CrosswordStatus.Published, item.Status);
        Assert.Equal(clock.GetUtcNow(), item.PublishedAt);
        Assert.Equal(clock.GetUtcNow(), item.PublishAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveDraftAsync(item.Id, item.Seed,
            item.RowVersion, "editor@test.local"));
        Assert.Single(await repository.GetAuditAsync(item.Id));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Returned_tokens_are_copies_and_unknown_ids_return_null(bool ef) => Run(ef, async (repository, _) =>
    {
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
        var id = await repository.CreateDraftAsync(CrosswordSampleData.Load()[0], Creator, "editor@test.local");
        var item = (await repository.GetByIdAsync(id))!;
        var token = item.RowVersion.ToArray();
        item.RowVersion[0] ^= 255;
        Assert.Equal(token, (await repository.GetByIdAsync(id))!.RowVersion);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Metadata_actor_and_duplicate_slug_failures_preserve_existing_rows(bool ef) => Run(ef, async (repository, _) =>
    {
        var seed = CrosswordSampleData.Load()[0];
        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateDraftAsync(seed with { Title = new string('x', 201) }, Creator, "editor@test.local"));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateDraftAsync(seed, Creator, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ImportAsync([seed], Creator, new string('x', 321)));
        var id = await repository.CreateDraftAsync(seed, Creator, "editor@test.local");
        await Assert.ThrowsAnyAsync<Exception>(() => repository.CreateDraftAsync(seed, Creator, "editor@test.local"));
        Assert.Single(await repository.GetAllAsync());
        Assert.Single(await repository.GetAuditAsync(id));
        await repository.ImportAsync([CrosswordSampleData.Load()[1]], Creator, "test-import");
        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Publication_schedule_archive_and_unpublish_share_visibility_and_audit_rules(bool ef) => Run(ef, async (repository, clock) =>
    {
        var id = await repository.CreateDraftAsync(CrosswordSampleData.Load()[0], Creator, "editor");
        var item = (await repository.GetByIdAsync(id))!;
        Assert.False(CrosswordVisibility.IsPlayable(item, clock.GetUtcNow()));
        var publishAt = clock.GetUtcNow().AddHours(1);
        await repository.SetPublicationAsync(id, CrosswordStatus.Scheduled, publishAt, item.RowVersion, "editor");
        item = (await repository.GetByIdAsync(id))!;
        Assert.False(CrosswordVisibility.IsListed(item, clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(CrosswordVisibility.IsListed(item, clock.GetUtcNow()));
        Assert.True(CrosswordVisibility.IsPlayable(item, clock.GetUtcNow()));
        await repository.SetPublicationAsync(id, CrosswordStatus.Archived, null, item.RowVersion, "editor");
        item = (await repository.GetByIdAsync(id))!;
        Assert.False(CrosswordVisibility.IsListed(item, clock.GetUtcNow()));
        Assert.True(CrosswordVisibility.IsPlayable(item, clock.GetUtcNow()));
        Assert.Equal(publishAt, item.PublishedAt);
        await repository.SetPublicationAsync(id, CrosswordStatus.Draft, null, item.RowVersion, "editor");
        item = (await repository.GetByIdAsync(id))!;
        Assert.False(CrosswordVisibility.IsPlayable(item, clock.GetUtcNow()));
        Assert.Null(item.PublishAt);
        Assert.Equal(new[] { "Created", "Scheduled", "Archived", "Unpublished" }.Order(),
            (await repository.GetAuditAsync(id)).Select(log => log.Action).Order());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Publish_now_rejects_stale_changes_without_extra_audit(bool ef) => Run(ef, async (repository, clock) =>
    {
        var id = await repository.CreateDraftAsync(CrosswordSampleData.Load()[0], Creator, "editor");
        var original = (await repository.GetByIdAsync(id))!;
        await repository.SetPublicationAsync(id, CrosswordStatus.Published, null, original.RowVersion, "publisher");
        var published = (await repository.GetByIdAsync(id))!;
        Assert.Equal(clock.GetUtcNow(), published.PublishedAt);
        Assert.True(CrosswordVisibility.IsListed(published, clock.GetUtcNow()));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.SetPublicationAsync(
            id, CrosswordStatus.Archived, null, original.RowVersion, "stale"));
        Assert.Equal(CrosswordStatus.Published, (await repository.GetByIdAsync(id))!.Status);
        Assert.Equal(2, (await repository.GetAuditAsync(id)).Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Invalid_publication_or_never_published_archive_preserves_draft(bool ef) => Run(ef, async (repository, clock) =>
    {
        var seed = CrosswordSampleData.Load()[0];
        var id = await repository.CreateDraftAsync(seed, Creator, "editor");
        var item = (await repository.GetByIdAsync(id))!;
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetPublicationAsync(id,
            (CrosswordStatus)99, null, item.RowVersion, "editor"));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetPublicationAsync(id,
            CrosswordStatus.Scheduled, null, item.RowVersion, "editor"));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetPublicationAsync(id,
            CrosswordStatus.Scheduled, clock.GetUtcNow(), item.RowVersion, "editor"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetPublicationAsync(id,
            CrosswordStatus.Archived, null, item.RowVersion, "editor"));
        await repository.SaveDraftAsync(id, seed with { Grid = seed.Grid with { Clues = [] } }, item.RowVersion, "editor");
        item = (await repository.GetByIdAsync(id))!;
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetPublicationAsync(id,
            CrosswordStatus.Published, null, item.RowVersion, "editor"));
        Assert.Equal(CrosswordStatus.Draft, (await repository.GetByIdAsync(id))!.Status);
        Assert.Equal(2, (await repository.GetAuditAsync(id)).Count);
    });

    private static async Task Run(bool ef, Func<ICrosswordCatalogRepository, FakeTimeProvider, Task> scenario)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        if (!ef)
        {
            await scenario(new InMemoryCrosswordCatalogRepository(clock), clock);
            return;
        }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await scenario(new EfCrosswordCatalogRepository(db, clock), clock);
    }
}

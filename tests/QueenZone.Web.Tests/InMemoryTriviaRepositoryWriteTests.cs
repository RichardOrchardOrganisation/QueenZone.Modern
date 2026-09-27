using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryTriviaRepositoryWriteTests
{
    [Fact]
    public async Task CreateAndUpdate_persist_fact_fields()
    {
        var store = new SharedTriviaStore();
        var repository = new InMemoryTriviaRepository(store);

        var id = await repository.CreateAsync(
            new AdminTriviaDraft("Original", false, "Band", TriviaDifficulty.Easy, "Note"));

        var created = await repository.GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Original", created.Text);
        Assert.Equal("Band", created.Category);
        Assert.Equal(TriviaDifficulty.Easy, created.Difficulty);
        Assert.Equal("Note", created.Source);
        Assert.False(created.IsPublished);

        await repository.UpdateAsync(
            id,
            new AdminTriviaDraft("Updated", true, "Albums", TriviaDifficulty.Hard, "Revised"));

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Text);
        Assert.Equal("Albums", updated.Category);
        Assert.Equal(TriviaDifficulty.Hard, updated.Difficulty);
        Assert.Equal("Revised", updated.Source);
        Assert.True(updated.IsPublished);
    }

    [Fact]
    public async Task Update_missing_fact_throws()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateAsync(42, new AdminTriviaDraft("x", true)));
    }

    [Fact]
    public async Task Delete_removes_fact_and_missing_id_throws()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());
        var id = await repository.CreateAsync(new AdminTriviaDraft("Text", true));

        await repository.DeleteAsync(id);

        Assert.Null(await repository.GetByIdAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(id));
    }

    [Fact]
    public async Task GetRandomPublishedAsync_only_returns_published_facts()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());
        await repository.CreateAsync(new AdminTriviaDraft("Hidden", false, "Band"));
        var publishedId = await repository.CreateAsync(new AdminTriviaDraft("Visible", true, "Albums"));

        var fact = await repository.GetRandomPublishedAsync();

        Assert.NotNull(fact);
        Assert.Equal(publishedId, fact.Id);
        Assert.True(fact.IsPublished);
    }

    [Fact]
    public async Task GetRandomPublishedAsync_returns_null_when_nothing_is_published()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());
        await repository.CreateAsync(new AdminTriviaDraft("Hidden", false));

        Assert.Null(await repository.GetRandomPublishedAsync());
    }

    [Fact]
    public async Task GetAllAsync_returns_every_fact_newest_first()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());
        var firstId = await repository.CreateAsync(new AdminTriviaDraft("First", true));
        var secondId = await repository.CreateAsync(new AdminTriviaDraft("Second", false));

        var facts = await repository.GetAllAsync();

        Assert.Equal(2, facts.Count);
        Assert.Equal(secondId, facts[0].Id);
        Assert.Equal(firstId, facts[1].Id);
    }

    [Fact]
    public async Task SetPublishedAsync_toggles_publish_state()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());
        var id = await repository.CreateAsync(new AdminTriviaDraft("Text", false));

        await repository.SetPublishedAsync(id, true);

        var fact = await repository.GetByIdAsync(id);
        Assert.NotNull(fact);
        Assert.True(fact.IsPublished);
    }

    [Fact]
    public async Task SetPublishedAsync_missing_fact_throws()
    {
        var repository = new InMemoryTriviaRepository(new SharedTriviaStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetPublishedAsync(42, true));
    }

    [Fact]
    public async Task Clear_removes_facts_and_resets_ids()
    {
        var store = new SharedTriviaStore();
        var repository = new InMemoryTriviaRepository(store);
        await repository.CreateAsync(new AdminTriviaDraft("Keep then clear", true));
        Assert.NotEmpty(await repository.GetAllAsync());

        store.Clear();

        Assert.Empty(await repository.GetAllAsync());
        Assert.Equal(1, await repository.CreateAsync(new AdminTriviaDraft("After clear", true)));
    }
}

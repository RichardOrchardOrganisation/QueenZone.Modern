using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class HomePollRepositoryTests
{
    private readonly InMemoryHomePollRepository repository = new(new SharedHomePollStore());

    [Fact]
    public async Task GetCurrentAsync_returns_null_when_nothing_is_published()
    {
        var draftId = await repository.CreateAsync(
            new AdminHomePollDraft("Hidden?", ["A", "B"]),
            Guid.NewGuid());

        Assert.Null(await repository.GetCurrentAsync(null));

        var listed = await repository.GetAllAsync();
        Assert.Equal(draftId, listed[0].Id);
        Assert.False(listed[0].IsCurrent);
    }

    [Fact]
    public async Task Publish_makes_exactly_one_current_and_keeps_previous_votes()
    {
        var first = await repository.CreateAsync(new AdminHomePollDraft("First?", ["Yes", "No"]), Guid.NewGuid());
        var second = await repository.CreateAsync(new AdminHomePollDraft("Second?", ["Left", "Right"]), Guid.NewGuid());
        await repository.PublishAsync(first);
        var firstResults = await repository.GetCurrentAsync(null);
        Assert.NotNull(firstResults);
        var voter = Guid.NewGuid();
        await repository.CastVoteAsync(firstResults!.Options[0].OptionId, voter);

        await repository.PublishAsync(second);

        var current = await repository.GetCurrentAsync(null);
        Assert.NotNull(current);
        Assert.Equal(second, current!.PollId);
        Assert.Equal("Second?", current.Question);
        Assert.Equal(0, current.TotalVotes);

        var previous = await repository.GetByIdAsync(first);
        Assert.NotNull(previous);
        Assert.False(previous!.IsCurrent);
        Assert.Equal(1, previous.VoteCount);
    }

    [Fact]
    public async Task Hide_current_then_publish_other_makes_only_the_new_poll_current()
    {
        var first = await repository.CreateAsync(new AdminHomePollDraft("First?", ["Yes", "No"]), Guid.NewGuid());
        var second = await repository.CreateAsync(new AdminHomePollDraft("Second?", ["Left", "Right"]), Guid.NewGuid());
        await repository.PublishAsync(first);
        await repository.HideAsync(first);
        await repository.PublishAsync(second);

        var current = await repository.GetCurrentAsync(null);
        Assert.Equal(second, current!.PollId);
        Assert.False((await repository.GetByIdAsync(first))!.IsCurrent);
        Assert.True((await repository.GetByIdAsync(second))!.IsCurrent);
    }

    [Fact]
    public async Task Republish_of_the_current_poll_is_idempotent_success()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var firstPublishedAt = (await repository.GetByIdAsync(pollId))!.PublishedAt;

        await repository.PublishAsync(pollId);

        var again = await repository.GetByIdAsync(pollId);
        Assert.True(again!.IsCurrent);
        Assert.Equal(firstPublishedAt, again.PublishedAt);
        Assert.Equal(pollId, (await repository.GetCurrentAsync(null))!.PollId);
    }

    [Fact]
    public async Task Guest_sees_counts_and_cannot_be_marked_as_voter()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var published = await repository.GetCurrentAsync(null);
        await repository.CastVoteAsync(published!.Options[0].OptionId, Guid.NewGuid());

        var guest = await repository.GetCurrentAsync(null);
        Assert.NotNull(guest);
        Assert.Equal(1, guest!.TotalVotes);
        Assert.Equal(100, guest.Options[0].Percentage);
        Assert.False(guest.ViewerHasVoted);
        Assert.Null(guest.SelectedOptionId);
        Assert.False(guest.IsClosed);
    }

    [Fact]
    public async Task Member_votes_once_and_second_ballot_is_rejected()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var published = await repository.GetCurrentAsync(null);
        var member = Guid.NewGuid();
        var firstOption = published!.Options[0].OptionId;
        var secondOption = published.Options[1].OptionId;

        await repository.CastVoteAsync(firstOption, member);
        var after = await repository.GetCurrentAsync(member);
        Assert.True(after!.ViewerHasVoted);
        Assert.Equal(firstOption, after.SelectedOptionId);
        Assert.Equal(1, after.TotalVotes);

        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repository.CastVoteAsync(secondOption, member));
        Assert.Equal(ForumPollVoteException.AlreadyVoted, ex.Code);

        var again = await repository.GetCurrentAsync(member);
        Assert.Equal(1, again!.TotalVotes);
        Assert.Equal(firstOption, again.SelectedOptionId);
    }

    [Fact]
    public async Task Closed_poll_rejects_votes_and_stays_on_home()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        await repository.CloseAsync(pollId);

        var current = await repository.GetCurrentAsync(null);
        Assert.NotNull(current);
        Assert.True(current!.IsClosed);
        Assert.Equal(pollId, current.PollId);

        var published = await repository.GetByIdAsync(pollId);
        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repository.CastVoteAsync(published!.Options[0].OptionId, Guid.NewGuid()));
        Assert.Equal(ForumPollVoteException.Closed, ex.Code);
    }

    [Fact]
    public async Task Hide_removes_poll_from_home_without_deleting_votes()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var published = await repository.GetCurrentAsync(null);
        await repository.CastVoteAsync(published!.Options[0].OptionId, Guid.NewGuid());
        await repository.HideAsync(pollId);

        Assert.Null(await repository.GetCurrentAsync(null));
        var hidden = await repository.GetByIdAsync(pollId);
        Assert.NotNull(hidden);
        Assert.False(hidden!.IsCurrent);
        Assert.Equal(1, hidden.VoteCount);
    }

    [Fact]
    public async Task Options_cannot_change_after_the_first_vote()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.PublishAsync(pollId);
        var published = await repository.GetCurrentAsync(null);
        await repository.CastVoteAsync(published!.Options[0].OptionId, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<HomePollException>(() =>
            repository.UpdateAsync(pollId, new AdminHomePollDraft("Changed?", ["X", "Y"])));
        Assert.Equal(HomePollException.HasVotes, ex.Code);

        var delete = await Assert.ThrowsAsync<HomePollException>(() => repository.DeleteAsync(pollId));
        Assert.Equal(HomePollException.HasVotes, delete.Code);
    }

    [Fact]
    public async Task Zero_vote_draft_can_be_edited_and_deleted()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        await repository.UpdateAsync(pollId, new AdminHomePollDraft("Edited?", ["One", "Two", "Three"]));
        var updated = await repository.GetByIdAsync(pollId);
        Assert.Equal("Edited?", updated!.Question);
        Assert.Equal(3, updated.Options.Count);

        await repository.DeleteAsync(pollId);
        Assert.Null(await repository.GetByIdAsync(pollId));
    }

    [Fact]
    public async Task Vote_on_unpublished_option_is_not_found()
    {
        var pollId = await repository.CreateAsync(new AdminHomePollDraft("Q?", ["A", "B"]), Guid.NewGuid());
        var draft = await repository.GetByIdAsync(pollId);
        var ex = await Assert.ThrowsAsync<ForumPollVoteException>(() =>
            repository.CastVoteAsync(draft!.Options[0].OptionId, Guid.NewGuid()));
        Assert.Equal(ForumPollVoteException.NotFound, ex.Code);
    }

    [Fact]
    public async Task Clear_removes_polls_and_votes()
    {
        var store = new SharedHomePollStore();
        var polls = new InMemoryHomePollRepository(store);
        var id = await polls.CreateAsync(new AdminHomePollDraft("Clear me?", ["A", "B"]), Guid.NewGuid());
        await polls.PublishAsync(id);
        Assert.NotNull(await polls.GetCurrentAsync(null));

        store.Clear();

        Assert.Null(await polls.GetCurrentAsync(null));
        Assert.Empty(await polls.GetAllAsync());
    }
}

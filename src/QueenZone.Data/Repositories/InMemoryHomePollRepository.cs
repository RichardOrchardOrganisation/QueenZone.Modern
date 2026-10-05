using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryHomePollRepository(
    SharedHomePollStore store,
    TimeProvider? timeProvider = null) : IHomePollRepository
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public Task<HomePollResults?> GetCurrentAsync(
        Guid? viewerMemberId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((polls, votes) =>
        {
            var poll = polls.SingleOrDefault(item => item.IsCurrent);
            return poll is null ? null : BuildResults(poll, votes, viewerMemberId);
        }));

    public Task<IReadOnlyList<HomePollAdminItem>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((polls, votes) =>
        {
            IReadOnlyList<HomePollAdminItem> items = polls
                .OrderByDescending(item => item.IsCurrent)
                .ThenByDescending(item => item.CreatedAt)
                .Select(poll => new HomePollAdminItem(
                    poll.Id,
                    poll.Question,
                    poll.IsCurrent,
                    poll.ClosedAt,
                    poll.PublishedAt,
                    poll.CreatedAt,
                    votes.Count(vote => vote.PollId == poll.Id),
                    poll.Options.Count))
                .ToList();
            return items;
        }));

    public Task<HomePollAdminDetail?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((polls, votes) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id);
            if (poll is null)
            {
                return null;
            }

            var results = BuildResults(poll, votes, viewerMemberId: null);
            return new HomePollAdminDetail(
                poll.Id,
                poll.Question,
                poll.IsCurrent,
                poll.ClosedAt,
                poll.PublishedAt,
                poll.CreatedAt,
                results.TotalVotes,
                results.Options);
        }));

    public Task<Guid> CreateAsync(
        AdminHomePollDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default)
    {
        var errors = HomePollValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        return Task.FromResult(store.Write((polls, _) =>
        {
            var entity = EfHomePollRepository.BuildEntity(draft, createdByMemberId, timeProvider.GetUtcNow());
            polls.Add(entity);
            return entity.Id;
        }));
    }

    public Task UpdateAsync(Guid id, AdminHomePollDraft draft, CancellationToken cancellationToken = default)
    {
        var errors = HomePollValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        store.Write((polls, votes) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");
            EnsureNoVotes(id, votes, "Question and options cannot be changed after the first vote.");
            poll.Question = draft.Question.Trim();
            var options = HomePollValidation.NormalizeOptions(draft.Options);
            poll.Options = options
                .Select((text, index) => new HomePollOptionEntity
                {
                    Id = Guid.NewGuid(),
                    PollId = poll.Id,
                    OptionText = text,
                    DisplayOrder = index,
                })
                .ToList();
        });
        return Task.CompletedTask;
    }

    public Task PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((polls, _) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");
            var now = timeProvider.GetUtcNow();
            foreach (var other in polls.Where(item => item.IsCurrent && item.Id != id))
            {
                other.IsCurrent = false;
            }

            poll.IsCurrent = true;
            poll.PublishedAt ??= now;
        });
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((polls, _) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");
            poll.ClosedAt ??= timeProvider.GetUtcNow();
        });
        return Task.CompletedTask;
    }

    public Task HideAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((polls, _) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");
            poll.IsCurrent = false;
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((polls, votes) =>
        {
            var poll = polls.SingleOrDefault(item => item.Id == id)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");
            EnsureNoVotes(id, votes, "This poll has votes and cannot be deleted.");
            polls.Remove(poll);
        });
        return Task.CompletedTask;
    }

    public Task CastVoteAsync(Guid optionId, Guid memberId, CancellationToken cancellationToken = default)
    {
        store.Write((polls, votes) =>
        {
            var option = polls
                .SelectMany(poll => poll.Options.Select(item => (poll, option: item)))
                .SingleOrDefault(pair => pair.option.Id == optionId);
            if (option.poll is null)
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.InvalidOptions,
                    "That option is not valid for the current poll.");
            }

            if (!option.poll.IsCurrent)
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.NotFound,
                    "This poll is not the current Home poll.");
            }

            if (option.poll.ClosedAt is not null)
            {
                throw new ForumPollVoteException(ForumPollVoteException.Closed, "This poll is closed.");
            }

            if (votes.Any(vote => vote.PollId == option.poll.Id && vote.MemberAccountId == memberId))
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.AlreadyVoted,
                    "You have already voted in this poll. Votes cannot be changed.");
            }

            votes.Add(new HomePollVoteEntity
            {
                Id = Guid.NewGuid(),
                PollId = option.poll.Id,
                OptionId = option.option.Id,
                MemberAccountId = memberId,
                VotedAt = timeProvider.GetUtcNow(),
            });
        });
        return Task.CompletedTask;
    }

    private static HomePollResults BuildResults(
        HomePollEntity poll,
        IReadOnlyList<HomePollVoteEntity> votes,
        Guid? viewerMemberId)
    {
        var optionCounts = votes
            .Where(vote => vote.PollId == poll.Id)
            .GroupBy(vote => vote.OptionId)
            .ToDictionary(group => group.Key, group => group.Count());
        Guid? selected = null;
        if (viewerMemberId is Guid memberId)
        {
            selected = votes
                .Where(vote => vote.PollId == poll.Id && vote.MemberAccountId == memberId)
                .Select(vote => (Guid?)vote.OptionId)
                .SingleOrDefault();
        }

        return HomePollResultsBuilder.Build(
            new HomePollMetadata(poll.Id, poll.Question, poll.ClosedAt, poll.CreatedAt, poll.PublishedAt),
            poll.Options.Select(item => (item.Id, item.OptionText, item.DisplayOrder)).ToList(),
            optionCounts,
            selected);
    }

    private static void EnsureNoVotes(
        Guid pollId,
        IReadOnlyList<HomePollVoteEntity> votes,
        string message)
    {
        if (votes.Any(vote => vote.PollId == pollId))
        {
            throw new HomePollException(HomePollException.HasVotes, message);
        }
    }
}

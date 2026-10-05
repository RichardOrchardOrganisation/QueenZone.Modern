using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryForumPollRepository(TimeProvider? timeProvider = null) : IForumPollRepository
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private readonly List<ForumPollEntity> polls = [];
    private readonly List<ForumPollVoteEntity> votes = [];
    private readonly Dictionary<int, long> topicToThreadId = new();

    /// <summary>Register a topic id for CreatePollAsync (tests / in-memory write path).</summary>
    public void RegisterTopic(int legacyTopicId, long threadId = 0)
    {
        lock (sync)
        {
            topicToThreadId[legacyTopicId] = threadId == 0 ? legacyTopicId : threadId;
        }
    }

    public Task<Guid> CreatePollAsync(
        int legacyTopicId,
        NewForumPoll poll,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (!topicToThreadId.TryGetValue(legacyTopicId, out var threadId))
            {
                // Allow creating against unknown topics in tests by synthetic thread id.
                threadId = legacyTopicId;
                topicToThreadId[legacyTopicId] = threadId;
            }

            if (polls.Any(item => item.LegacyTopicId == legacyTopicId))
            {
                throw new InvalidOperationException("This thread already has a poll.");
            }

            var entity = EfForumPollRepository.BuildPollEntity(
                threadId,
                legacyTopicId,
                poll,
                timeProvider.GetUtcNow());
            polls.Add(entity);
            return Task.FromResult(entity.Id);
        }
    }

    public Task<ForumPollResults?> GetPollWithResultsAsync(
        int legacyTopicId,
        Guid? viewerMemberId,
        bool viewerIsAdmin = false,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var poll = polls.SingleOrDefault(item => item.LegacyTopicId == legacyTopicId);
            if (poll is null)
            {
                return Task.FromResult<ForumPollResults?>(null);
            }

            var pollVotes = votes
                .Where(vote => vote.PollId == poll.Id)
                .Select(vote => (vote.OptionId, vote.MemberAccountId))
                .ToList();

            return Task.FromResult<ForumPollResults?>(
                EfForumPollRepository.BuildResults(
                    poll,
                    pollVotes,
                    viewerMemberId,
                    viewerIsAdmin,
                    timeProvider.GetUtcNow()));
        }
    }

    public Task CastVoteAsync(
        Guid pollId,
        Guid memberId,
        IEnumerable<Guid> optionIds,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var selected = optionIds.Distinct().ToArray();
            if (selected.Length == 0)
            {
                throw new ForumPollVoteException(ForumPollVoteException.InvalidOptions, "Select at least one option.");
            }

            var poll = polls.SingleOrDefault(item => item.Id == pollId)
                ?? throw new ForumPollVoteException(ForumPollVoteException.NotFound, "Poll was not found.");

            var now = timeProvider.GetUtcNow();
            if (EfForumPollRepository.IsClosed(poll, now))
            {
                throw new ForumPollVoteException(ForumPollVoteException.Closed, "This poll is closed.");
            }

            if (votes.Any(vote => vote.PollId == pollId && vote.MemberAccountId == memberId))
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.AlreadyVoted,
                    "You have already voted in this poll. Votes cannot be changed.");
            }

            if (!poll.IsMultiChoice && selected.Length > 1)
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.MaxChoices,
                    "This poll only allows one choice.");
            }

            int maxChoices;
            if (!poll.IsMultiChoice)
            {
                maxChoices = 1;
            }
            else
            {
                maxChoices = poll.MaxChoices is int max && max > 0
                    ? Math.Min(max, poll.Options.Count)
                    : poll.Options.Count;
            }

            if (selected.Length > maxChoices)
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.MaxChoices,
                    $"You can select at most {maxChoices} option(s).");
            }

            var valid = poll.Options.Select(option => option.Id).ToHashSet();
            if (selected.Any(id => !valid.Contains(id)))
            {
                throw new ForumPollVoteException(ForumPollVoteException.InvalidOptions, "One or more options are invalid.");
            }

            foreach (var optionId in selected)
            {
                votes.Add(new ForumPollVoteEntity
                {
                    Id = Guid.NewGuid(),
                    PollId = pollId,
                    OptionId = optionId,
                    MemberAccountId = memberId,
                    VotedAt = now,
                });
            }

            return Task.CompletedTask;
        }
    }

    public Task ClosePollAsync(
        Guid pollId,
        Guid memberId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var poll = polls.SingleOrDefault(item => item.Id == pollId)
                ?? throw new ForumPollVoteException(ForumPollVoteException.NotFound, "Poll was not found.");

            if (!isAdmin && poll.CreatedByMemberId != memberId)
            {
                throw new ForumPollVoteException(
                    ForumPollVoteException.Forbidden,
                    "Only the thread author or an admin can close this poll.");
            }

            var now = timeProvider.GetUtcNow();
            if (!EfForumPollRepository.IsClosed(poll, now))
            {
                poll.ClosedAt = now;
            }

            return Task.CompletedTask;
        }
    }

    public IReadOnlyList<ForumPollVoteEntity> GetVotes(Guid pollId)
    {
        lock (sync)
        {
            return votes.Where(vote => vote.PollId == pollId).ToList();
        }
    }
}

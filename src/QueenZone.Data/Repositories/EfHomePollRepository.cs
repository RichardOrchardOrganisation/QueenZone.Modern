using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfHomePollRepository(QueenZoneDbContext dbContext, TimeProvider timeProvider)
    : IHomePollRepository
{
    public async Task<HomePollResults?> GetCurrentAsync(
        Guid? viewerMemberId,
        CancellationToken cancellationToken = default)
    {
        var poll = await dbContext.HomePolls
            .AsNoTracking()
            .Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.IsCurrent, cancellationToken);
        if (poll is null)
        {
            return null;
        }

        return await BuildResultsAsync(poll, viewerMemberId, cancellationToken);
    }

    public async Task<IReadOnlyList<HomePollAdminItem>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var polls = await dbContext.HomePolls
            .AsNoTracking()
            .Include(item => item.Options)
            .OrderByDescending(item => item.IsCurrent)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var voteCounts = await dbContext.HomePollVotes
            .AsNoTracking()
            .GroupBy(vote => vote.PollId)
            .Select(group => new { PollId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PollId, row => row.Count, cancellationToken);

        return polls
            .Select(poll => new HomePollAdminItem(
                poll.Id,
                poll.Question,
                poll.IsCurrent,
                poll.ClosedAt,
                poll.PublishedAt,
                poll.CreatedAt,
                voteCounts.GetValueOrDefault(poll.Id),
                poll.Options.Count))
            .ToList();
    }

    public async Task<HomePollAdminDetail?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var poll = await dbContext.HomePolls
            .AsNoTracking()
            .Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (poll is null)
        {
            return null;
        }

        var results = await BuildResultsAsync(poll, viewerMemberId: null, cancellationToken);
        return new HomePollAdminDetail(
            poll.Id,
            poll.Question,
            poll.IsCurrent,
            poll.ClosedAt,
            poll.PublishedAt,
            poll.CreatedAt,
            results.TotalVotes,
            results.Options);
    }

    public async Task<Guid> CreateAsync(
        AdminHomePollDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default)
    {
        var errors = HomePollValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        var now = timeProvider.GetUtcNow();
        var entity = BuildEntity(draft, createdByMemberId, now);
        dbContext.HomePolls.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(Guid id, AdminHomePollDraft draft, CancellationToken cancellationToken = default)
    {
        var errors = HomePollValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        var poll = await dbContext.HomePolls
            .Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");

        await EnsureNoVotesAsync(
            id,
            "Question and options cannot be changed after the first vote.",
            cancellationToken);

        poll.Question = draft.Question.Trim();
        dbContext.HomePollOptions.RemoveRange(poll.Options);
        var options = HomePollValidation.NormalizeOptions(draft.Options);
        for (var index = 0; index < options.Count; index++)
        {
            dbContext.HomePollOptions.Add(new HomePollOptionEntity
            {
                Id = Guid.NewGuid(),
                PollId = poll.Id,
                OptionText = options[index],
                DisplayOrder = index,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var poll = await dbContext.HomePolls
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");

            var now = timeProvider.GetUtcNow();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var current = await dbContext.HomePolls
                .Where(item => item.IsCurrent && item.Id != id)
                .ToListAsync(cancellationToken);
            foreach (var other in current)
            {
                other.IsCurrent = false;
            }

            // SQL Server evaluates UX_HomePolls_IsCurrent per UPDATE. One SaveChanges can
            // emit the promote first and collide on (1). Persist demotions, then promote.
            await dbContext.SaveChangesAsync(cancellationToken);

            poll.IsCurrent = true;
            poll.PublishedAt ??= now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var poll = await dbContext.HomePolls
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");

        if (poll.ClosedAt is not null)
        {
            return;
        }

        poll.ClosedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task HideAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var poll = await dbContext.HomePolls
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");

        poll.IsCurrent = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var poll = await dbContext.HomePolls
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new HomePollException(HomePollException.NotFound, "Poll was not found.");

        await EnsureNoVotesAsync(id, "This poll has votes and cannot be deleted.", cancellationToken);
        dbContext.HomePolls.Remove(poll);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CastVoteAsync(Guid optionId, Guid memberId, CancellationToken cancellationToken = default)
    {
        var option = await dbContext.HomePollOptions
            .Include(item => item.Poll)
            .SingleOrDefaultAsync(item => item.Id == optionId, cancellationToken)
            ?? throw new ForumPollVoteException(
                ForumPollVoteException.InvalidOptions,
                "That option is not valid for the current poll.");

        var poll = option.Poll
            ?? throw new ForumPollVoteException(ForumPollVoteException.NotFound, "Poll was not found.");

        if (!poll.IsCurrent)
        {
            throw new ForumPollVoteException(
                ForumPollVoteException.NotFound,
                "This poll is not the current Home poll.");
        }

        if (poll.ClosedAt is not null)
        {
            throw new ForumPollVoteException(ForumPollVoteException.Closed, "This poll is closed.");
        }

        var alreadyVoted = await dbContext.HomePollVotes
            .AsNoTracking()
            .AnyAsync(vote => vote.PollId == poll.Id && vote.MemberAccountId == memberId, cancellationToken);
        if (alreadyVoted)
        {
            throw new ForumPollVoteException(
                ForumPollVoteException.AlreadyVoted,
                "You have already voted in this poll. Votes cannot be changed.");
        }

        dbContext.HomePollVotes.Add(new HomePollVoteEntity
        {
            Id = Guid.NewGuid(),
            PollId = poll.Id,
            OptionId = option.Id,
            MemberAccountId = memberId,
            VotedAt = timeProvider.GetUtcNow(),
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            throw new ForumPollVoteException(
                ForumPollVoteException.AlreadyVoted,
                "You have already voted in this poll. Votes cannot be changed.");
        }
    }

    internal static HomePollEntity BuildEntity(
        AdminHomePollDraft draft,
        Guid createdByMemberId,
        DateTimeOffset createdAt)
    {
        var pollId = Guid.NewGuid();
        var options = HomePollValidation.NormalizeOptions(draft.Options);
        return new HomePollEntity
        {
            Id = pollId,
            Question = draft.Question.Trim(),
            IsCurrent = false,
            ClosedAt = null,
            CreatedAt = createdAt,
            PublishedAt = null,
            CreatedByMemberId = createdByMemberId,
            Options = options
                .Select((text, index) => new HomePollOptionEntity
                {
                    Id = Guid.NewGuid(),
                    PollId = pollId,
                    OptionText = text,
                    DisplayOrder = index,
                })
                .ToList(),
        };
    }

    private async Task<HomePollResults> BuildResultsAsync(
        HomePollEntity poll,
        Guid? viewerMemberId,
        CancellationToken cancellationToken)
    {
        var optionCounts = await dbContext.HomePollVotes
            .AsNoTracking()
            .Where(vote => vote.PollId == poll.Id)
            .GroupBy(vote => vote.OptionId)
            .Select(group => new { OptionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.OptionId, row => row.Count, cancellationToken);

        Guid? selectedOptionId = null;
        if (viewerMemberId is Guid memberId)
        {
            selectedOptionId = await dbContext.HomePollVotes
                .AsNoTracking()
                .Where(vote => vote.PollId == poll.Id && vote.MemberAccountId == memberId)
                .Select(vote => (Guid?)vote.OptionId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return HomePollResultsBuilder.Build(
            poll.Id,
            poll.Question,
            poll.ClosedAt,
            poll.CreatedAt,
            poll.PublishedAt,
            poll.Options.Select(option => (option.Id, option.OptionText, option.DisplayOrder)).ToList(),
            optionCounts,
            selectedOptionId);
    }

    private async Task EnsureNoVotesAsync(Guid pollId, string message, CancellationToken cancellationToken)
    {
        var hasVotes = await dbContext.HomePollVotes
            .AsNoTracking()
            .AnyAsync(vote => vote.PollId == pollId, cancellationToken);
        if (hasVotes)
        {
            throw new HomePollException(HomePollException.HasVotes, message);
        }
    }
}

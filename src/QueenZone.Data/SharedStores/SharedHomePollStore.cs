using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class SharedHomePollStore
{
    private readonly object sync = new();
    private readonly List<HomePollEntity> polls = [];
    private readonly List<HomePollVoteEntity> votes = [];

    public T Read<T>(Func<IReadOnlyList<HomePollEntity>, IReadOnlyList<HomePollVoteEntity>, T> reader)
    {
        lock (sync)
        {
            return reader(polls, votes);
        }
    }

    public T Write<T>(Func<List<HomePollEntity>, List<HomePollVoteEntity>, T> writer)
    {
        lock (sync)
        {
            return writer(polls, votes);
        }
    }

    public void Write(Action<List<HomePollEntity>, List<HomePollVoteEntity>> writer)
    {
        lock (sync)
        {
            writer(polls, votes);
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            polls.Clear();
            votes.Clear();
        }
    }
}

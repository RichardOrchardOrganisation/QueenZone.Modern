namespace QueenZone.Data;

public sealed class SharedTriviaStore
{
    private readonly object sync = new();
    private readonly List<TriviaFactItem> facts = [];
    private int nextId = 1;

    public SharedTriviaStore()
    {
    }

    public SharedTriviaStore(IEnumerable<TriviaFactItem> seedFacts)
    {
        lock (sync)
        {
            facts.AddRange(seedFacts);
            nextId = facts.Count == 0 ? 1 : facts.Max(fact => fact.Id) + 1;
        }
    }

    public IReadOnlyList<TriviaFactItem> GetAll()
    {
        lock (sync)
        {
            return facts.OrderByDescending(fact => fact.CreatedAt).ThenByDescending(fact => fact.Id).ToList();
        }
    }

    public TriviaFactItem? GetById(int id)
    {
        lock (sync)
        {
            return facts.SingleOrDefault(fact => fact.Id == id);
        }
    }

    public TriviaFactItem? GetRandomPublished()
    {
        lock (sync)
        {
            var published = facts.Where(fact => fact.IsPublished).ToList();
            return published.Count == 0 ? null : published[Random.Shared.Next(published.Count)];
        }
    }

    public int Create(AdminTriviaDraft draft)
    {
        lock (sync)
        {
            var id = nextId++;
            facts.Add(new TriviaFactItem(
                id,
                draft.Text,
                DateTime.UtcNow,
                draft.IsPublished,
                draft.Category,
                draft.Difficulty,
                draft.Source));
            return id;
        }
    }

    public bool Update(int id, AdminTriviaDraft draft)
    {
        lock (sync)
        {
            var index = facts.FindIndex(fact => fact.Id == id);
            if (index < 0)
            {
                return false;
            }

            facts[index] = facts[index] with
            {
                Text = draft.Text,
                Category = draft.Category,
                Difficulty = draft.Difficulty,
                Source = draft.Source,
                IsPublished = draft.IsPublished,
            };
            return true;
        }
    }

    public bool Delete(int id)
    {
        lock (sync)
        {
            return facts.RemoveAll(fact => fact.Id == id) > 0;
        }
    }

    public bool SetPublished(int id, bool isPublished)
    {
        lock (sync)
        {
            var index = facts.FindIndex(fact => fact.Id == id);
            if (index < 0)
            {
                return false;
            }

            facts[index] = facts[index] with { IsPublished = isPublished };
            return true;
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            facts.Clear();
            nextId = 1;
        }
    }
}

namespace QueenZone.Data;

public sealed class SharedQuoteStore
{
    private readonly object sync = new();
    private readonly List<QuoteItem> quotes = [];
    private int nextId = 1;

    public SharedQuoteStore()
    {
    }

    public SharedQuoteStore(IEnumerable<QuoteItem> seedQuotes)
    {
        lock (sync)
        {
            quotes.AddRange(seedQuotes);
            nextId = quotes.Count == 0 ? 1 : quotes.Max(quote => quote.Id) + 1;
        }
    }

    public IReadOnlyList<QuoteItem> GetAll()
    {
        lock (sync)
        {
            return quotes.OrderByDescending(quote => quote.CreatedAt).ThenByDescending(quote => quote.Id).ToList();
        }
    }

    public QuoteItem? GetById(int id)
    {
        lock (sync)
        {
            return quotes.SingleOrDefault(quote => quote.Id == id);
        }
    }

    public QuoteItem? GetRandomPublished()
    {
        lock (sync)
        {
            var published = quotes.Where(quote => quote.IsPublished).ToList();
            return published.Count == 0 ? null : published[Random.Shared.Next(published.Count)];
        }
    }

    public int Create(AdminQuoteDraft draft)
    {
        lock (sync)
        {
            var id = nextId++;
            quotes.Add(new QuoteItem(id, draft.Text, draft.WhoSaid, DateTime.UtcNow, draft.IsPublished, draft.Context));
            return id;
        }
    }

    public bool Update(int id, AdminQuoteDraft draft)
    {
        lock (sync)
        {
            var index = quotes.FindIndex(quote => quote.Id == id);
            if (index < 0)
            {
                return false;
            }

            quotes[index] = quotes[index] with
            {
                Text = draft.Text,
                WhoSaid = draft.WhoSaid,
                Context = draft.Context,
                IsPublished = draft.IsPublished,
            };
            return true;
        }
    }

    public bool Delete(int id)
    {
        lock (sync)
        {
            return quotes.RemoveAll(quote => quote.Id == id) > 0;
        }
    }

    public bool SetPublished(int id, bool isPublished)
    {
        lock (sync)
        {
            var index = quotes.FindIndex(quote => quote.Id == id);
            if (index < 0)
            {
                return false;
            }

            quotes[index] = quotes[index] with { IsPublished = isPublished };
            return true;
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            quotes.Clear();
            nextId = 1;
        }
    }
}

namespace QueenZone.Data;

public sealed class SharedBiographyStore
{
    private readonly object sync = new();
    private readonly List<BiographyChapterItem> chapters = [];
    private int nextId = 1;

    public SharedBiographyStore()
    {
    }

    public SharedBiographyStore(IEnumerable<BiographyChapterItem> seedChapters)
    {
        lock (sync)
        {
            chapters.AddRange(seedChapters);
            nextId = chapters.Count == 0 ? 1 : chapters.Max(chapter => chapter.Id) + 1;
        }
    }

    public IReadOnlyList<BiographyChapterItem> GetAll()
    {
        lock (sync)
        {
            return chapters.ToList();
        }
    }

    public BiographyChapterItem? GetById(int id)
    {
        lock (sync)
        {
            return chapters.SingleOrDefault(chapter => chapter.Id == id);
        }
    }

    public int Create(AdminBiographyDraft draft)
    {
        lock (sync)
        {
            var id = nextId++;
            chapters.Add(new BiographyChapterItem(
                id,
                draft.Title,
                draft.Summary,
                draft.Body,
                draft.DisplaySequence,
                DateTime.UtcNow));
            return id;
        }
    }

    public bool Update(int id, AdminBiographyDraft draft)
    {
        lock (sync)
        {
            var index = chapters.FindIndex(chapter => chapter.Id == id);
            if (index < 0)
            {
                return false;
            }

            var existing = chapters[index];
            chapters[index] = existing with
            {
                Title = draft.Title,
                Summary = draft.Summary,
                Body = draft.Body,
                DisplaySequence = draft.DisplaySequence,
                CreatedAt = DateTime.UtcNow
            };
            return true;
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            chapters.Clear();
            nextId = 1;
        }
    }

    internal void Seed(IEnumerable<BiographyChapterItem> seedChapters)
    {
        lock (sync)
        {
            chapters.Clear();
            chapters.AddRange(seedChapters);
            nextId = chapters.Count == 0 ? 1 : chapters.Max(chapter => chapter.Id) + 1;
        }
    }
}

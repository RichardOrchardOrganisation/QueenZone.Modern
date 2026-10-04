namespace QueenZone.Data.Entities;

public enum CrosswordStatus { Draft, Scheduled, Published, Archived }

public sealed class CrosswordEntity
{
    public Guid Id { get; set; }
    /// <summary>Public opaque identity for this grid/solution, stable across clue-only corrections.</summary>
    public Guid PlayVersion { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Difficulty { get; set; } = "easy";
    public string Style { get; set; } = "british";
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Row-major mask: # is a block, . is a white cell. Answers live only on entries.</summary>
    public string BlockMask { get; set; } = "";
    /// <summary>Private solution/draft rows, including unfinished cells; never projected into public payloads.</summary>
    public string SolutionRowsJson { get; set; } = "[]";
    public CrosswordStatus Status { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByEmail { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
    public ICollection<CrosswordEntryEntity> Entries { get; set; } = [];
}

public sealed class CrosswordEntryEntity
{
    public Guid Id { get; set; }
    public Guid CrosswordId { get; set; }
    public int Number { get; set; }
    public CrosswordDirection Direction { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string Answer { get; set; } = "";
    public string Clue { get; set; } = "";
    public string Enumeration { get; set; } = "";
    public string? Explanation { get; set; }
    public CrosswordEntity? Crossword { get; set; }
}

public sealed class CrosswordAuditLogEntity
{
    public Guid Id { get; set; }
    public Guid CrosswordId { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Summary { get; set; } = "";
}

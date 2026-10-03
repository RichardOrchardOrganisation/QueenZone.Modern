namespace QueenZone.Data;

/// <summary>Row shape shared by the modern and legacy <c>FindLegacyPostAsync</c> queries.</summary>
internal sealed class ForumLegacyPostRow
{
    public int TopicId { get; set; }

    public string? Title { get; set; }

    public int PostId { get; set; }

    public int PostIndex { get; set; }

    public static ForumLegacyPostLocation Map(ForumLegacyPostRow row) =>
        new(row.TopicId, row.Title?.Trim() ?? string.Empty, row.PostId, row.PostIndex);
}

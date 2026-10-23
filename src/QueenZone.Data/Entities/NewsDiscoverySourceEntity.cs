namespace QueenZone.Data.Entities;

public sealed class NewsDiscoverySourceEntity
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string HomepageUrl { get; set; } = string.Empty;

    public string? FeedOrSiteUrl { get; set; }

    public NewsDiscoverySourceType SourceType { get; set; }

    public NewsDiscoveryTrustTier TrustTier { get; set; }

    public int PollIntervalMinutes { get; set; }

    public bool Enabled { get; set; }

    public string? RelevanceKeywords { get; set; }

    public int ConsecutiveFailureCount { get; set; }

    public DateTime? LastFetchedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

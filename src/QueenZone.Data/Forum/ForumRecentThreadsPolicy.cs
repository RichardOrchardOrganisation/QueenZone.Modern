namespace QueenZone.Data;

/// <summary>Board exclusions for cross-board latest-topic promotion only.</summary>
public static class ForumRecentThreadsPolicy
{
    // Queenzone.com: website support and app testing. Keep board, topic, search and
    // sitemap access intact; filter before Take so other boards fill the feed.
    public const int WebsiteDiscussionBoardId = 7;
}

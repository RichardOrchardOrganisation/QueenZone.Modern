namespace QueenZone.Data;

/// <summary>
/// Where a legacy forum post ID (topic starter or reply) sits in the public archive.
/// Old QueenZone links such as <c>forum_topic_view.aspx?Q={id}</c> carry either kind of ID.
/// </summary>
/// <param name="TopicId">Legacy topic ID the post belongs to (the public topic route ID).</param>
/// <param name="Title">Topic title, used for the canonical slug.</param>
/// <param name="PostId">Legacy post ID that was looked up.</param>
/// <param name="PostIndex">Zero-based position among the topic's visible posts, in display order.</param>
public sealed record ForumLegacyPostLocation(int TopicId, string Title, int PostId, int PostIndex);

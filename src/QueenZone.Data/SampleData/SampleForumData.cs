namespace QueenZone.Data;

public static class SampleForumData
{
    public static IReadOnlyList<ForumCategoryItem> CreateSeedCategories() =>
    [
        new(
            1,
            "The Music",
            "Albums, songs, lyrics and the catalogue, track by track.",
            41_200,
            new DateTime(2024, 6, 12, 14, 0, 0, DateTimeKind.Utc),
            "Ranking every studio album",
            10),
        new(
            2,
            "Live & Tours",
            "Setlists, bootlegs and memories from every era of touring.",
            28_900,
            new DateTime(2024, 6, 12, 12, 0, 0, DateTimeKind.Utc),
            "Magic Tour — night by night",
            20),
        new(
            3,
            "Recordings & Rarities",
            "Sessions, outtakes, demos and the hunt for lost tapes.",
            19_300,
            new DateTime(2024, 6, 11, 18, 0, 0, DateTimeKind.Utc),
            "Unheard Montreux session tapes",
            30),
        new(
            4,
            "The Archive Project",
            "Restoring and cataloguing the original Queenzone.com archive.",
            6_100,
            new DateTime(2024, 6, 10, 9, 0, 0, DateTimeKind.Utc),
            "Earls Court 1977 negatives",
            40),
        new(
            5,
            "Gear & Technique",
            "The Red Special, amps, harmonies and how the sound was made.",
            11_400,
            new DateTime(2024, 6, 9, 16, 0, 0, DateTimeKind.Utc),
            "Brian May's Red Special mods",
            50),
        new(
            6,
            "The Lounge",
            "Introductions, off-topic and everything in between.",
            14_800,
            new DateTime(2024, 6, 12, 8, 0, 0, DateTimeKind.Utc),
            "How did you find Queenzone?",
            60)
    ];

    public static ForumArchiveStats CreateSeedStats()
    {
        var categories = CreateSeedCategories();
        return ForumArchiveStats.FromCategories(categories, 12_600);
    }

    public static IReadOnlyList<ForumTopicItem> CreateSeedTopics(int forumId)
    {
        if (forumId != 1)
        {
            return [];
        }

        var topics = new List<ForumTopicItem>
        {
            new(1001, "Forum Guidelines", new DateTime(2024, 6, 12, 20, 4, 0, DateTimeKind.Utc), "Richard Orchard", 44, "waunakonor", true),
            new(1002, "Ranking every studio album", new DateTime(2024, 6, 12, 14, 0, 0, DateTimeKind.Utc), "brightonrock", 1284, "brightonrock", false),
            new(1003, "Queen 2017 NOTW expanded release?", new DateTime(2024, 6, 11, 13, 10, 0, DateTimeKind.Utc), "Sam99", 12, "SpaceGrey", false)
        };

        for (var id = 1004; id <= 1030; id++)
        {
            topics.Add(new ForumTopicItem(
                id,
                $"Archive sample thread {id}",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(1030 - id),
                "archive_member",
                id % 50,
                "archive_member",
                false));
        }

        return topics;
    }

    public static IReadOnlyList<ForumPostItem> CreateSeedPosts(int topicId)
    {
        // Deterministic archive-shaped browser fixture; never writes legacy rows.
        if (topicId == 1029)
        {
            return Enumerable.Range(1, 15).Select(index => new ForumPostItem(
                102900 + index,
                $"<p>Long thread post {index}.</p><p><a href=\"https://youtu.be/QZ{index:000000000}\">First video</a></p><p><a href=\"https://youtu.be/QZ{index + 15:000000000}\">Second video</a></p>",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                "archive_video_fan",
                "",
                10,
                new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AuthorLegacyUserId: 5010)).ToList();
        }

        if (topicId == 1030)
        {
            return Enumerable.Range(1, 16).Select(index => new ForumPostItem(
                103000 + index,
                index is 1 or 16
                    ? """<p>Before the shared video.</p><p><a href="http://youtube.com/watch?v=M7lc1UVf-VE&amp;t=1m30s">Original video</a></p><p><a href="https://youtu.be/abcdefghijk">Another video</a></p><p><a href="https://youtu.be/M7lc1UVf-VE?t=90">Duplicate link</a></p><blockquote><p><a href="https://youtu.be/12345678901">Quoted video</a></p></blockquote><p>Inline <a href="https://youtu.be/12345678901">sentence link</a> stays readable.</p><p>After the shared video.</p>"""
                    : $"Archive video fixture reply {index}.",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                "archive_video_fan",
                "https://youtu.be/M7lc1UVf-VE",
                10,
                new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AuthorLegacyUserId: 5002)).ToList();
        }

        if (topicId == 1002)
        {
            var posts = new List<ForumPostItem>
            {
                new(
                    1002,
                    "Where would you put <strong>A Night at the Opera</strong> in the ranking?",
                    new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                    "brightonrock",
                    "Queen collector since 1989.",
                    4_812,
                    new DateTime(2004, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                    [new ForumPostAttachment(
                        "anoto-setlist-scan.jpg",
                        284_712,
                        ForumAttachmentPaths.LegacyDownloadPath(1002))],
                    AuthorLegacyUserId: 5001),
                new(
                    1101,
                    "Top tier for me — side two is basically perfect.",
                    new DateTime(2024, 6, 1, 11, 15, 0, DateTimeKind.Utc),
                    "jazzfanz",
                    null,
                    921,
                    new DateTime(2011, 8, 2, 0, 0, 0, DateTimeKind.Utc),
                    [new ForumPostAttachment(
                        "opera-side-two-notes.pdf",
                        48_128,
                        ForumAttachmentPaths.LegacyDownloadPath(1101),
                        "application/pdf")]),
                new(
                    1102,
                    "I still prefer <em>Sheer Heart Attack</em> for raw energy.",
                    new DateTime(2024, 6, 2, 9, 30, 0, DateTimeKind.Utc),
                    "nightattheopera",
                    null,
                    388,
                    new DateTime(2016, 1, 20, 0, 0, 0, DateTimeKind.Utc))
            };

            for (var id = 1103; id <= 1125; id++)
            {
                posts.Add(new ForumPostItem(
                    id,
                    $"Archive reply {id} in the studio album ranking thread.",
                    new DateTime(2024, 6, 3, 8, 0, 0, DateTimeKind.Utc).AddHours(id - 1103),
                    "archive_member",
                    null,
                    id % 200,
                    new DateTime(2010, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                    AuthorLegacyUserId: 5002));
            }

            return posts;
        }

        if (topicId == 1001)
        {
            return
            [
                new ForumPostItem(
                    1001,
                    "Please keep discussion civil and on-topic. This is a read-only archive.",
                    new DateTime(2024, 6, 12, 20, 4, 0, DateTimeKind.Utc),
                    "Richard Orchard",
                    "Site owner",
                    12_400,
                    new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ];
        }

        return [];
    }

    public static IReadOnlyList<ForumTopicSitemapItem> CreateSeedTopicSitemapItems()
    {
        return CreateSeedCategories()
            .SelectMany(category => CreateSeedTopics(category.Id))
            .Where(topic => !string.IsNullOrWhiteSpace(topic.Title))
            .OrderBy(topic => topic.Id)
            .Select(topic => new ForumTopicSitemapItem(topic.Id, topic.Title, topic.LastActivityAt))
            .ToList();
    }

    public static ForumTopicHeader? TryGetSeedTopicHeader(int topicId)
    {
        var topics = CreateSeedTopics(1).Concat(CreateSeedTopics(2)).ToList();
        var topic = topics.SingleOrDefault(item => item.Id == topicId);
        if (topic is null)
        {
            return null;
        }

        return new ForumTopicHeader(topicId, topic.Title, 1, "The Music");
    }
}

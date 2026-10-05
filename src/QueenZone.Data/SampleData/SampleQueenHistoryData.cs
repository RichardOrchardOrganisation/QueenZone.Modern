namespace QueenZone.Data;

public static class SampleQueenHistoryData
{
    public static IReadOnlyList<QueenHistoryEvent> CreateSeedEvents() =>
    [
        Create(1, "Freddie Mercury born", "Freddie Mercury, Queen's lead vocalist and pianist, was born in Stone Town, Zanzibar.", new DateOnly(1946, 9, 5), QueenHistoryEventCategory.Birthday, 95, "freddie-mercury-born-1946-09-05"),
        Create(2, "Brian May born", "Brian May, Queen's lead guitarist, was born in Hampton, Middlesex, England.", new DateOnly(1947, 7, 19), QueenHistoryEventCategory.Birthday, 85, "brian-may-born-1947-07-19"),
        Create(3, "Roger Taylor born", "Roger Taylor, Queen's drummer, was born in King's Lynn, Norfolk, England.", new DateOnly(1949, 7, 26), QueenHistoryEventCategory.Birthday, 85, "roger-taylor-born-1949-07-26"),
        Create(4, "John Deacon born", "John Deacon, Queen's bassist, was born in Leicester, England.", new DateOnly(1951, 8, 19), QueenHistoryEventCategory.Birthday, 85, "john-deacon-born-1951-08-19"),
        Create(5, "Queen's Live Aid performance", "Queen perform a 21-minute set at Live Aid at Wembley Stadium, later widely acclaimed as one of rock's greatest live performances.", new DateOnly(1985, 7, 13), QueenHistoryEventCategory.Concert, 100, "queen-s-live-aid-performance-1985-07-13"),
        Create(6, "'Bohemian Rhapsody' released", "Queen release 'Bohemian Rhapsody' as a single in the UK.", new DateOnly(1975, 10, 31), QueenHistoryEventCategory.Release, 100, "bohemian-rhapsody-released-1975-10-31"),
        Create(7, "The Freddie Mercury Tribute Concert", "The Freddie Mercury Tribute Concert for AIDS Awareness is held at Wembley Stadium.", new DateOnly(1992, 4, 20), QueenHistoryEventCategory.Concert, 95, "the-freddie-mercury-tribute-concert-1992-04-20"),
        Create(8, "Queen released", "Queen's self-titled debut album is released in the UK.", new DateOnly(1973, 7, 13), QueenHistoryEventCategory.Release, 85, "queen-released-1973-07-13"),
        Create(9, "John Deacon joins Queen", "John Deacon joins Brian May, Freddie Mercury and Roger Taylor, completing Queen's classic line-up.", new DateOnly(1971, 3, 1), QueenHistoryEventCategory.Other, 80),
        Create(10, "QueenZone modernisation milestone", "The modern QueenZone rebuild tracks archive-first development and public restoration work.", new DateOnly(2026, 7, 3), QueenHistoryEventCategory.SiteHistory, 60),
    ];

    private static QueenHistoryEvent Create(
        int id,
        string title,
        string summary,
        DateOnly eventDate,
        QueenHistoryEventCategory category,
        int importance,
        string? wikipediaSourceKey = null) =>
        new(
            id,
            title,
            summary,
            eventDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            QueenHistoryDatePrecision.ExactDate,
            category,
            importance,
            wikipediaSourceKey is null ? QueenHistoryEventSourceType.Curated : QueenHistoryEventSourceType.Wikipedia,
            wikipediaSourceKey ?? $"curated:{id}",
            wikipediaSourceKey is null ? null : "https://en.wikipedia.org/wiki/Queen_(band)",
            true);
}

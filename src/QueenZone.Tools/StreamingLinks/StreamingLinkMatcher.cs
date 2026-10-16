using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QueenZone.Tools;

/// <summary>A scored candidate. <see cref="Flags"/> name things a reviewer should check.</summary>
internal sealed record ScoredMatch<T>(T Candidate, int Score, IReadOnlyList<string> Flags);

/// <summary>
/// Pure scoring for <c>suggest-streaming-links</c>. Scores are 0–100 and only rank candidates for a
/// human; nothing is applied automatically. Queen's catalogue is full of remasters, deluxe
/// editions and live versions, so those are surfaced as flags instead of being silently chosen.
/// </summary>
internal static partial class StreamingLinkMatcher
{
    public const string RemasterFlag = "remaster";

    public const string DeluxeFlag = "deluxe";

    public const string LiveFlag = "live";

    public const string CompilationFlag = "compilation";

    public const string EditionFlag = "edition";

    public const string MixFlag = "mix";

    /// <summary>Known legacy album labels whose catalogue names differ.</summary>
    public static string AlbumSearchTitle(string title) => Normalize(title) switch
    {
        "queen i" => "Queen",
        "greatest hits 1" => "Greatest Hits",
        "greatest hits 2" => "Greatest Hits II",
        "greatest hits 3" => "Greatest Hits III",
        "live at wembley 86" => "Live at Wembley Stadium",
        "rock montreal" => "Queen Rock Montreal",
        _ => title,
    };

    /// <summary>
    /// Lowercase, accent-free comparison key without parentheticals, bracketed text, " - 2011 Remaster"
    /// style suffixes or punctuation. "&amp;" compares equal to "and".
    /// </summary>
    public static string Normalize(string title)
    {
        var text = Bracketed().Replace(title, " ");
        text = DashSuffix().Replace(text, " ");
        text = text.Replace("&", " and ", StringComparison.Ordinal);
        text = RemoveDiacritics(text).ToLowerInvariant();
        text = text.Replace("’", "'", StringComparison.Ordinal)
            .Replace("'n'", " n ", StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);
        text = NonAlphanumeric().Replace(text, " ");
        return Whitespace().Replace(text, " ").Trim() switch
        {
            "doin alright" => "doing all right",
            "looser in the end" => "the loser in the end",
            "march of the black queen" => "the march of the black queen",
            "prophets song" => "the prophets song",
            "teo toriatte" => "teo torriatte",
            "mustahpa" => "mustapha",
            "its a kind of magic" => "a kind of magic",
            "just one year of love" => "one year of love",
            var normalized => normalized,
        };
    }

    public static IReadOnlyList<string> Flags(string candidateName, bool isCompilation, string localName)
    {
        var lower = candidateName.ToLowerInvariant();
        var localLower = localName.ToLowerInvariant();
        var flags = new List<string>();
        if (lower.Contains("remaster", StringComparison.Ordinal))
        {
            flags.Add(RemasterFlag);
        }

        if (lower.Contains("deluxe", StringComparison.Ordinal))
        {
            flags.Add(DeluxeFlag);
        }

        if (LiveVersion().IsMatch(lower) && !LiveWord().IsMatch(localLower))
        {
            flags.Add(LiveFlag);
        }

        if (isCompilation)
        {
            flags.Add(CompilationFlag);
        }

        if (lower.Contains("edition", StringComparison.Ordinal) && !lower.Contains("deluxe", StringComparison.Ordinal))
        {
            flags.Add(EditionFlag);
        }

        if (MixWord().IsMatch(lower))
        {
            flags.Add(MixFlag);
        }

        return flags;
    }

    public static ScoredMatch<CatalogAlbum>? BestAlbum(
        string albumName,
        int? releaseYear,
        int trackCount,
        IEnumerable<CatalogAlbum> candidates) =>
        candidates
            .Select(candidate => ScoreAlbum(albumName, releaseYear, trackCount, candidate))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Flags.Count)
            .FirstOrDefault();

    public static ScoredMatch<CatalogAlbum> ScoreAlbum(string albumName, int? releaseYear, int trackCount, CatalogAlbum candidate)
    {
        var titlePoints = TitlePoints(Normalize(AlbumSearchTitle(albumName)), Normalize(AlbumSearchTitle(candidate.Name)));
        if (titlePoints == 0)
        {
            // Without a title match the other signals are noise.
            return new ScoredMatch<CatalogAlbum>(candidate, 0, []);
        }

        var flags = Flags(candidate.Name, candidate.IsCompilation, albumName);
        var score = titlePoints
            + YearPoints(releaseYear, candidate.ReleaseYear)
            + TrackCountPoints(trackCount, candidate.TrackCount)
            + 10
            - FlagPenalty(flags);
        return new ScoredMatch<CatalogAlbum>(candidate, Math.Clamp(score, 0, 100), flags);
    }

    private static int TitlePoints(string local, string remote)
    {
        if (local.Length == 0)
        {
            return 0;
        }

        if (local == remote)
        {
            return 50;
        }

        return remote.Contains(local, StringComparison.Ordinal) ? 25 : 0;
    }

    private static int YearPoints(int? releaseYear, int? candidateYear) =>
        (releaseYear, candidateYear) switch
        {
            (int year, int other) when year == other => 20,
            (int year, int other) when Math.Abs(year - other) == 1 => 10,
            _ => 0,
        };

    private static int TrackCountPoints(int trackCount, int candidateTrackCount)
    {
        if (trackCount <= 0)
        {
            return 0;
        }

        var difference = Math.Abs(trackCount - candidateTrackCount);
        if (difference == 0)
        {
            return 20;
        }

        return difference <= 2 ? 10 : 0;
    }

    private static int FlagPenalty(IReadOnlyList<string> flags)
    {
        var penalty = 0;
        if (flags.Contains(DeluxeFlag) || flags.Contains(EditionFlag) || flags.Contains(MixFlag))
        {
            penalty += 10;
        }

        if (flags.Contains(LiveFlag) || flags.Contains(CompilationFlag))
        {
            penalty += 15;
        }

        return penalty;
    }

    /// <summary>
    /// Best track on the matched album for a local track title at its 1-based position. A title
    /// match is required; position only breaks ties (for example a reprise later on the album).
    /// </summary>
    public static ScoredMatch<CatalogTrack>? BestTrack(
        string title,
        int position,
        string albumName,
        IEnumerable<CatalogTrack> tracks)
    {
        var local = Normalize(title);
        if (local.Length == 0)
        {
            return null;
        }

        return tracks
            .Where(track => Normalize(track.Name) == local)
            .Select(track =>
            {
                var score = 60;
                if (track.TrackNumber == position)
                {
                    score += 30;
                }

                if (track.DiscNumber <= 1)
                {
                    score += 10;
                }

                var flags = Flags(track.Name, isCompilation: false, albumName);
                if (flags.Contains(LiveFlag))
                {
                    score -= 15;
                }

                return new ScoredMatch<CatalogTrack>(track, Math.Clamp(score, 0, 100), flags);
            })
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    // " - 2011 Remaster", " - Live At Wembley", " – Remastered 2011": streaming services append
    // version notes after a spaced dash; local titles do not.
    [GeneratedRegex(@"\s[-–—]\s.*$", RegexOptions.CultureInvariant)]
    private static partial Regex DashSuffix();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\blive\b", RegexOptions.CultureInvariant)]
    private static partial Regex LiveWord();

    [GeneratedRegex(@"(?:^|[\(\[\-–—:]\s*)live\b", RegexOptions.CultureInvariant)]
    private static partial Regex LiveVersion();

    [GeneratedRegex(@"\b(?:mix|remix)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MixWord();
}

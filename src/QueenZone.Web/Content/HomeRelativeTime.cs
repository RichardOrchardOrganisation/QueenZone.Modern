using System.Globalization;

namespace QueenZone.Web;

/// <summary>
/// Short relative time for homepage feeds ("just now", "20 min ago", "3 hr ago", "2 days ago",
/// then a date). Same rules as the mobile <c>relativeTimeFromNow</c> so web and app read alike.
/// Timestamps are treated as UTC, as the mobile client does.
/// </summary>
public static class HomeRelativeTime
{
    /// <summary>Activity this recent counts as "live" in forum feeds.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);

    public static string Format(DateTime value, DateTimeOffset now)
    {
        var then = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var minutes = (int)Math.Floor((now.UtcDateTime - then).TotalMinutes);
        if (minutes < 1)
        {
            return "just now";
        }

        if (minutes < 60)
        {
            return $"{minutes} min ago";
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return $"{hours} hr ago";
        }

        var days = hours / 24;
        if (days < 7)
        {
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        return then.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public static bool IsLive(DateTime value, DateTimeOffset now) =>
        now.UtcDateTime - DateTime.SpecifyKind(value, DateTimeKind.Utc) < LiveWindow;
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace QueenZone.Data;

/// <summary>
/// A date known only to the year, month or day, written as ISO 8601 reduced precision
/// (<c>1986</c>, <c>1986-07</c>, <c>1986-07-12</c>). Archive photos are often only datable to a year.
/// </summary>
public readonly partial record struct PartialDate(int Year, int? Month = null, int? Day = null)
{
    public const int MinYear = 1900;

    /// <summary>Database precision flags stored beside the <c>date</c> column.</summary>
    public const string PrecisionYear = "Y";

    public const string PrecisionMonth = "M";

    public const string PrecisionDay = "D";

    public string Precision
    {
        get
        {
            if (Day is not null)
            {
                return PrecisionDay;
            }

            return Month is not null ? PrecisionMonth : PrecisionYear;
        }
    }

    public static bool TryParse(string? text, out PartialDate value)
    {
        value = default;
        var match = text is null ? null : IsoPattern().Match(text.Trim());
        if (match is not { Success: true })
        {
            return false;
        }

        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        int? month = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : null;
        int? day = match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : null;
        if (!IsValid(year, month, day))
        {
            return false;
        }

        value = new PartialDate(year, month, day);
        return true;
    }

    /// <summary>Rebuilds a value from the stored <c>date</c> and precision flag; null when unusable.</summary>
    public static PartialDate? FromDatabase(DateTime? date, string? precision)
    {
        if (date is not { } stored)
        {
            return null;
        }

        return precision switch
        {
            PrecisionDay => new PartialDate(stored.Year, stored.Month, stored.Day),
            PrecisionMonth => new PartialDate(stored.Year, stored.Month),
            PrecisionYear => new PartialDate(stored.Year),
            _ => null,
        };
    }

    /// <summary>The value for the <c>date</c> column; unknown parts default to the first of the period.</summary>
    public DateTime ToDatabaseDate() => new(Year, Month ?? 1, Day ?? 1, 0, 0, 0, DateTimeKind.Unspecified);

    public string ToIso()
    {
        if (Day is not null)
        {
            return $"{Year:D4}-{Month:D2}-{Day:D2}";
        }

        return Month is not null ? $"{Year:D4}-{Month:D2}" : $"{Year:D4}";
    }

    public string ToDisplay()
    {
        if (Day is not null)
        {
            return new DateTime(Year, Month!.Value, Day.Value, 0, 0, 0, DateTimeKind.Unspecified)
                .ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        }

        if (Month is not null)
        {
            return new DateTime(Year, Month.Value, 1, 0, 0, 0, DateTimeKind.Unspecified)
                .ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        return Year.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsValid(int year, int? month, int? day)
    {
        if (year < MinYear || year > DateTime.UtcNow.Year)
        {
            return false;
        }

        if (month is null)
        {
            return day is null;
        }

        if (month is < 1 or > 12)
        {
            return false;
        }

        return day is null || (day >= 1 && day <= DateTime.DaysInMonth(year, month.Value));
    }

    [GeneratedRegex(@"^(?<y>\d{4})(?:-(?<m>\d{2})(?:-(?<d>\d{2}))?)?$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex IsoPattern();
}

/// <summary>How firmly a photograph date is known.</summary>
public enum PhotoDateAccuracy
{
    Unknown,
    Approximate,
    Exact,
}

/// <summary>
/// The dates the archive keeps apart: when the event happened, when the photograph was taken, and (not
/// stored here) when it was added to the archive, which is the legacy upload timestamp.
/// </summary>
public sealed record PhotoArchiveDates(PartialDate? EventDate, PartialDate? PhotoDate)
{
    public static PhotoArchiveDates Unknown { get; } = new(null, null);

    /// <summary>A photograph date is exact only when it is known to the day; otherwise approximate.</summary>
    public PhotoDateAccuracy PhotoDateAccuracy => PhotoDate switch
    {
        null => PhotoDateAccuracy.Unknown,
        { Day: not null } => PhotoDateAccuracy.Exact,
        _ => PhotoDateAccuracy.Approximate,
    };
}

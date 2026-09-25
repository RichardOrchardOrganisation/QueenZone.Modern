namespace QueenZone.Web;

public sealed class HelpRequestOptions
{
    public const string SectionName = "HelpRequests";

    public string NotificationAddress { get; set; } = "support@queenzone.org";

    // Applies to all contact submissions, including signed-in members.
    public int MaxAnonymousPerIpPerHour { get; set; } = 3;

    public int MaxPerMemberPerMinute { get; set; } = 20;

    public int MaxPerEmailPerDay { get; set; } = 2;

    public int MaxPerMemberPerDay { get; set; } = 5;

    public int MinimumDwellSeconds { get; set; } = 3;
}

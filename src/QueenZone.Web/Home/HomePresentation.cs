namespace QueenZone.Web;

public static class HomePresentation
{
    public static string? ForumRepliesToday(int? count)
    {
        if (count is not int replies)
        {
            return null;
        }

        var noun = replies == 1 ? "reply" : "replies";
        return $"{replies:N0} new forum {noun} today";
    }

    public static string SprintPlayersNote(int players)
    {
        if (players <= 0)
        {
            return string.Empty;
        }

        var suffix = players == 1 ? " has" : "s have";
        return $" {players:N0} member{suffix} played today.";
    }
}

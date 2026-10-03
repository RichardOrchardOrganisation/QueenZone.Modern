namespace QueenZone.Web;

public static class HomePresentation
{
    public static string? ForumRepliesToday(int? count) => count is int replies
        ? $"{replies:N0} new forum {(replies == 1 ? "reply" : "replies")} today"
        : null;

    public static string SprintPlayersNote(int players) => players > 0
        ? $" {players:N0} member{(players == 1 ? " has" : "s have")} played today."
        : string.Empty;
}

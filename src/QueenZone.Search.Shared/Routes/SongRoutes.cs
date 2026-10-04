namespace QueenZone.Routing;

public static class SongRoutes
{
    public static string GetIndexPath() => "/songs";

    public static string GetSongPath(string slug) => $"/songs/{slug}";
}

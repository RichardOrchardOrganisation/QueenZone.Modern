namespace QueenZone.Data;

/// <summary>
/// Mutable discography state shared by <see cref="InMemoryDiscographyRepository"/> and
/// <see cref="InMemoryAdminDiscographyRepository"/> so admin edits show up on public pages
/// in local development and tests.
/// </summary>
public sealed class InMemoryDiscographyStore
{
    public static readonly IReadOnlyList<AdminArtist> Artists = [new(1, "Queen")];

    private readonly object gate = new();
    private readonly List<AlbumState> albums;
    private int nextSongId;

    private static readonly DateTime SeedLinkTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public InMemoryDiscographyStore(IReadOnlyList<AlbumSeed> seedAlbums, IReadOnlyList<StreamingLinkSeed>? seedLinks = null)
    {
        albums = seedAlbums.Select(FromSeed).ToList();
        nextSongId = albums.SelectMany(album => album.Songs).Select(song => song.SongId).DefaultIfEmpty(0).Max() + 1;
        foreach (var seed in seedLinks ?? [])
        {
            AddSeedLink(seed);
        }
    }

    public IReadOnlyList<AlbumSummary> GetActiveAlbums()
    {
        lock (gate)
        {
            return albums
                .Where(album => album.IsActive)
                .OrderBy(album => album.ReleaseDate ?? DateTime.MaxValue)
                .ThenBy(album => album.AlbumId)
                .Select(album => new AlbumSummary(
                    AlbumId: album.AlbumId,
                    Name: album.Name,
                    Slug: NewsSlug.Slugify(album.Name),
                    ReleaseYear: album.ReleaseDate?.Year,
                    ThumbnailUrl: AlbumCoverUrl.Build(album.ThumbFileName)))
                .ToList();
        }
    }

    public AlbumDetail? GetActiveAlbum(int albumId)
    {
        lock (gate)
        {
            var album = Find(albumId);
            if (album is null || !album.IsActive)
            {
                return null;
            }

            return new AlbumDetail(
                AlbumId: album.AlbumId,
                Name: album.Name,
                Slug: NewsSlug.Slugify(album.Name),
                ReleaseYear: album.ReleaseDate?.Year,
                ArtistName: ArtistName(album.ArtistId),
                GeneralNotes: album.GeneralNotes,
                CoverUrl: AlbumCoverUrl.Build(album.PictureFileName) ?? AlbumCoverUrl.Build(album.ThumbFileName),
                Songs: album.Songs
                    .Select(song => new AlbumSong(
                        song.SongId,
                        song.Title,
                        song.IsSingle,
                        song.Lyrics,
                        song.Notes,
                        AlbumCoverUrl.Build(song.CoverFileName))
                    {
                        StreamingLinks = PublicLinks(song.Links),
                    })
                    .ToList(),
                ReleaseDate: album.ReleaseDate)
            {
                StreamingLinks = PublicLinks(album.Links),
            };
        }
    }

    public IReadOnlyList<AdminAlbumListItem> GetAdminAlbums()
    {
        lock (gate)
        {
            return albums
                .OrderBy(album => album.ReleaseDate is null ? 1 : 0)
                .ThenBy(album => album.ReleaseDate)
                .ThenBy(album => album.AlbumId)
                .Select(album => new AdminAlbumListItem(
                    album.AlbumId,
                    album.Name,
                    album.ReleaseDate,
                    album.IsActive,
                    album.ThumbFileName,
                    album.Songs.Count))
                .ToList();
        }
    }

    public AdminAlbum? GetAdminAlbum(int albumId)
    {
        lock (gate)
        {
            var album = Find(albumId);
            return album is null ? null : ToAdmin(album);
        }
    }

    public AdminAlbumSong? GetAdminSong(int songId)
    {
        lock (gate)
        {
            var album = FindAlbumForSong(songId);
            if (album is null)
            {
                return null;
            }

            var index = album.Songs.FindIndex(song => song.SongId == songId);
            return ToAdmin(album, album.Songs[index], index);
        }
    }

    public int CreateAlbum(AdminAlbumInput input)
    {
        lock (gate)
        {
            var albumId = albums.Select(album => album.AlbumId).DefaultIfEmpty(0).Max() + 1;
            if (albumId > AdminDiscographyValidation.MaxTinyIntId)
            {
                throw new InvalidOperationException("No album ids are left (the legacy album id is limited to 255).");
            }

            var album = new AlbumState { AlbumId = albumId };
            Apply(album, input);
            albums.Add(album);
            return albumId;
        }
    }

    public void UpdateAlbum(int albumId, AdminAlbumInput input)
    {
        lock (gate)
        {
            Apply(RequireAlbum(albumId), input);
        }
    }

    public void SetAlbumCover(int albumId, AdminAlbumCover? cover)
    {
        lock (gate)
        {
            var album = RequireAlbum(albumId);
            album.PictureFileName = cover?.PictureFileName;
            album.ThumbFileName = cover?.ThumbFileName;
        }
    }

    public void DeleteAlbum(int albumId)
    {
        lock (gate)
        {
            albums.Remove(RequireAlbum(albumId));
        }
    }

    public int CreateSong(int albumId, AdminSongInput input, int position)
    {
        lock (gate)
        {
            var album = RequireAlbum(albumId);
            var song = new SongState { SongId = nextSongId++ };
            Apply(song, input);
            var target = AdminDiscographyValidation.ClampPosition(position, album.Songs.Count);
            album.Songs.Insert(target - 1, song);
            return song.SongId;
        }
    }

    public void UpdateSong(int songId, AdminSongInput input)
    {
        lock (gate)
        {
            Apply(RequireSong(songId).Song, input);
        }
    }

    public void MoveSong(int songId, int position)
    {
        lock (gate)
        {
            var (album, _) = RequireSong(songId);
            var order = AdminDiscographyValidation.MoveTo(album.Songs.Select(s => s.SongId).ToList(), songId, position);
            var byId = album.Songs.ToDictionary(s => s.SongId);
            album.Songs.Clear();
            album.Songs.AddRange(order.Select(id => byId[id]));
        }
    }

    public void SetSongCover(int songId, string? coverFileName)
    {
        lock (gate)
        {
            RequireSong(songId).Song.CoverFileName = AdminDiscographyValidation.NullIfBlank(coverFileName);
        }
    }

    public void DeleteSong(int songId)
    {
        lock (gate)
        {
            var (album, song) = RequireSong(songId);
            album.Songs.Remove(song);
        }
    }

    public void SetAlbumStreamingLink(int albumId, StreamingProvider provider, StreamingLinkWrite? link)
    {
        link?.EnsureFits(provider, StreamingLinkKind.Album);
        lock (gate)
        {
            SetLink(RequireAlbum(albumId).Links, provider, link);
        }
    }

    public void SetSongStreamingLink(int songId, StreamingProvider provider, StreamingLinkWrite? link)
    {
        link?.EnsureFits(provider, StreamingLinkKind.Track);
        lock (gate)
        {
            SetLink(RequireSong(songId).Song.Links, provider, link);
        }
    }

    private static void SetLink(
        Dictionary<StreamingProvider, AdminStreamingLink> links,
        StreamingProvider provider,
        StreamingLinkWrite? link,
        DateTime? updatedAtUtc = null)
    {
        if (link is null)
        {
            links.Remove(provider);
            return;
        }

        links[provider] = new AdminStreamingLink(
            provider,
            link.Link.ExternalId,
            link.Link.Url,
            link.Source,
            updatedAtUtc ?? DateTime.UtcNow,
            link.TrimmedUpdatedBy());
    }

    private static IReadOnlyList<StreamingLink> PublicLinks(Dictionary<StreamingProvider, AdminStreamingLink> links) =>
        OrderedLinks(links).Select(link => link.ToPublic()).ToList();

    private static IReadOnlyList<AdminStreamingLink> OrderedLinks(Dictionary<StreamingProvider, AdminStreamingLink> links) =>
        StreamingProviders.All.Where(links.ContainsKey).Select(provider => links[provider]).ToList();

    private void AddSeedLink(StreamingLinkSeed seed)
    {
        var album = RequireAlbum(seed.AlbumId);
        var kind = seed.TrackNumber is null ? StreamingLinkKind.Album : StreamingLinkKind.Track;
        if (!StreamingLinkUrl.TryParse(seed.Url, kind, out var parsed, out var error))
        {
            throw new InvalidOperationException($"Invalid sample streaming link {seed.Url}: {error}");
        }

        var target = seed.TrackNumber is int track ? album.Songs[track - 1].Links : album.Links;
        SetLink(target, parsed.Provider, new StreamingLinkWrite(parsed, StreamingLinkSource.Manual, null), SeedLinkTimestamp);
    }

    private static AlbumState FromSeed(AlbumSeed seed)
    {
        var slug = NewsSlug.Slugify(seed.Name);
        var album = new AlbumState
        {
            AlbumId = seed.AlbumId,
            Name = seed.Name,
            ArtistId = 1,
            ReleaseDate = new DateTime(seed.ReleaseYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            GeneralNotes = seed.GeneralNotes,
            IsActive = true,
            ThumbFileName = $"{slug}-thumb.jpg",
            PictureFileName = $"{slug}-cover.jpg",
        };

        album.Songs.AddRange(seed.SongTitles.Select((title, index) => new SongState
        {
            SongId = (seed.AlbumId * 1000) + index + 1,
            Title = title,

            // The first track of each album carries sample lyrics containing a line that looks
            // like raw markup ("</li></ol>"), to guard against lyrics text breaking the
            // surrounding tracklist when rendered.
            Lyrics = index == 0 ? $"First line of {title}\nSecond line </li></ol> third line" : null,
        }));
        return album;
    }

    private static string ArtistName(int artistId) =>
        Artists.FirstOrDefault(artist => artist.ArtistId == artistId)?.Name ?? string.Empty;

    private static void Apply(AlbumState album, AdminAlbumInput input)
    {
        album.Name = input.Name.Trim();
        album.ArtistId = input.ArtistId;
        album.ReleaseDate = input.ReleaseDate?.Date;
        album.GeneralNotes = AdminDiscographyValidation.NullIfBlank(input.GeneralNotes);
        album.IsActive = input.IsActive;
    }

    private static void Apply(SongState song, AdminSongInput input)
    {
        song.Title = input.Title.Trim();
        song.Lyrics = AdminDiscographyValidation.NullIfBlank(input.Lyrics);
        song.Notes = AdminDiscographyValidation.NullIfBlank(input.Notes);
        song.IsSingle = input.IsSingle;
    }

    private static AdminAlbum ToAdmin(AlbumState album) =>
        new(
            album.AlbumId,
            album.Name,
            album.ArtistId,
            album.ReleaseDate,
            album.GeneralNotes,
            album.IsActive,
            album.ThumbFileName,
            album.PictureFileName,
            album.Songs.Select((song, index) => ToAdmin(album, song, index)).ToList())
        {
            StreamingLinks = OrderedLinks(album.Links),
        };

    private static AdminAlbumSong ToAdmin(AlbumState album, SongState song, int index) =>
        new(song.SongId, album.AlbumId, index + 1, song.Title, song.Lyrics, song.Notes, song.IsSingle, song.CoverFileName)
        {
            StreamingLinks = OrderedLinks(song.Links),
        };

    private AlbumState? Find(int albumId) => albums.FirstOrDefault(album => album.AlbumId == albumId);

    private AlbumState? FindAlbumForSong(int songId) =>
        albums.FirstOrDefault(album => album.Songs.Any(song => song.SongId == songId));

    private AlbumState RequireAlbum(int albumId) =>
        Find(albumId) ?? throw new InvalidOperationException($"Album {albumId} was not found.");

    private (AlbumState Album, SongState Song) RequireSong(int songId)
    {
        var album = FindAlbumForSong(songId)
            ?? throw new InvalidOperationException($"Song {songId} was not found.");
        return (album, album.Songs.First(song => song.SongId == songId));
    }

    private sealed class AlbumState
    {
        public int AlbumId { get; init; }

        public string Name { get; set; } = string.Empty;

        public int ArtistId { get; set; }

        public DateTime? ReleaseDate { get; set; }

        public string? GeneralNotes { get; set; }

        public bool IsActive { get; set; }

        public string? ThumbFileName { get; set; }

        public string? PictureFileName { get; set; }

        public List<SongState> Songs { get; } = [];

        public Dictionary<StreamingProvider, AdminStreamingLink> Links { get; } = [];
    }

    private sealed class SongState
    {
        public int SongId { get; init; }

        public string Title { get; set; } = string.Empty;

        public string? Lyrics { get; set; }

        public string? Notes { get; set; }

        public bool IsSingle { get; set; }

        public string? CoverFileName { get; set; }

        public Dictionary<StreamingProvider, AdminStreamingLink> Links { get; } = [];
    }
}

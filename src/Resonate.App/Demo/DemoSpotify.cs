using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Demo;

/// <summary>
/// Made-up music for "--demo": lets anyone try the interface (and lets CI take
/// screenshots) without a Spotify account. Nothing here comes from Spotify.
/// Playing a song only pretends to play it.
/// </summary>
public static class DemoCatalog
{
    private static readonly string[] Artists =
    [
        "Northern Lights", "The Paper Kites Club", "Mira Sol", "Velvet Static", "Juniper Lane",
        "Echo Harbor", "Neon Orchard", "Lumen", "Saltwater Radio", "The Quiet Hours",
    ];

    private static readonly string[] Words =
    [
        "Midnight", "Golden", "Glass", "River", "Satellite", "Paper", "Summer", "Static", "Velvet", "Ocean",
        "Echo", "Ember", "Silver", "Wild", "Neon", "Quiet", "Distant", "Electric", "Blue", "Hollow",
    ];

    private static readonly string[] Nouns =
    [
        "Hearts", "Lights", "Season", "Signals", "Afternoon", "Highway", "Dreams", "Skyline", "Waves", "Letters",
        "Gardens", "Machines", "Weather", "Rooms", "Horizons", "Echoes", "Fireflies", "Mirrors", "Tides", "Stars",
    ];

    public static readonly IReadOnlyList<(string Id, string Name, string Description, int Size)> Playlists =
    [
        ("late-night", "Late Night Drive", "Slow synths for empty roads.", 48),
        ("focus", "Deep Focus", "Instrumental music to keep you in the zone.", 64),
        ("sunday", "Sunday Morning", "Coffee, sunlight, nowhere to be.", 36),
        ("running", "Running Mix", "Steady tempo, no skipping needed.", 52),
        ("indie", "Indie Finds", "Bands you will tell your friends about.", 40),
        ("lofi", "Lo-fi Study", "Warm beats and soft noise.", 70),
        ("jazz", "Jazz Evenings", "Brushed drums and late conversations.", 30),
        ("throwback", "Throwback Hits", "The songs everyone knows the words to.", 55),
    ];

    public static PlayableItem Track(string playlistId, int index)
    {
        var stableSeed = StableHash(playlistId) + (index * 7919);
        var title = $"{Words[stableSeed % Words.Length]} {Nouns[(stableSeed / 7) % Nouns.Length]}";
        var artist = Artists[(stableSeed / 3) % Artists.Length];
        var album = $"{Nouns[(stableSeed / 11) % Nouns.Length]} of {Words[(stableSeed / 13) % Words.Length]}";
        return new PlayableItem
        {
            Id = $"{playlistId}-{index}",
            Name = title,
            Uri = $"demo:track:{playlistId}:{index}",
            Type = "track",
            DurationMs = 150_000 + (stableSeed % 120) * 1000,
            Artists = [new SimplifiedArtist { Name = artist, Id = ArtistId(artist), Uri = $"demo:artist:{ArtistId(artist)}" }],
            Album = new SimplifiedAlbum
            {
                Name = album,
                Id = AlbumId(album),
                Uri = $"demo:album:{AlbumId(album)}",
                Artists = [new SimplifiedArtist { Name = artist, Id = ArtistId(artist) }],
            },
            TrackNumber = (index % 12) + 1,
        };
    }

    public static IReadOnlyList<string> AllArtists => Artists;

    /// <summary>The songs of the demo listener's favourite artist (the most played on Home).</summary>
    public static IReadOnlyList<PlayableItem> FavouriteSongs()
    {
        var all = AllTracks().ToList();
        var favourite = all.Where(t => t.Artists![0].Name == "Mira Sol").ToList();
        return favourite.Count > 0 ? favourite : all;
    }

    public static string ArtistId(string name) => name.ToLowerInvariant().Replace(' ', '-');

    public static string AlbumId(string name) => name.ToLowerInvariant().Replace(' ', '-');

    /// <summary>Every demo song, once.</summary>
    public static IEnumerable<PlayableItem> AllTracks() =>
        Playlists.SelectMany(p => TracksOf(p.Id, p.Size)).DistinctBy(t => t.Name + t.Artists![0].Name);

    public static IEnumerable<PlayableItem> TracksOf(string playlistId, int count) =>
        Enumerable.Range(0, count).Select(i => Track(playlistId, i));

    public static PlayableItem? FindByUri(string? uri)
    {
        if (uri is null || !uri.StartsWith("demo:track:", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = uri.Split(':');
        return parts.Length == 4 && int.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? Track(parts[2], index)
            : null;
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked((hash * 31) + c);
        }

        return Math.Abs(hash % 100_000);
    }
}

/// <summary>A pretend Web API serving <see cref="DemoCatalog"/>.</summary>
public sealed class DemoWebApi : ISpotifyWebApi
{
    /// <summary>How many songs Liked Songs has (the speed test uses a heavy listener's library).</summary>
    public static int LikedCount { get; set; } = 240;

    private readonly DemoPlayer _player;

    // The pretend history ends when demo mode starts, so asking again finds nothing new.
    private readonly DateTimeOffset _historyEnd = DateTimeOffset.UtcNow;

    public DemoWebApi(DemoPlayer player) => _player = player;

    public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SpotifyUser { Id = "demo", DisplayName = "Demo listener", Uri = "demo:user:demo" });

    public Task<Page<SimplifiedPlaylist>> GetMyPlaylistsAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var all = DemoCatalog.Playlists.Select(p => new SimplifiedPlaylist
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Uri = $"demo:playlist:{p.Id}",
            Owner = new PlaylistOwner { Id = "demo", DisplayName = "Demo listener" },
            Items = new ItemsReference { Total = p.Size },
            SnapshotId = "demo-1",
        }).ToList();
        var page = all.Skip(offset).Take(limit).ToList<SimplifiedPlaylist?>();
        return Task.FromResult(new Page<SimplifiedPlaylist> { Items = page, Total = all.Count, Offset = offset, Limit = limit });
    }

    public Task<Page<SavedTrack>> GetSavedTracksAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var total = LikedCount;
        var items = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, total - offset)))
            .Select(i => new SavedTrack
            {
                Track = DemoCatalog.Track("liked", i),
                AddedAt = DateTimeOffset.UtcNow.AddDays(-i * 3).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList<SavedTrack?>();
        return Task.FromResult(new Page<SavedTrack>
        {
            Items = items,
            Total = total,
            Offset = offset,
            Limit = limit,
            Next = offset + items.Count < total ? "more" : null,
        });
    }

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var (id, name, description, size) = DemoCatalog.Playlists.FirstOrDefault(p => p.Id == playlistId);
        var first = DemoCatalog.TracksOf(playlistId, Math.Min(size, 50)).Select(t => new PlaylistEntry { Item = t }).ToList<PlaylistEntry?>();
        return Task.FromResult(new Playlist
        {
            Id = id ?? playlistId,
            Name = name ?? "Playlist",
            Description = description,
            Uri = $"demo:playlist:{playlistId}",
            Owner = new PlaylistOwner { Id = "demo", DisplayName = "Demo listener" },
            Items = new Page<PlaylistEntry> { Items = first, Total = size, Limit = 50, Next = size > 50 ? "more" : null },
        });
    }

    public Task<Page<PlaylistEntry>> GetPlaylistItemsAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken)
    {
        var size = DemoCatalog.Playlists.FirstOrDefault(p => p.Id == playlistId).Size;
        var items = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, size - offset)))
            .Select(i => new PlaylistEntry
            {
                Item = DemoCatalog.Track(playlistId, i),
                AddedAt = DateTimeOffset.UtcNow.AddDays(-i).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList<PlaylistEntry?>();
        return Task.FromResult(new Page<PlaylistEntry>
        {
            Items = items,
            Total = size,
            Offset = offset,
            Limit = limit,
            Next = offset + items.Count < size ? "more" : null,
        });
    }

    public Task<SearchResults> SearchAsync(string query, SearchTypes types, int offset, int limit, CancellationToken cancellationToken)
    {
        var tracks = DemoCatalog.Playlists
            .SelectMany(p => DemoCatalog.TracksOf(p.Id, p.Size))
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || t.Artists![0].Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(t => t.Name)
            .Take(limit)
            .ToList<PlayableItem?>();
        var playlists = DemoCatalog.Playlists
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || tracks.Count > 0)
            .Take(limit)
            .Select(p => new SimplifiedPlaylist
            {
                Id = p.Id,
                Name = p.Name,
                Uri = $"demo:playlist:{p.Id}",
                Owner = new PlaylistOwner { Id = "demo", DisplayName = "Demo listener" },
            })
            .ToList<SimplifiedPlaylist?>();
        var albums = tracks
            .Select(t => t!.Album!)
            .DistinctBy(a => a.Name)
            .Take(limit)
            .Select(a => new SimplifiedAlbum { Name = a.Name, Uri = a.Uri, Artists = [new SimplifiedArtist { Name = "Various artists" }] })
            .ToList<SimplifiedAlbum?>();

        return Task.FromResult(new SearchResults
        {
            Tracks = new Page<PlayableItem> { Items = tracks, Total = tracks.Count },
            Playlists = new Page<SimplifiedPlaylist> { Items = playlists, Total = playlists.Count },
            Albums = new Page<SimplifiedAlbum> { Items = albums, Total = albums.Count },
            Artists = new Page<Artist>(),
        });
    }

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Device>>([new Device { Id = "demo-device", Name = Environment.MachineName, Type = "Computer", IsActive = true }]);

    public Task<PlaybackState?> GetPlaybackStateAsync(CancellationToken cancellationToken) => Task.FromResult<PlaybackState?>(null);

    public Task StartPlaybackAsync(StartPlaybackBody? body, string? deviceId, CancellationToken cancellationToken)
    {
        var uri = body?.Offset?.Uri ?? body?.Uris?.FirstOrDefault();
        if (DemoCatalog.FindByUri(uri) is { } track)
        {
            _player.Start(track);
        }
        else if (body?.ContextUri?.StartsWith("demo:playlist:", StringComparison.Ordinal) == true)
        {
            _player.Start(DemoCatalog.Track(body.ContextUri["demo:playlist:".Length..], 0));
        }
        else
        {
            _player.SetPlaying(true);
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(string? deviceId, CancellationToken cancellationToken)
    {
        _player.SetPlaying(false);
        return Task.CompletedTask;
    }

    public Task SkipToNextAsync(string? deviceId, CancellationToken cancellationToken) => _player.NextAsync(cancellationToken);

    public Task SkipToPreviousAsync(string? deviceId, CancellationToken cancellationToken) => _player.PreviousAsync(cancellationToken);

    public Task SeekAsync(TimeSpan position, string? deviceId, CancellationToken cancellationToken) => _player.SeekAsync(position, cancellationToken);

    public Task SetVolumeAsync(int percent, string? deviceId, CancellationToken cancellationToken)
    {
        _player.TrySetVolume(percent / 100.0);
        return Task.CompletedTask;
    }

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetShuffleAsync(bool shuffle, string? deviceId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetRepeatAsync(RepeatMode mode, string? deviceId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddToQueueAsync(string uri, string? deviceId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<PlayerQueue> GetQueueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PlayerQueue
        {
            CurrentlyPlaying = DemoCatalog.Track("late-night", 3),
            Queue = Enumerable.Range(4, 10).Select(i => DemoCatalog.Track("late-night", i)).ToList<PlayableItem?>(),
        });

    /// <summary>
    /// A believable week of listening, newest first: a busy last day (a
    /// song every 40 minutes), then a few songs a day. One artist and one
    /// song keep coming back, so the stats on Home have a clear favourite.
    /// </summary>
    public Task<CursorPage<PlayHistoryItem>> GetRecentlyPlayedAsync(int limit, DateTimeOffset? after, CancellationToken cancellationToken)
    {
        var all = DemoCatalog.AllTracks().ToList();
        var favourite = DemoCatalog.FavouriteSongs();
        var items = Enumerable.Range(0, 50)
            .Select(i => new PlayHistoryItem
            {
                Track = (i % 4) switch
                {
                    0 => favourite[0],
                    2 => favourite[(i / 4) % favourite.Count],
                    _ => all[(i * 7) % all.Count],
                },
                PlayedAt = (i < 24 ? _historyEnd.AddMinutes(-(i * 40) - 3) : _historyEnd.AddHours(-17 - ((i - 24) * 5.5)))
                    .ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                Context = new PlaybackContext { Type = "playlist", Uri = $"demo:playlist:{DemoCatalog.Playlists[i % 3].Id}" },
            })
            .Where(p => after is null || DateTimeOffset.Parse(p.PlayedAt!, System.Globalization.CultureInfo.InvariantCulture) > after)
            .Take(limit)
            .ToList<PlayHistoryItem?>();
        return Task.FromResult(new CursorPage<PlayHistoryItem> { Items = items, Limit = limit });
    }

    public Task<Page<Artist>> GetTopArtistsAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken)
    {
        // The favourite first over four weeks, as on the stats; longer ranges lead with others.
        var ranked = DemoCatalog.AllArtists.OrderBy(name => name == "Mira Sol" ? 0 : 1).ToList();
        var lead = (int)range * 2;
        var page = ranked.Skip(lead).Concat(ranked.Take(lead))
            .Select(name => new Artist { Id = DemoCatalog.ArtistId(name), Name = name, Uri = $"demo:artist:{DemoCatalog.ArtistId(name)}" })
            .Skip(offset)
            .Take(limit)
            .ToList<Artist?>();
        return Task.FromResult(new Page<Artist> { Items = page, Total = DemoCatalog.AllArtists.Count, Offset = offset, Limit = limit });
    }

    public Task<Page<PlayableItem>> GetTopTracksAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken)
    {
        var stride = 5 + (int)range;
        var tracks = DemoCatalog.FavouriteSongs().Take(range == TopRange.ShortTerm ? 1 : 0)
            .Concat(DemoCatalog.AllTracks().Where((_, i) => i % stride == (int)range))
            .DistinctBy(t => t.Uri)
            .Skip(offset)
            .Take(limit)
            .ToList<PlayableItem?>();
        return Task.FromResult(new Page<PlayableItem> { Items = tracks, Total = tracks.Count, Offset = offset, Limit = limit });
    }

    public Task<IReadOnlyList<bool>> CheckLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<bool>>(uris.Select(u => u.Contains(":liked:", StringComparison.Ordinal) || u.EndsWith('3')).ToList());

    public Task SaveToLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveFromLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string?> ReorderPlaylistItemsAsync(string playlistId, int rangeStart, int insertBefore, int rangeLength, string? snapshotId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("demo-2");

    public Task<string?> AddPlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, int? position, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("demo-2");

    public Task<string?> RemovePlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, string? snapshotId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("demo-2");

    public Task<SimplifiedPlaylist> CreatePlaylistAsync(string name, string? description, bool isPublic, CancellationToken cancellationToken) =>
        Task.FromResult(new SimplifiedPlaylist
        {
            Id = "demo-new",
            Name = name,
            Uri = "demo:playlist:demo-new",
            Owner = new PlaylistOwner { Id = "demo", DisplayName = "Demo listener" },
        });

    public Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var tracks = DemoCatalog.AllTracks().Where(t => t.Album?.Id == albumId).ToList();
        if (tracks.Count == 0)
        {
            tracks = DemoCatalog.TracksOf("focus", 9).ToList();
        }

        var first = tracks[0].Album!;
        return Task.FromResult(new Album
        {
            Id = albumId,
            Name = first.Name,
            Uri = $"demo:album:{albumId}",
            AlbumType = "album",
            ReleaseDate = "2025-05-16",
            TotalTracks = tracks.Count,
            Artists = first.Artists,
            Tracks = new Page<PlayableItem> { Items = tracks.Select(t => (PlayableItem?)t).ToList(), Total = tracks.Count },
        });
    }

    public Task<Page<PlayableItem>> GetAlbumTracksAsync(string albumId, int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(new Page<PlayableItem>());

    public Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        var name = DemoCatalog.AllArtists.FirstOrDefault(a => DemoCatalog.ArtistId(a) == artistId) ?? "Demo artist";
        return Task.FromResult(new Artist { Id = artistId, Name = name, Uri = $"demo:artist:{artistId}", Genres = ["indie", "dream pop"] });
    }

    public Task<Page<SimplifiedAlbum>> GetArtistAlbumsAsync(string artistId, int offset, int limit, CancellationToken cancellationToken)
    {
        // As Spotify does since February 2026, so a caller asking for more shows up in demo runs too.
        if (limit > SpotifyWebApi.MaxArtistAlbumsLimit)
        {
            return Task.FromException<Page<SimplifiedAlbum>>(new SpotifyApiException(System.Net.HttpStatusCode.BadRequest, null, "Invalid limit"));
        }

        var all = DemoCatalog.AllTracks()
            .Where(t => t.Artists?.Any(a => a.Id == artistId) == true)
            .Select(t => t.Album!)
            .DistinctBy(a => a.Id)
            .ToList();
        var albums = all
            .Skip(offset)
            .Take(limit)
            .Select(a => (SimplifiedAlbum?)new SimplifiedAlbum { Id = a.Id, Name = a.Name, Uri = a.Uri, Artists = a.Artists, AlbumType = "album", ReleaseDate = "2024" })
            .ToList();
        var next = offset + albums.Count < all.Count ? "demo:next" : null;
        return Task.FromResult(new Page<SimplifiedAlbum> { Items = albums, Total = all.Count, Offset = offset, Limit = limit, Next = next });
    }
}

/// <summary>A pretend Spotify app: keeps a now-playing state and reports it like the media session would.</summary>
public sealed class DemoPlayer : ILocalMediaChannel, IAppVolume, ISpotifyAppLauncher, ISpotifyAppWindow
{
    private readonly Lock _gate = new();
    private LocalMediaSnapshot _current;
    private string _playlistId = "late-night";
    private int _index;
    private double _volume = 0.7;

    public DemoPlayer()
    {
        var first = DemoCatalog.Track(_playlistId, 3);
        _index = 3;
        _current = Snapshot(first, playing: true, TimeSpan.FromSeconds(42));
    }

    public event EventHandler<LocalMediaSnapshot>? Changed;

    public LocalMediaSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsRunning => true;

    public bool Enabled { get; set; } = true;

    public bool KeepHidden { get; set; } = true;

    public bool SaveResources { get; set; } = true;

    public bool ShowSpotify() => true;

    public Task<SpotifyAppStatus> EnsureRunningAsync(CancellationToken cancellationToken) => Task.FromResult(SpotifyAppStatus.Running);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Stop()
    {
    }

    public void Start(PlayableItem track)
    {
        var parts = track.Uri!.Split(':');
        _playlistId = parts[2];
        _index = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
        Report(Snapshot(track, playing: true, TimeSpan.Zero));
    }

    public void SetPlaying(bool playing)
    {
        var now = Current;
        Report(now with { IsPlaying = playing, Position = now.PositionAt(DateTimeOffset.UtcNow), PositionUpdatedAt = DateTimeOffset.UtcNow });
    }

    public Task<bool> PlayAsync(CancellationToken cancellationToken)
    {
        SetPlaying(true);
        return Task.FromResult(true);
    }

    public Task<bool> PauseAsync(CancellationToken cancellationToken)
    {
        SetPlaying(false);
        return Task.FromResult(true);
    }

    public Task<bool> NextAsync(CancellationToken cancellationToken)
    {
        _index++;
        Report(Snapshot(DemoCatalog.Track(_playlistId, _index), playing: true, TimeSpan.Zero));
        return Task.FromResult(true);
    }

    public Task<bool> PreviousAsync(CancellationToken cancellationToken)
    {
        var now = Current;
        if (now.PositionAt(DateTimeOffset.UtcNow) < TimeSpan.FromSeconds(3) && _index > 0)
        {
            _index--;
        }

        Report(Snapshot(DemoCatalog.Track(_playlistId, _index), playing: true, TimeSpan.Zero));
        return Task.FromResult(true);
    }

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        Report(Current with { Position = position, PositionUpdatedAt = DateTimeOffset.UtcNow });
        return Task.FromResult(true);
    }

    public double? TryGetVolume() => _volume;

    public bool TrySetVolume(double volume)
    {
        _volume = volume;
        return true;
    }

    public void Dispose()
    {
    }

    private static LocalMediaSnapshot Snapshot(PlayableItem track, bool playing, TimeSpan position) => new()
    {
        HasSession = true,
        Title = track.Name,
        Artist = track.Artists?.FirstOrDefault()?.Name,
        Album = track.Album?.Name,
        IsPlaying = playing,
        Position = position,
        PositionUpdatedAt = DateTimeOffset.UtcNow,
        Duration = TimeSpan.FromMilliseconds(track.DurationMs),
        CanSeek = true,
        CanSkipNext = true,
        CanSkipPrevious = true,
    };

    private void Report(LocalMediaSnapshot snapshot)
    {
        lock (_gate)
        {
            _current = snapshot;
        }

        // Like the real media session, the report arrives a moment later.
        _ = Task.Delay(120).ContinueWith(_ => Changed?.Invoke(this, snapshot), TaskScheduler.Default);
    }
}

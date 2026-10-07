using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.History;

/// <summary>What Home shows besides the stats, made once a day and kept on disk.</summary>
public sealed class HomeContent
{
    /// <summary>The (local) day the mixes were made for; they are made again when it changes.</summary>
    public DateOnly Day { get; set; }

    public DateTimeOffset MadeAt { get; set; }

    public List<DailyMix> Mixes { get; set; } = [];

    /// <summary>The songs played most over the last four weeks (Spotify's short-term top tracks).</summary>
    public List<TrackInfo> OnRepeat { get; set; } = [];

    /// <summary>The user's top artists and songs as Spotify works them out, for each time range it knows (none without permission).</summary>
    public List<TopOnSpotify> Top { get; set; } = [];

    /// <summary>Artist pictures by artist ID ("" when the artist has none), so they are asked for once.</summary>
    public Dictionary<string, string> ArtistImages { get; set; } = [];

    /// <summary>The top lists for <paramref name="range"/>, or null when Spotify did not give them.</summary>
    public TopOnSpotify? TopFor(TopRange range) => Top.FirstOrDefault(t => t.Range == range);
}

/// <summary>The artists and songs a user played most over one of Spotify's time ranges, most played first.</summary>
public sealed class TopOnSpotify
{
    public TopRange Range { get; set; }

    public List<TopArtist> Artists { get; set; } = [];

    public List<TrackInfo> Songs { get; set; } = [];
}

/// <summary>One of the user's top artists, with a picture link when Spotify has one.</summary>
public sealed record TopArtist(string Id, string Name, string? ImageUrl);

/// <summary>
/// Home's data: the listening history, and the daily mixes, "On repeat" and
/// Spotify's own top lists, asked for once a day. What is stored shows at
/// once; <see cref="RefreshAsync"/> brings it up to date in the background.
/// Holds names and picture links only, never tokens.
/// </summary>
public sealed class HomeFeed : IDisposable
{
    public const int OnRepeatSize = 30;

    /// <summary>How many artists and songs Home shows for each time range.</summary>
    public const int TopSize = 10;

    private const int TopArtistsLimit = 20;

    private static readonly TopRange[] TopRanges = [TopRange.ShortTerm, TopRange.MediumTerm, TopRange.LongTerm];

    /// <summary>The user's own playlists not yet stored that one round of making mixes may load (each once per version).</summary>
    private const int MaxPlaylistsToLoad = 10;

    private const int MaxPlaylistSize = 500;

    /// <summary>With no mixes or top lists yet (a new account, or Spotify did not answer), try again after this long instead of waiting for tomorrow.</summary>
    private static readonly TimeSpan RetryEmptyAfter = TimeSpan.FromMinutes(10);

    private readonly ISpotifyWebApi _api;
    private readonly LibraryService _library;
    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _makeLock = new(1, 1);
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, string> _artistImages = new(StringComparer.Ordinal);
    private HomeContent? _content;
    private volatile bool _loaded;
    private int _generation;
    private DateTimeOffset _topTriedAt = DateTimeOffset.MinValue;

    /// <param name="path">The file to keep the day's mixes in; null keeps them in memory only (demo mode, tests).</param>
    public HomeFeed(ISpotifyWebApi api, LibraryService library, ListeningHistory history, string? path, TimeProvider? time = null)
    {
        _api = api;
        _library = library;
        History = history;
        _path = path;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on any thread when new mixes were made (or everything was forgotten).</summary>
    public event EventHandler? Changed;

    public ListeningHistory History { get; }

    /// <summary>The latest mixes and top lists, possibly from an earlier day; null before any were made or loaded.</summary>
    public HomeContent? Content => Volatile.Read(ref _content);

    /// <summary>Read without the lock, which is held while files are read and written: Home asks from the interface thread.</summary>
    public bool IsLoaded => _loaded && History.IsLoaded;

    /// <summary>Today, in the user's time zone: mixes change at local midnight.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>Reads the stored history and mixes once. Call it off the interface thread.</summary>
    public void LoadStored()
    {
        History.Load();
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            if (Read() is { } stored)
            {
                foreach (var (id, url) in stored.ArtistImages)
                {
                    _artistImages.TryAdd(id, url);
                }

                Volatile.Write(ref _content, stored);
            }

            _loaded = true;
        }
    }

    /// <summary>
    /// Saves the songs played since last time, then makes the day's mixes if
    /// they are not made yet. Each part is tried even when the other fails;
    /// the first failure is then thrown.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        LoadStored();
        ExceptionDispatchInfo? failure = null;
        try
        {
            await History.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ExceptionDispatchInfo.Capture(ex);
        }

        try
        {
            await MakeMixesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure ??= ExceptionDispatchInfo.Capture(ex);
        }

        failure?.Throw();
    }

    /// <summary>The mixes to show: the stored ones, or newly made when there are none yet (a mix page opened first).</summary>
    public async Task<HomeContent> GetContentAsync(CancellationToken cancellationToken)
    {
        LoadStored();
        if (Content is { } content)
        {
            return content;
        }

        await MakeMixesAsync(cancellationToken).ConfigureAwait(false);
        return Content ?? new HomeContent();
    }

    /// <summary>An artist's picture if it is already known; null otherwise (see <see cref="GetArtistImageAsync"/>).</summary>
    public string? KnownArtistImage(string artistId) =>
        _artistImages.TryGetValue(artistId, out var url) && url.Length > 0 ? url : null;

    /// <summary>An artist's picture, asked of Spotify the first time and remembered.</summary>
    public async Task<string?> GetArtistImageAsync(string artistId, CancellationToken cancellationToken)
    {
        if (_artistImages.TryGetValue(artistId, out var known))
        {
            return known.Length > 0 ? known : null;
        }

        var url = await FetchArtistImageAsync(artistId, cancellationToken).ConfigureAwait(false);
        if (Content is { } content)
        {
            SaveWithImages(content);
        }

        return url;
    }

    /// <summary>Forgets the history and mixes, in memory and on disk (for example on signing out).</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _generation++;
            _loaded = true;
            _artistImages.Clear();
            Volatile.Write(ref _content, null);
            Delete();
        }

        History.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _makeLock.Dispose();
        History.Dispose();
    }

    /// <summary>Whether the stored mixes are out of date.</summary>
    internal bool NeedsMixes()
    {
        if (Content is not { } content || content.Day != Today)
        {
            return true;
        }

        return content.Mixes.Count == 0 && _time.GetUtcNow() - content.MadeAt > RetryEmptyAfter;
    }

    /// <summary>
    /// Whether today's mixes lack Spotify's top lists (made before Resonate
    /// showed them, or Spotify did not answer): asked for again, at most
    /// every few minutes.
    /// </summary>
    internal bool NeedsTop() =>
        Content is { } content
        && content.Top.Count < TopRanges.Length
        && _time.GetUtcNow() - _topTriedAt > RetryEmptyAfter;

    private async Task MakeMixesAsync(CancellationToken cancellationToken)
    {
        await _makeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var needsMixes = NeedsMixes();
            if (!needsMixes && !NeedsTop())
            {
                // Made meanwhile (Home and a mix page asked at once).
                return;
            }

            int generation;
            lock (_gate)
            {
                generation = _generation;
            }

            var today = Today;
            _topTriedAt = _time.GetUtcNow();
            var top = await FetchTopAsync(cancellationToken).ConfigureAwait(false);
            HomeContent content;
            if (needsMixes)
            {
                var liked = await _library.GetAllLikedSongsAsync(cancellationToken).ConfigureAwait(false);
                if (_library.Snapshot is null)
                {
                    // The very first start: the playlists are not known yet, and today's mixes should learn from them.
                    await _library.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }

                var playlists = await OwnPlaylistSongsAsync(cancellationToken).ConfigureAwait(false);
                var mixes = DailyMixBuilder.Build(top.Seeds, liked, playlists, History.Plays, today);
                await AddMissingImagesAsync(mixes, cancellationToken).ConfigureAwait(false);
                content = new HomeContent
                {
                    Day = today,
                    MadeAt = _time.GetUtcNow(),
                    Mixes = mixes,
                    OnRepeat = top.OnRepeat,
                    Top = top.Lists,
                    ArtistImages = new Dictionary<string, string>(_artistImages, StringComparer.Ordinal),
                };
            }
            else
            {
                var made = Content!;
                if (top.Lists.Count <= made.Top.Count)
                {
                    // Nothing new from Spotify.
                    return;
                }

                // Today's mixes (and "On repeat", when it has songs) stay; only the top lists are new.
                content = new HomeContent
                {
                    Day = made.Day,
                    MadeAt = made.MadeAt,
                    Mixes = made.Mixes,
                    OnRepeat = made.OnRepeat.Count > 0 ? made.OnRepeat : top.OnRepeat,
                    Top = top.Lists,
                    ArtistImages = new Dictionary<string, string>(_artistImages, StringComparer.Ordinal),
                };
            }

            lock (_gate)
            {
                if (generation != _generation)
                {
                    // Signed out meanwhile: these mixes belong to the old account.
                    return;
                }

                Volatile.Write(ref _content, content);
                Save(content);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _makeLock.Release();
        }
    }

    /// <summary>
    /// Spotify's top artists and songs for each time range, and from them
    /// the mix seeds (the top artists over about four weeks, or six months
    /// when that says too little) and "On repeat". Stops at the first
    /// refusal (a sign-in without the permission), keeping what came before.
    /// </summary>
    private async Task<TopFetch> FetchTopAsync(CancellationToken cancellationToken)
    {
        var lists = new List<TopOnSpotify>();
        var seeds = new List<MixSeed>();
        var onRepeat = new List<TrackInfo>();
        try
        {
            foreach (var range in TopRanges)
            {
                var artistPage = await _api.GetTopArtistsAsync(range, 0, TopArtistsLimit, cancellationToken).ConfigureAwait(false);
                var trackPage = await _api.GetTopTracksAsync(range, 0, OnRepeatSize, cancellationToken).ConfigureAwait(false);
                var artists = new List<TopArtist>();
                foreach (var artist in artistPage.Items.OfType<Artist>())
                {
                    var image = ImagePicker.Pick(artist.Images, 300);
                    _artistImages[artist.Id] = image ?? string.Empty;
                    artists.Add(new TopArtist(artist.Id, artist.Name, image));
                }

                var songs = trackPage.Items
                    .Select((PlayableItem? t, int i) => TrackInfo.From(t, position: i))
                    .OfType<TrackInfo>()
                    .Where(t => t.IsPlayable)
                    .ToList();
                if (range == TopRange.ShortTerm)
                {
                    onRepeat = songs;
                }

                if (range != TopRange.LongTerm && seeds.Count < TopArtistsLimit / 2)
                {
                    seeds.AddRange(artists.Select(a => new MixSeed(a.Id, a.Name, a.ImageUrl)));
                }

                lists.Add(new TopOnSpotify { Range = range, Artists = artists.Take(TopSize).ToList(), Songs = songs.Take(TopSize).ToList() });
            }
        }
        catch (SpotifyApiException)
        {
            // Not allowed (an older sign-in) or not now: the history and Liked Songs still give seeds.
        }

        return new TopFetch(lists, seeds.DistinctBy(s => s.Id).ToList(), onRepeat);
    }

    /// <summary>
    /// The songs of the user's own playlists: the stored copies (no
    /// requests), plus a few not stored yet, which are stored for next time.
    /// </summary>
    private async Task<List<IReadOnlyList<TrackInfo>>> OwnPlaylistSongsAsync(CancellationToken cancellationToken)
    {
        var lists = new List<IReadOnlyList<TrackInfo>>();
        var loads = 0;
        foreach (var playlist in _library.Snapshot?.Playlists.ToList() ?? [])
        {
            if (!_library.CanListSongs(playlist))
            {
                continue;
            }

            if (_library.GetStoredPlaylistTracks(playlist.Id) is { } stored)
            {
                lists.Add(stored);
                continue;
            }

            if (loads >= MaxPlaylistsToLoad || playlist.ItemCount is 0 or > MaxPlaylistSize)
            {
                continue;
            }

            loads++;
            try
            {
                var list = await _library.GetAllPlaylistTracksAsync(playlist.Id, playlist.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (!list.ItemsHidden)
                {
                    lists.Add(list.Tracks);
                }
            }
            catch (SpotifyApiException)
            {
                // One playlist less to learn from.
            }
        }

        return lists;
    }

    /// <summary>Seeds found in the history or Liked Songs come without a picture; ask for it (at most one request per mix).</summary>
    private async Task AddMissingImagesAsync(List<DailyMix> mixes, CancellationToken cancellationToken)
    {
        foreach (var mix in mixes)
        {
            if (mix.ImageUrl is not null)
            {
                continue;
            }

            if (_artistImages.TryGetValue(mix.SeedId, out var known))
            {
                mix.ImageUrl = known.Length > 0 ? known : null;
                continue;
            }

            try
            {
                mix.ImageUrl = await FetchArtistImageAsync(mix.SeedId, cancellationToken).ConfigureAwait(false);
            }
            catch (SpotifyApiException)
            {
                // The card shows its colours instead.
            }
        }
    }

    private async Task<string?> FetchArtistImageAsync(string artistId, CancellationToken cancellationToken)
    {
        var artist = await _api.GetArtistAsync(artistId, cancellationToken).ConfigureAwait(false);
        var url = ImagePicker.Pick(artist.Images, 300);
        _artistImages[artistId] = url ?? string.Empty;
        return url;
    }

    private void SaveWithImages(HomeContent content)
    {
        lock (_gate)
        {
            if (Content != content)
            {
                return;
            }

            Save(new HomeContent
            {
                Day = content.Day,
                MadeAt = content.MadeAt,
                Mixes = content.Mixes,
                OnRepeat = content.OnRepeat,
                Top = content.Top,
                ArtistImages = new Dictionary<string, string>(_artistImages, StringComparer.Ordinal),
            });
        }
    }

    private HomeContent? Read()
    {
        if (_path is null)
        {
            return null;
        }

        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.HomeContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Save(HomeContent content)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, content, SpotifyJsonContext.Default.HomeContent);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Made again next time.
        }
    }

    private void Delete()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private sealed record TopFetch(List<TopOnSpotify> Lists, List<MixSeed> Seeds, List<TrackInfo> OnRepeat);
}

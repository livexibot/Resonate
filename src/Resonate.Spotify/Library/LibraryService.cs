using System.Net;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>One page of a song list.</summary>
/// <param name="ItemsHidden">
/// Spotify did not share the songs. Since February 2026 it only lists the
/// songs of playlists the user owns or collaborates on; others can still be
/// played as a whole.
/// </param>
/// <param name="NextOffset">Where the next page starts (entries Resonate skips, such as removed songs, still count).</param>
public sealed record TrackListPage(IReadOnlyList<TrackInfo> Tracks, int Total, bool HasMore, bool ItemsHidden, int NextOffset)
{
    public static readonly TrackListPage Hidden = new([], 0, false, true, 0);
}

/// <summary>A playlist's header and its first page of songs.</summary>
public sealed record PlaylistDetails(
    string Id,
    string Uri,
    string Name,
    string? Description,
    string? Owner,
    string? ImageUrl,
    TrackListPage FirstPage);

/// <summary>Search results ready to show.</summary>
public sealed record SearchMatches(
    IReadOnlyList<TrackInfo> Tracks,
    IReadOnlyList<SimplifiedAlbum> Albums,
    IReadOnlyList<Artist> Artists,
    IReadOnlyList<SimplifiedPlaylist> Playlists);

/// <summary>
/// Reads the library (playlists, Liked Songs, playlist songs, search)
/// through the Web API and keeps the sidebar's data on disk for a fast start.
/// </summary>
public sealed class LibraryService : IDisposable
{
    public const int PageSize = SpotifyWebApi.MaxPageLimit;

    public const string LikedSongsKey = "liked";

    /// <summary>Whole lists are loaded this many pages at a time.</summary>
    private const int ParallelPages = 4;

    private readonly ISpotifyWebApi _api;
    private readonly LibraryCache? _cache;
    private readonly TrackListStore _lists;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _likedLock = new(1, 1);

    public LibraryService(ISpotifyWebApi api, LibraryCache? cache, TimeProvider? time = null, TrackListStore? lists = null)
    {
        _api = api;
        _cache = cache;
        _lists = lists ?? new TrackListStore(null);
        _time = time ?? TimeProvider.System;
        Snapshot = cache?.Load();
    }

    /// <summary>Raised on any thread when Liked Songs changed (a song was liked or unliked through Resonate).</summary>
    public event EventHandler? LikedSongsChanged;

    /// <summary>The newest known library; from the disk cache until <see cref="RefreshAsync"/> finishes.</summary>
    public LibrarySnapshot? Snapshot { get; private set; }

    public async Task<LibrarySnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        var userTask = _api.GetCurrentUserAsync(cancellationToken);
        var playlists = new List<SimplifiedPlaylist>();
        var offset = 0;
        while (true)
        {
            var page = await _api.GetMyPlaylistsAsync(offset, PageSize, cancellationToken).ConfigureAwait(false);
            playlists.AddRange(page.Items.OfType<SimplifiedPlaylist>());
            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            offset += page.Items.Count;
        }

        var snapshot = new LibrarySnapshot
        {
            User = await userTask.ConfigureAwait(false),
            Playlists = playlists,
            SavedAt = _time.GetUtcNow(),
        };
        Snapshot = snapshot;
        _cache?.Save(snapshot);
        return snapshot;
    }

    public async Task<TrackListPage> GetLikedSongsAsync(int offset, CancellationToken cancellationToken)
    {
        var page = await _api.GetSavedTracksAsync(offset, PageSize, cancellationToken).ConfigureAwait(false);
        var tracks = page.Items
            .Select((SavedTrack? s, int i) => TrackInfo.From(s?.Track, TrackInfo.ParseTime(s?.AddedAt), offset + i))
            .OfType<TrackInfo>()
            .ToList();
        return new TrackListPage(tracks, page.Total, page.HasMore, ItemsHidden: false, offset + page.Items.Count);
    }

    public async Task<PlaylistDetails> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var playlist = await _api.GetPlaylistAsync(playlistId, cancellationToken).ConfigureAwait(false);
        var entries = playlist.Entries;
        var firstPage = entries is null ? TrackListPage.Hidden : ToPage(entries, 0);

        return new PlaylistDetails(
            playlist.Id,
            playlist.Uri,
            playlist.Name,
            string.IsNullOrWhiteSpace(playlist.Description) ? null : WebUtility.HtmlDecode(playlist.Description),
            playlist.Owner?.DisplayName ?? playlist.Owner?.Id,
            ImagePicker.Pick(playlist.Images, 300),
            firstPage);
    }

    public async Task<TrackListPage> GetPlaylistTracksAsync(string playlistId, int offset, CancellationToken cancellationToken)
    {
        try
        {
            var page = await _api.GetPlaylistItemsAsync(playlistId, offset, PageSize, cancellationToken).ConfigureAwait(false);
            return ToPage(page, offset);
        }
        catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return TrackListPage.Hidden;
        }
    }

    public async Task<SearchMatches> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var results = await _api
            .SearchAsync(query, SearchTypes.All, 0, SpotifyWebApi.MaxSearchLimit, cancellationToken)
            .ConfigureAwait(false);

        return new SearchMatches(
            results.Tracks?.Items.Select(t => TrackInfo.From(t)).OfType<TrackInfo>().ToList() ?? [],
            results.Albums?.Items.OfType<SimplifiedAlbum>().ToList() ?? [],
            results.Artists?.Items.OfType<Artist>().ToList() ?? [],
            results.Playlists?.Items.OfType<SimplifiedPlaylist>().ToList() ?? []);
    }

    /// <summary>
    /// All of Liked Songs, newest first. Kept on disk: when nothing changed
    /// this costs one request, and newly liked songs are added from the
    /// first page without reloading the rest.
    /// </summary>
    public async Task<IReadOnlyList<TrackInfo>> GetAllLikedSongsAsync(CancellationToken cancellationToken)
    {
        await _likedLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = _lists.Load(LikedSongsKey);
            var first = await GetLikedSongsAsync(0, cancellationToken).ConfigureAwait(false);
            if (cached is not null && TryExtend(cached.Tracks, first) is { } extended)
            {
                if (extended.Count != cached.Tracks.Count)
                {
                    SaveList(LikedSongsKey, null, extended);
                }

                return extended;
            }

            var all = await LoadRestAsync(first, (offset, ct) => GetLikedSongsAsync(offset, ct), cancellationToken).ConfigureAwait(false);
            SaveList(LikedSongsKey, null, all);
            return all;
        }
        finally
        {
            _likedLock.Release();
        }
    }

    /// <summary>
    /// All of a playlist's songs in the playlist's order. Kept on disk per
    /// playlist version (<paramref name="snapshotId"/>, from the sidebar), so
    /// an unchanged playlist costs no requests at all.
    /// </summary>
    public async Task<FullTrackList> GetAllPlaylistTracksAsync(string playlistId, string? snapshotId, CancellationToken cancellationToken)
    {
        var key = "playlist-" + playlistId;
        snapshotId ??= Snapshot?.Playlists.FirstOrDefault(p => p.Id == playlistId)?.SnapshotId;
        if (snapshotId is not null && _lists.Load(key) is { } cached && cached.Version == snapshotId)
        {
            return new FullTrackList(cached.Tracks, ItemsHidden: false);
        }

        var first = await GetPlaylistTracksAsync(playlistId, 0, cancellationToken).ConfigureAwait(false);
        if (first.ItemsHidden)
        {
            return FullTrackList.Hidden;
        }

        var all = await LoadRestAsync(first, (offset, ct) => GetPlaylistTracksAsync(playlistId, offset, ct), cancellationToken).ConfigureAwait(false);
        if (snapshotId is not null)
        {
            SaveList(key, snapshotId, all);
        }

        return new FullTrackList(all, ItemsHidden: false);
    }

    /// <summary>An album's header and all of its songs.</summary>
    public async Task<(Album Album, IReadOnlyList<TrackInfo> Tracks)> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var album = await _api.GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
        var simplified = album.ToSimplified();
        var tracks = new List<TrackInfo>();
        var page = album.Tracks;
        var offset = 0;
        while (page is not null)
        {
            tracks.AddRange(page.Items.Select((PlayableItem? t, int i) => TrackInfo.From(t, null, offset + i, simplified)).OfType<TrackInfo>());
            offset += page.Items.Count;
            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            page = await _api.GetAlbumTracksAsync(albumId, offset, PageSize, cancellationToken).ConfigureAwait(false);
        }

        return (album, tracks);
    }

    /// <summary>Tells listeners (the Liked Songs page, mixes) that Liked Songs changed, and forgets the stored copy's newest page.</summary>
    public void NotifyLikedSongsChanged() => LikedSongsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Forgets every stored song list (on sign-out).</summary>
    public void ClearStoredLists() => _lists.Clear();

    public void Dispose() => _likedLock.Dispose();

    /// <summary>Whether Spotify will list this playlist's songs for the signed-in user.</summary>
    public bool CanListSongs(SimplifiedPlaylist playlist) =>
        playlist.Collaborative || (Snapshot?.User?.Id is { } me && playlist.Owner?.Id == me);

    /// <summary>
    /// The stored list with the songs liked since then added on top, or null
    /// when the first page does not line up with it (songs were removed or
    /// many were added), so the whole list must be loaded again.
    /// </summary>
    internal static List<TrackInfo>? TryExtend(IReadOnlyList<TrackInfo> stored, TrackListPage first)
    {
        if (stored.Count == 0)
        {
            return first.Total == 0 ? [] : null;
        }

        var anchor = stored[0];
        var index = -1;
        for (var i = 0; i < first.Tracks.Count; i++)
        {
            if (first.Tracks[i].Uri == anchor.Uri && first.Tracks[i].AddedAt == anchor.AddedAt)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || stored.Count + index != first.Total)
        {
            return null;
        }

        // The rest of the first page must match what is stored after the anchor.
        for (var i = index; i < first.Tracks.Count && i - index < stored.Count; i++)
        {
            if (first.Tracks[i].Uri != stored[i - index].Uri)
            {
                return null;
            }
        }

        var result = new List<TrackInfo>(first.Total);
        result.AddRange(first.Tracks.Take(index));
        result.AddRange(stored);
        return Renumber(result);
    }

    private static List<TrackInfo> Renumber(List<TrackInfo> tracks)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Position != i)
            {
                tracks[i] = tracks[i] with { Position = i };
            }
        }

        return tracks;
    }

    /// <summary>Loads the pages after <paramref name="first"/>, a few at a time, and returns every song in order.</summary>
    private static async Task<List<TrackInfo>> LoadRestAsync(
        TrackListPage first,
        Func<int, CancellationToken, Task<TrackListPage>> loadPage,
        CancellationToken cancellationToken)
    {
        var all = new List<TrackInfo>(Math.Max(first.Total, first.Tracks.Count));
        all.AddRange(first.Tracks);
        if (!first.HasMore)
        {
            return Renumber(all);
        }

        // Offsets are known from the total, so a few pages can load at once.
        var offsets = new List<int>();
        for (var offset = first.NextOffset; offset < first.Total; offset += PageSize)
        {
            offsets.Add(offset);
        }

        foreach (var batch in offsets.Chunk(ParallelPages))
        {
            var pages = await Task.WhenAll(batch.Select(o => loadPage(o, cancellationToken))).ConfigureAwait(false);
            foreach (var page in pages)
            {
                all.AddRange(page.Tracks);
            }
        }

        return Renumber(all);
    }

    private void SaveList(string key, string? version, List<TrackInfo> tracks) =>
        _lists.Save(new CachedTrackList { Key = key, Version = version, SavedAt = _time.GetUtcNow(), Tracks = tracks });

    private static TrackListPage ToPage(Page<PlaylistEntry> page, int offset)
    {
        var tracks = page.Items
            .Select((PlaylistEntry? e, int i) => TrackInfo.From(e?.Playable, TrackInfo.ParseTime(e?.AddedAt), offset + i))
            .OfType<TrackInfo>()
            .ToList();
        return new TrackListPage(tracks, page.Total, page.HasMore, ItemsHidden: false, offset + page.Items.Count);
    }
}

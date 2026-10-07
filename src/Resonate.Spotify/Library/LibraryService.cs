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
public sealed class LibraryService
{
    public const int PageSize = SpotifyWebApi.MaxPageLimit;

    private readonly ISpotifyWebApi _api;
    private readonly LibraryCache? _cache;
    private readonly TimeProvider _time;

    public LibraryService(ISpotifyWebApi api, LibraryCache? cache, TimeProvider? time = null)
    {
        _api = api;
        _cache = cache;
        _time = time ?? TimeProvider.System;
        Snapshot = cache?.Load();
    }

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
        var tracks = page.Items.Select(s => TrackInfo.From(s?.Track)).OfType<TrackInfo>().ToList();
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
            results.Tracks?.Items.Select(TrackInfo.From).OfType<TrackInfo>().ToList() ?? [],
            results.Albums?.Items.OfType<SimplifiedAlbum>().ToList() ?? [],
            results.Artists?.Items.OfType<Artist>().ToList() ?? [],
            results.Playlists?.Items.OfType<SimplifiedPlaylist>().ToList() ?? []);
    }

    /// <summary>Whether Spotify will list this playlist's songs for the signed-in user.</summary>
    public bool CanListSongs(SimplifiedPlaylist playlist) =>
        playlist.Collaborative || (Snapshot?.User?.Id is { } me && playlist.Owner?.Id == me);

    private static TrackListPage ToPage(Page<PlaylistEntry> page, int offset)
    {
        var tracks = page.Items.Select(e => TrackInfo.From(e?.Playable)).OfType<TrackInfo>().ToList();
        return new TrackListPage(tracks, page.Total, page.HasMore, ItemsHidden: false, offset + page.Items.Count);
    }
}
